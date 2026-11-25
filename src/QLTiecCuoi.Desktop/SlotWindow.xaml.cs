using System.Windows;
using QLTiecCuoi.Desktop.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop;

/// <summary>
/// Màn hình một lượt đặt sảnh (ô đang giữ hoặc đã cọc): báo giá, hợp đồng, hủy giữ chỗ.
/// Luồng chuẩn: giữ sảnh → lập báo giá → lập hợp đồng → thu cọc.
/// </summary>
public partial class SlotWindow : Window
{
    private readonly ApiClient _api;
    private readonly List<HallDto> _halls;
    private readonly int _bookingId;
    private BookingDto? _booking;
    private List<QuoteSummaryDto> _quotes = [];
    private ContractDto? _contract;

    /// <summary>true nếu có thay đổi ảnh hưởng tới lịch sảnh.</summary>
    public bool Changed { get; private set; }

    public SlotWindow(ApiClient api, List<HallDto> halls, int bookingId)
    {
        InitializeComponent();
        _api = api;
        _halls = halls;
        _bookingId = bookingId;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        ErrorText.Text = "";
        try
        {
            _booking = await _api.GetBookingAsync(_bookingId);
            _quotes = await _api.ListQuotesAsync(_bookingId);
            _contract = (await _api.ListContractsAsync(_bookingId)).FirstOrDefault(c => c.Status != ContractStatus.Cancelled);
        }
        catch (ApiException ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }

        var b = _booking;
        HeaderText.Text = $"{b.HallName} · ca {Labels.Of(b.Shift).ToLower()} · {b.Date:dd/MM/yyyy}";
        CustomerText.Text = $"Khách: {b.CustomerName} · {b.CustomerPhone} · {b.ExpectedTables} bàn dự kiến";
        HoldText.Text = b.Status switch
        {
            BookingStatus.Held => $"Đang giữ chỗ đến {b.HoldExpiresAt?.ToLocalTime():HH:mm dd/MM/yyyy}.",
            BookingStatus.Deposited => "Đã đặt cọc, sảnh đã chốt.",
            _ => "Lượt giữ chỗ đã hủy hoặc hết hạn.",
        };

        var latest = _quotes.FirstOrDefault();
        QuoteText.Text = latest is null
            ? "Chưa có báo giá."
            : $"{_quotes.Count} báo giá. Mới nhất: BG-{latest.Id:00000}, {Labels.Money(latest.Total)}, lập {latest.CreatedAt.ToLocalTime():dd/MM/yyyy}.";
        QuotePdfButton.IsEnabled = latest is not null;
        NewQuoteButton.IsEnabled = b.Status == BookingStatus.Held && _contract is null;

        if (_contract is { } c)
        {
            ContractText.Text = $"{c.Code} · {Labels.Of(c.Status)} · đã thu {Labels.Money(c.Paid)} / cọc {Labels.Money(c.DepositRequired)}";
            NewContractForm.Visibility = Visibility.Collapsed;
            ContractButton.Content = "Xem hợp đồng, thu cọc";
            ContractButton.IsEnabled = true;
        }
        else
        {
            ContractText.Text = latest is null
                ? "Cần lập báo giá trước khi lập hợp đồng."
                : $"Hợp đồng sẽ lập theo báo giá mới nhất (BG-{latest.Id:00000}).";
            NewContractForm.Visibility = latest is null ? Visibility.Collapsed : Visibility.Visible;
            ContractButton.Content = "Lập hợp đồng";
            ContractButton.IsEnabled = latest is not null && b.Status == BookingStatus.Held;
        }

        CancelHoldButton.IsEnabled = b.Status == BookingStatus.Held && _contract is null;
    }

    private async void NewQuote_Click(object sender, RoutedEventArgs e)
    {
        var b = _booking!;
        var w = new QuoteWindow(_api, _halls, new QuotePrefill(b.HallId, b.Date, b.Shift, b.CustomerName, b.CustomerPhone, b.ExpectedTables, b.Id)) { Owner = this };
        w.ShowDialog();
        await ReloadAsync();
    }

    private async void QuotePdf_Click(object sender, RoutedEventArgs e)
    {
        var q = _quotes.First();
        await PdfSaver.SaveAndOpenAsync(this, $"BG-{q.Id:00000}.pdf", () => _api.QuotePdfAsync(q.Id));
    }

    private async void Contract_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        try
        {
            var contract = _contract;
            if (contract is null)
            {
                var q = _quotes.First();
                if (MessageBox.Show(this, $"Lập hợp đồng theo báo giá BG-{q.Id:00000} ({Labels.Money(q.Total)})?",
                        "Lập hợp đồng", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                contract = await _api.CreateContractAsync(new CreateContractRequest(q.Id, AddressBox.Text, IdBox.Text, ContractNoteBox.Text));
            }
            var w = new ContractWindow(_api, contract) { Owner = this };
            w.ShowDialog();
            Changed |= w.Changed;
        }
        catch (ApiException ex)
        {
            ErrorText.Text = ex.Message;
        }
        await ReloadAsync();
    }

    private async void CancelHold_Click(object sender, RoutedEventArgs e)
    {
        var b = _booking!;
        if (MessageBox.Show(this, $"Hủy giữ chỗ của khách {b.CustomerName}? Sảnh sẽ trống lại.", "Hủy giữ chỗ",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            await _api.CancelHoldAsync(b.Id);
            Changed = true;
            Close();
        }
        catch (ApiException ex)
        {
            ErrorText.Text = ex.Message;
        }
    }
}
