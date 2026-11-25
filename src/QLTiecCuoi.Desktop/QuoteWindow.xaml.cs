using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using QLTiecCuoi.Desktop.Services;
using QLTiecCuoi.Desktop.ViewModels;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop;

/// <summary>Thông tin điền sẵn khi mở báo giá từ một lượt giữ sảnh.</summary>
public record QuotePrefill(int HallId, DateOnly Date, Shift Shift, string CustomerName, string CustomerPhone,
    int Tables, int? BookingId);

public partial class QuoteWindow : Window
{
    private readonly ApiClient _api;
    private readonly int? _bookingId;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private List<MenuChoice> _menu = [];
    private List<ServiceChoice> _services = [];
    private int _version; // bỏ qua kết quả xem trước đã cũ
    private bool _loaded;

    public QuoteDto? Saved { get; private set; }

    public QuoteWindow(ApiClient api, List<HallDto> halls, QuotePrefill? prefill = null)
    {
        InitializeComponent();
        _api = api;
        _bookingId = prefill?.BookingId;

        HallBox.ItemsSource = halls;
        ShiftBox.ItemsSource = Enum.GetValues<Shift>().Select(s => new ComboBoxItem { Content = Labels.Of(s), Tag = s }).ToList();
        HallBox.SelectedItem = halls.FirstOrDefault(h => h.Id == prefill?.HallId) ?? halls.FirstOrDefault();
        ShiftBox.SelectedIndex = prefill?.Shift == Shift.Lunch ? 0 : 1;
        DatePick.SelectedDate = prefill?.Date.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today.AddDays(30);
        NameBox.Text = prefill?.CustomerName ?? "";
        PhoneBox.Text = prefill?.CustomerPhone ?? "";
        TablesBox.Text = (prefill?.Tables ?? 30).ToString();

        if (_bookingId is not null)
        {
            // Báo giá cho lượt đã giữ sảnh: không cho đổi sảnh, ngày, ca.
            HallBox.IsEnabled = DatePick.IsEnabled = ShiftBox.IsEnabled = false;
            Title = $"Lập báo giá – lượt giữ sảnh #{_bookingId}";
        }

        _debounce.Tick += async (_, _) => { _debounce.Stop(); await PreviewAsync(); };
        foreach (var tb in new[] { NameBox, PhoneBox, TablesBox, NoteBox }) tb.TextChanged += (_, _) => Changed();
        HallBox.SelectionChanged += (_, _) => Changed();
        ShiftBox.SelectionChanged += (_, _) => Changed();
        DatePick.SelectedDateChanged += (_, _) => Changed();
        Loaded += async (_, _) => await LoadCatalogAsync();
    }

