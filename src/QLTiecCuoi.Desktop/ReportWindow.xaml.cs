using System.Windows;
using QLTiecCuoi.Desktop.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop;

public record MonthOption(DateOnly Month, string Label);

/// <summary>Một dòng báo cáo đã định dạng, kèm độ dài thanh so sánh.</summary>
public record ReportRow(string Label, int Events, int Tables, string Revenue, int UpcomingEvents, string UpcomingValue,
    double RevenueBar, double UpcomingBar);

/// <summary>Báo cáo doanh thu mùa cưới (UC06).</summary>
public partial class ReportWindow : Window
{
    private const double BarMax = 220;
    private readonly ApiClient _api;

    public ReportWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        var now = DateTime.Today;
        var first = new DateOnly(now.Year, now.Month, 1);
        var months = Enumerable.Range(-12, 25).Select(i => first.AddMonths(i))
            .Select(m => new MonthOption(m, $"{m.Month:00}/{m.Year}")).ToList();
        FromBox.ItemsSource = months;
        ToBox.ItemsSource = months;
        // Mặc định: mùa cưới từ 2 tháng trước đến 4 tháng tới.
        FromBox.SelectedIndex = 10;
        ToBox.SelectedIndex = 16;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async void Load_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        StatusText.Text = "";
        if (FromBox.SelectedItem is not MonthOption from || ToBox.SelectedItem is not MonthOption to) return;
        try
        {
            var r = await _api.RevenueAsync(from.Month, to.Month);
            RevenueText.Text = Labels.Money(r.Total.Revenue);
            EventsText.Text = $"{r.Total.Events} tiệc · {r.Total.Tables} bàn";
            UpcomingText.Text = $"{r.Total.UpcomingEvents} tiệc · {Labels.Money(r.Total.UpcomingValue)}";
            MonthGrid.ItemsSource = Rows(r.ByMonth);
            HallGrid.ItemsSource = Rows(r.ByHall);
        }
        catch (ApiException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private static List<ReportRow> Rows(List<RevenueRowDto> rows)
    {
        var max = rows.Select(x => x.Revenue + x.UpcomingValue).DefaultIfEmpty(0).Max();
        double Bar(decimal v) => max == 0 ? 0 : (double)(v / max) * BarMax;
        return rows.Select(x => new ReportRow(x.Label, x.Events, x.Tables, Labels.Money(x.Revenue),
            x.UpcomingEvents, Labels.Money(x.UpcomingValue), Bar(x.Revenue), Bar(x.UpcomingValue))).ToList();
    }
}
