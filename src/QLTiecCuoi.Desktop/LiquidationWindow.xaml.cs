using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using QLTiecCuoi.Desktop.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop;

/// <summary>Dòng phát sinh sửa được trong bảng.</summary>
public class ExtraRow
{
    public string Description { get; set; } = "";
    public string Amount { get; set; } = "";
}

public record BreakdownRow(string Label, string Value, bool Strong = false);

/// <summary>Thanh lý hợp đồng sau tiệc (UC05): nhập số bàn thực tế, phát sinh, xem quyết toán rồi xác nhận.</summary>
public partial class LiquidationWindow : Window
{
    private readonly ApiClient _api;
    private readonly ContractDto _contract;
    private readonly ObservableCollection<ExtraRow> _extras = [];
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private int _version;

    public ContractDto? Result { get; private set; }

    public LiquidationWindow(ApiClient api, ContractDto contract)
    {
        InitializeComponent();
        _api = api;
        _contract = contract;
        HeaderText.Text = $"Thanh lý hợp đồng {contract.Code}";
        InfoText.Text = $"{contract.CustomerName} · {contract.HallName}, ca {Labels.Of(contract.Shift).ToLower()} {contract.Date:dd/MM/yyyy}";
        TablesBox.Text = contract.Tables.ToString();
        TablesHint.Text = $"(hợp đồng {contract.Tables} bàn)";
        ExtrasGrid.ItemsSource = _extras;

        _debounce.Tick += async (_, _) => { _debounce.Stop(); await PreviewAsync(); };
        TablesBox.TextChanged += (_, _) => Changed();
        NoteBox.TextChanged += (_, _) => Changed();
        _extras.CollectionChanged += (_, _) => Changed();
        Loaded += async (_, _) => await PreviewAsync();
    }

    private void ExtrasGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(Changed, DispatcherPriority.Background);

    private void Changed()
    {
        ConfirmButton.IsEnabled = false;
        _debounce.Stop();
        _debounce.Start();
    }

    private LiquidationRequest? BuildRequest()
    {
        if (!int.TryParse(TablesBox.Text.Trim(), out var tables))
            return Fail("Số bàn thực tế phải là số nguyên.");
        var extras = new List<ExtraChargeDto>();
        foreach (var x in _extras.Where(x => !string.IsNullOrWhiteSpace(x.Description) || !string.IsNullOrWhiteSpace(x.Amount)))
        {
            var raw = (x.Amount ?? "").Replace(".", "").Replace(",", "").Replace(" ", "");
            if (!decimal.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
                return Fail($"Số tiền của \"{x.Description}\" phải là số nguyên dương, ví dụ 420000.");
            extras.Add(new ExtraChargeDto(x.Description, amount));
        }
        return new LiquidationRequest(tables, extras, NoteBox.Text);

        LiquidationRequest? Fail(string message)
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
            var r = await _api.PreviewLiquidationAsync(_contract.Id, req);
            if (version != _version) return;
            Show(r);
            ConfirmButton.IsEnabled = true;
        }
        catch (ApiException ex)
        {
            if (version != _version) return;
            ErrorText.Text = ex.Message;
        }
    }

    private void Show(LiquidationDto r)
    {
        var rows = new List<BreakdownRow>
        {
            new("Tiền sảnh", Labels.Money(r.HallPrice)),
            new($"Thực đơn: {Labels.Money(r.MenuPerTable)}/bàn × {r.ActualTables} bàn", Labels.Money(r.MenuTotal)),
            new("Dịch vụ theo hợp đồng", Labels.Money(r.ServiceTotal)),
        };
        rows.AddRange(r.Extras.Select(x => new BreakdownRow($"Phát sinh: {x.Description}", Labels.Money(x.Amount))));
        rows.Add(new("Cộng trước thuế", Labels.Money(r.Subtotal)));
        rows.Add(new($"VAT {r.VatPercent:0.##}%", Labels.Money(r.VatAmount)));
        rows.Add(new("TỔNG QUYẾT TOÁN", Labels.Money(r.FinalTotal), true));
        rows.Add(new("Giá trị hợp đồng ban đầu", Labels.Money(r.ContractTotal)));
        rows.Add(new("Chênh lệch so với hợp đồng", SignedMoney(r.FinalTotal - r.ContractTotal)));
        rows.Add(new("Khách đã thanh toán (cọc)", Labels.Money(r.Paid)));
        BreakdownGrid.ItemsSource = rows;

        if (r.Remaining >= 0)
        {
            ResultText.Text = $"Khách cần thanh toán thêm: {Labels.Money(r.Remaining)}";
            ResultBanner.Background = Brush("#FFF1C9");
        }
        else
        {
            ResultText.Text = $"Nhà hàng hoàn lại khách: {Labels.Money(-r.Remaining)}";
            ResultBanner.Background = Brush("#E3F4E6");
        }
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var req = BuildRequest();
        if (req is null) return;
        if (MessageBox.Show(this, $"Xác nhận thanh lý hợp đồng {_contract.Code} với {req.ActualTables} bàn thực tế?\nSau khi thanh lý không sửa được số liệu.",
                "Xác nhận thanh lý", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        ConfirmButton.IsEnabled = false;
        try
        {
            Result = await _api.LiquidateAsync(_contract.Id, req);
            DialogResult = true;
        }
        catch (ApiException ex)
        {
            ErrorText.Text = ex.Message;
            ConfirmButton.IsEnabled = true;
        }
    }

    private static string SignedMoney(decimal v) => v switch
    {
        > 0 => "+" + Labels.Money(v),
        < 0 => "−" + Labels.Money(-v),
        _ => Labels.Money(0),
    };

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
