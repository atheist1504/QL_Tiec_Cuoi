using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using QLTiecCuoi.Desktop.Services;
using QLTiecCuoi.Desktop.ViewModels;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop;

public partial class CatalogWindow : Window
{
    private readonly ApiClient _api;

    public CatalogWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        CategoryColumn.ItemsSource = Enum.GetValues<MenuCategory>()
            .Select(c => new ComboBoxItem { Content = Labels.Of(c), Tag = c }).ToList();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            MenuGrid.ItemsSource = new ObservableCollection<MenuRow>((await _api.GetMenuAsync(includeInactive: true))
                .Select(m => new MenuRow { Id = m.Id, Name = m.Name, Category = m.Category, PricePerTable = m.PricePerTable, IsActive = m.IsActive }));
            ServiceGrid.ItemsSource = new ObservableCollection<ServiceRow>((await _api.GetServicesAsync(includeInactive: true))
                .Select(s => new ServiceRow { Id = s.Id, Name = s.Name, Unit = s.Unit, Price = s.Price, IsActive = s.IsActive }));
        }
        catch (ApiException ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void MenuGrid_RowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not MenuRow row) return;
        // Chờ DataGrid ghi giá trị vào đối tượng rồi mới gửi lên API.
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        await SaveAsync(async () =>
        {
            var saved = await _api.SaveMenuItemAsync(row.Id == 0 ? null : row.Id,
                new MenuItemUpsert(row.Name, row.Category, row.PricePerTable, row.IsActive));
            row.Id = saved.Id;
            return $"Đã lưu món \"{saved.Name}\".";
        });
    }

    private async void ServiceGrid_RowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not ServiceRow row) return;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        await SaveAsync(async () =>
        {
            var saved = await _api.SaveServiceAsync(row.Id == 0 ? null : row.Id,
                new ServiceItemUpsert(row.Name, row.Unit, row.Price, row.IsActive));
            row.Id = saved.Id;
            return $"Đã lưu dịch vụ \"{saved.Name}\".";
        });
    }

    private async Task SaveAsync(Func<Task<string>> save)
    {
        try
        {
            StatusText.Text = await save();
        }
        catch (ApiException ex)
        {
            MessageBox.Show(this, ex.Message, "Không lưu được", MessageBoxButton.OK, MessageBoxImage.Warning);
            await ReloadAsync(); // trả bảng về dữ liệu đúng trên máy chủ
        }
    }
}