    private async Task LoadCatalogAsync()
    {
        try
        {
            _menu = (await _api.GetMenuAsync()).Select(m => new MenuChoice(m)).ToList();
            _services = (await _api.GetServicesAsync()).Select(s => new ServiceChoice(s)).ToList();
        }
        catch (ApiException ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }

        foreach (var c in _menu) c.PropertyChanged += (_, _) => Changed();
        foreach (var c in _services) c.PropertyChanged += (_, _) => Changed();

        var view = new ListCollectionView(_menu);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MenuChoice.Group)));
        MenuList.ItemsSource = view;
        ServiceList.ItemsSource = _services;
        _loaded = true;
        Changed();
    }

    private void Changed()
    {
        if (!_loaded) return;
        SaveButton.IsEnabled = false;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Đọc form thành yêu cầu báo giá; trả về null và báo lỗi nếu nhập sai.</summary>
    private QuoteRequest? BuildRequest()
    {
        if (HallBox.SelectedItem is not HallDto hall || DatePick.SelectedDate is not { } date)
            return Fail("Vui lòng chọn sảnh và ngày.");
        if (!int.TryParse(TablesBox.Text.Trim(), out var tables))
            return Fail("Số bàn phải là số nguyên.");

        var services = new List<QuoteServiceRequest>();
        foreach (var s in _services.Where(s => s.IsSelected))
        {
            if (!int.TryParse(s.Quantity.Trim(), out var q) || q <= 0)
                return Fail($"Số lượng \"{s.Item.Name}\" phải là số nguyên dương.");
            services.Add(new QuoteServiceRequest(s.Item.Id, q));
        }

        var shift = (Shift)((ComboBoxItem)ShiftBox.SelectedItem).Tag;
        return new QuoteRequest(hall.Id, DateOnly.FromDateTime(date), shift, tables, NameBox.Text, PhoneBox.Text,
            _menu.Where(m => m.IsSelected).Select(m => m.Item.Id).ToList(), services, _bookingId, NoteBox.Text);

        QuoteRequest? Fail(string message)
        {
            ErrorText.Text = message;
            return null;
        }
    }

    private async Task PreviewAsync()
    {
        ErrorText.Text = "";
        var req = BuildRequest();
        if (req is null) return;
        var version = ++_version;
        try
        {
            var quote = await _api.PreviewQuoteAsync(req);
            if (version != _version) return;
            Show(quote);
            SaveButton.IsEnabled = true;
        }
        catch (ApiException ex)
        {
            if (version != _version) return;
            ErrorText.Text = ex.Message;
        }
    }

    private void Show(QuoteDto q)
    {
        var rows = new List<QuoteRow> { new("Sảnh", $"Thuê {q.HallName}, ca {Labels.Of(q.Shift).ToLower()}", Labels.Money(q.HallPrice), "1", Labels.Money(q.HallPrice)) };
        rows.AddRange(q.MenuLines.Select(l => new QuoteRow("Thực đơn", l.Name, Labels.Money(l.UnitPrice), $"{l.Quantity} {l.Unit}", Labels.Money(l.Amount))));
        rows.AddRange(q.ServiceLines.Select(l => new QuoteRow("Dịch vụ", l.Name, Labels.Money(l.UnitPrice), $"{l.Quantity} {l.Unit}", Labels.Money(l.Amount))));
        LinesGrid.ItemsSource = rows;

        SubtotalText.Text = Labels.Money(q.Subtotal);
        VatLabel.Text = $"VAT {q.VatPercent:0.##}%";
        VatText.Text = Labels.Money(q.VatAmount);
        TotalText.Text = Labels.Money(q.Total);

        // Làm tròn đến nghìn đồng (decimal không nhận số chữ số âm trong Math.Round).
        var perGuest = Math.Round(q.Total / (q.Tables * 10) / 1000m) * 1000m;
        (SlotText.Text, SlotBanner.Background) = q.SlotStatus switch
        {
            SlotStatus.Available => ($"Sảnh còn trống ngày {q.Date:dd/MM/yyyy}. Khoảng {Labels.Money(perGuest)}/khách.", Brush("#E3F4E6")),
            SlotStatus.Held when _bookingId is not null => ($"Sảnh đang được giữ cho khách này. Khoảng {Labels.Money(perGuest)}/khách.", Brush("#E3F4E6")),
            SlotStatus.Held => ("Lưu ý: sảnh này đang có khách khác giữ chỗ vào ngày, ca đã chọn.", Brush("#FFF1C9")),
            _ => ("Lưu ý: sảnh này đã có khách đặt cọc vào ngày, ca đã chọn.", Brush("#F8D7DA")),
        };
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var req = BuildRequest();
        if (req is null) return;
        SaveButton.IsEnabled = false;
        try
        {
            Saved = await _api.CreateQuoteAsync(req);
            MessageBox.Show(this,
                $"Đã lưu báo giá #{Saved.Id}: {Labels.Money(Saved.Total)}.\nHiệu lực đến {Saved.ValidUntil:dd/MM/yyyy}.",
                "Đã lưu báo giá", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (ApiException ex)
        {
            ErrorText.Text = ex.Message;
            SaveButton.IsEnabled = true;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _debounce.Stop();
        base.OnClosing(e);
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
