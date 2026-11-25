using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QLTiecCuoi.Desktop.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop;

public partial class ContractWindow : Window
{
    private readonly ApiClient _api;
    private ContractDto _c;

    /// <summary>true nếu trạng thái hợp đồng hoặc sảnh đã thay đổi (lịch cần tải lại).</summary>
    public bool Changed { get; private set; }

    public ContractWindow(ApiClient api, ContractDto contract)
    {
        InitializeComponent();
        _api = api;
        _c = contract;
        MethodBox.ItemsSource = Enum.GetValues<PaymentMethod>().Select(m => new ComboBoxItem { Content = Labels.Of(m), Tag = m }).ToList();
        MethodBox.SelectedIndex = 1;
        Show(contract);
    }

    private void Show(ContractDto c)
    {
        _c = c;
        Title = $"Hợp đồng {c.Code}";
        CodeText.Text = $"Hợp đồng {c.Code}";
        StatusText.Text = Labels.Of(c.Status);
        StatusBadge.Background = Brush(c.Status switch
        {
            ContractStatus.AwaitingDeposit => "#FFF1C9",
            ContractStatus.Deposited => "#E3F4E6",
            _ => "#EEEEEE",
        });

        CustomerText.Text = $"{c.CustomerName} · {c.CustomerPhone}" + (c.CustomerIdNumber is null ? "" : $" · CCCD {c.CustomerIdNumber}");
        AddressText.Text = c.CustomerAddress ?? "(chưa có)";
        EventText.Text = $"{c.HallName}, ca {Labels.Of(c.Shift).ToLower()} {c.Date:dd/MM/yyyy}, {c.Tables} bàn";
        TotalText.Text = Labels.Money(c.Total);
        DepositText.Text = $"{Labels.Money(c.DepositRequired)} ({c.DepositPercent:0.##}%)";
        PaidText.Text = Labels.Money(c.Paid);
        RemainingText.Text = Labels.Money(c.Remaining);

        if (c.Liquidation is { } l)
        {
            HoldBanner.Visibility = Visibility.Visible;
            HoldBanner.Background = Brush(c.Remaining > 0 ? "#FFF1C9" : "#E3F4E6");
            var tail = c.Remaining switch
            {
                > 0 => $"Khách còn phải trả {Labels.Money(c.Remaining)}.",
                < 0 => $"Nhà hàng cần hoàn lại khách {Labels.Money(-c.Remaining)}.",
                _ => "Khách đã thanh toán đủ.",
            };
            HoldText.Text = $"Đã thanh lý {l.LiquidatedAt?.ToLocalTime():dd/MM/yyyy}: {l.ActualTables} bàn thực tế"
                            + (l.ExtraTotal > 0 ? $", phát sinh {Labels.Money(l.ExtraTotal)}" : "")
                            + $", quyết toán {Labels.Money(l.FinalTotal)}. {tail}";
            TotalText.Text = $"{Labels.Money(c.Total)} → quyết toán {Labels.Money(l.FinalTotal)}";
            PayBox.Header = "Thu tiền thanh toán cuối";
            AmountBox.Text = c.Remaining > 0 ? c.Remaining.ToString("0") : "";
        }
        else if (c.Status == ContractStatus.AwaitingDeposit)
        {
            HoldBanner.Background = Brush("#FFF1C9");
            var need = Math.Max(0, c.DepositRequired - c.Paid);
            HoldBanner.Visibility = Visibility.Visible;
            HoldText.Text = c.HoldExpiresAt is { } exp
                ? $"Cần thu thêm {Labels.Money(need)} để chốt sảnh. Giữ chỗ hết hạn lúc {exp.ToLocalTime():HH:mm dd/MM/yyyy}; quá hạn chưa đủ cọc thì sảnh tự nhả."
                : $"Cần thu thêm {Labels.Money(need)} để chốt sảnh.";
            AmountBox.Text = need.ToString("0");
        }
        else
        {
            HoldBanner.Visibility = Visibility.Collapsed;
            AmountBox.Text = "";
        }

        var open = c.Status != ContractStatus.Cancelled && c.Remaining > 0;
        PayBox.IsEnabled = open;
        LiquidateButton.IsEnabled = c.Status == ContractStatus.Deposited;
        CancelButton.IsEnabled = c.Status == ContractStatus.AwaitingDeposit && c.Payments.Count == 0;

        PaymentsGrid.ItemsSource = c.Payments.Select(p => new
        {
            When = p.PaidAt.ToLocalTime().ToString("HH:mm dd/MM/yyyy"),
            Kind = Labels.Of(p.Kind),
            Amount = Labels.Money(p.Amount),
            Method = Labels.Of(p.Method),
            p.Note,
        }).ToList();
    }

    private async void Pay_Click(object sender, RoutedEventArgs e)
    {
        PayError.Text = "";
        var raw = AmountBox.Text.Replace(".", "").Replace(",", "").Replace(" ", "").Trim();
        if (!decimal.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            PayError.Text = "Số tiền phải là số nguyên dương, ví dụ 39006000.";
            return;
        }
        var method = (PaymentMethod)((ComboBoxItem)MethodBox.SelectedItem).Tag;
        if (MessageBox.Show(this, $"Ghi nhận {Labels.Money(amount)} ({Labels.Of(method).ToLower()}) cho hợp đồng {_c.Code}?",
                "Xác nhận thu tiền", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        PayButton.IsEnabled = false;
        try
        {
            var before = _c.Status;
            Show(await _api.PayAsync(_c.Id, new PaymentRequest(amount, method, PayNoteBox.Text)));
            PayNoteBox.Text = "";
            Changed = true;
            if (before == ContractStatus.AwaitingDeposit && _c.Status == ContractStatus.Deposited)
                MessageBox.Show(this, $"Đã đủ cọc. {_c.HallName} ngày {_c.Date:dd/MM/yyyy} đã được chốt cho khách {_c.CustomerName}.",
                    "Đã chốt sảnh", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ApiException ex)
        {
            PayError.Text = ex.Message;
        }
        finally
        {
            PayButton.IsEnabled = true;
        }
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Hủy hợp đồng {_c.Code}? Lượt giữ sảnh vẫn được giữ đến hết hạn.", "Hủy hợp đồng",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            Show(await _api.CancelContractAsync(_c.Id));
            Changed = true;
        }
        catch (ApiException ex)
        {
            MessageBox.Show(this, ex.Message, "Không hủy được", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Liquidate_Click(object sender, RoutedEventArgs e)
    {
        var w = new LiquidationWindow(_api, _c) { Owner = this };
        if (w.ShowDialog() == true && w.Result is { } result)
        {
            Show(result);
            Changed = true;
        }
    }

    private async void Pdf_Click(object sender, RoutedEventArgs e) =>
        await PdfSaver.SaveAndOpenAsync(this, $"{_c.Code}.pdf", () => _api.ContractPdfAsync(_c.Id));

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
