using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using QLTiecCuoi.Desktop.Services;
using QLTiecCuoi.Desktop.ViewModels;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop;

public partial class MainWindow : Window
{
    private readonly ApiClient _api = new();
    private List<HallDto> _halls = [];
    private const int FixedColumns = 3; // Ngày, Thứ, Ca

    public MainWindow()
    {
        InitializeComponent();
        FromPicker.SelectedDate = DateTime.Today.AddDays(1);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void Load_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        FromPicker.SelectedDate = (FromPicker.SelectedDate ?? DateTime.Today).AddDays(7);
        await ReloadAsync();
    }

    private async void NewQuote_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureHallsAsync()) return;
        OpenQuote(null);
    }

    private void Report_Click(object sender, RoutedEventArgs e) =>
        new ReportWindow(_api) { Owner = this }.Show();

    private void Catalog_Click(object sender, RoutedEventArgs e) =>
        new CatalogWindow(_api) { Owner = this }.ShowDialog();

    private void OpenQuote(QuotePrefill? prefill)
    {
        var w = new QuoteWindow(_api, _halls, prefill) { Owner = this };
        if (w.ShowDialog() == true && w.Saved is { } q)
            StatusText.Text = $"Đã lưu báo giá #{q.Id} cho {q.CustomerName}: {Labels.Money(q.Total)}.";
    }

    private static QuotePrefill Prefill(BookingDto b) =>
        new(b.HallId, b.Date, b.Shift, b.CustomerName, b.CustomerPhone, b.ExpectedTables, b.Id);

    private async Task<bool> EnsureHallsAsync()
    {
        if (_halls.Count > 0) return true;
        await ReloadAsync();
        return _halls.Count > 0;
    }

    private int Days => int.Parse(((ComboBoxItem)DaysBox.SelectedItem).Content.ToString()!);

    private async Task ReloadAsync()
    {
        var from = DateOnly.FromDateTime(FromPicker.SelectedDate ?? DateTime.Today);
        var to = from.AddDays(Days - 1);
        StatusText.Text = "Đang tải lịch...";
        try
        {
            if (_halls.Count == 0)
            {
                _halls = await _api.GetHallsAsync();
                BuildHallColumns();
            }
            var slots = await _api.GetScheduleAsync(from, to);
            Grid.ItemsSource = slots
                .GroupBy(s => (s.Date, s.Shift))
                .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Shift)
                .Select(g => new ScheduleRow
                {
                    Date = g.Key.Date,
                    Shift = g.Key.Shift,
                    Cells = _halls.Select(h => new SlotCell(g.Single(s => s.HallId == h.Id))).ToList(),
                })
                .ToList();
            var free = slots.Count(s => s.Status == SlotStatus.Available);
            StatusText.Text = $"Từ {from:dd/MM/yyyy} đến {to:dd/MM/yyyy}: {free}/{slots.Count} ô còn trống · API {_api.BaseUrl}";
        }
        catch (ApiException ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Không tải được lịch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Mỗi sảnh một cột; ô là nút bấm tô màu theo trạng thái.</summary>
    private void BuildHallColumns()
    {
        while (Grid.Columns.Count > FixedColumns) Grid.Columns.RemoveAt(FixedColumns);
        for (var i = 0; i < _halls.Count; i++)
        {
            var template = (DataTemplate)XamlReader.Parse($$"""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <Button Tag="{Binding Cells[{{i}}]}" Content="{Binding Cells[{{i}}].Text}"
                          ToolTip="{Binding Cells[{{i}}].ToolTip}" Background="{Binding Cells[{{i}}].Background}"
                          BorderThickness="0" Margin="2" Cursor="Hand" HorizontalContentAlignment="Left" Padding="8,0"/>
                </DataTemplate>
                """);
            Grid.Columns.Add(new DataGridTemplateColumn
            {
                Header = $"{_halls[i].Name} ({_halls[i].MaxTables} bàn)",
                CellTemplate = template,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 160,
            });
        }
        Grid.AddHandler(Button.ClickEvent, new RoutedEventHandler(Cell_Click));
    }

    private async void Cell_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { Tag: SlotCell cell }) return;
        var slot = cell.Slot;
        var hall = _halls.Single(h => h.Id == slot.HallId);

        switch (slot.Status)
        {
            case SlotStatus.Available:
                var dlg = new HoldWindow(_api, hall, slot.Date, slot.Shift) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    StatusText.Text = $"Đã giữ {hall.Name} cho {dlg.Result!.CustomerName}.";
                    await ReloadAsync();
                    var next = MessageBox.Show(this, $"Đã giữ {hall.Name} cho khách {dlg.Result.CustomerName}.\nLập báo giá cho khách này ngay?",
                        "Đã giữ sảnh", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (next == MessageBoxResult.Yes) OpenQuote(Prefill(dlg.Result));
                }
                else await ReloadAsync();
                break;

            case SlotStatus.Held:
            case SlotStatus.Deposited:
                // Báo giá, hợp đồng, thu cọc, hủy giữ chỗ cho lượt đặt sảnh này.
                var w = new SlotWindow(_api, _halls, slot.BookingId!.Value) { Owner = this };
                w.ShowDialog();
                await ReloadAsync();
                break;
        }
    }
}
