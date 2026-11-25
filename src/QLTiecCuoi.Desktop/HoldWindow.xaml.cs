using System.Net;
using System.Windows;
using QLTiecCuoi.Desktop.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop;

public partial class HoldWindow : Window
{
    private readonly ApiClient _api;
    private readonly HallDto _hall;
    private readonly DateOnly _date;
    private readonly Shift _shift;

    public BookingDto? Result { get; private set; }

    public HoldWindow(ApiClient api, HallDto hall, DateOnly date, Shift shift)
    {
        InitializeComponent();
        _api = api;
        _hall = hall;
        _date = date;
        _shift = shift;
        SlotText.Text = $"{hall.Name} · ca {Labels.Of(shift).ToLower()} · ngày {date:dd/MM/yyyy}";
        TablesLabel.Text = $"Số bàn dự kiến * (tối đa {hall.MaxTables})";
        Loaded += (_, _) => NameBox.Focus();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (!int.TryParse(TablesBox.Text.Trim(), out var tables))
        {
            ErrorText.Text = "Số bàn phải là số nguyên.";
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            Result = await _api.HoldAsync(new HoldRequest(
                _hall.Id, _date, _shift, NameBox.Text, PhoneBox.Text, tables, NoteBox.Text));
            DialogResult = true;
        }
        catch (ApiException ex) when (ex.Status == HttpStatusCode.Conflict)
        {
            // Người khác vừa giữ ô này: báo rồi đóng để lịch tải lại.
            MessageBox.Show(this, ex.Message, "Sảnh vừa có người giữ", MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
        }
        catch (ApiException ex)
        {
            ErrorText.Text = ex.Message;
            SaveButton.IsEnabled = true;
        }
    }
}
