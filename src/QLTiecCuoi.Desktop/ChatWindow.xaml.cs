using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QLTiecCuoi.Desktop.Services;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Desktop;

/// <summary>Một bong bóng tin nhắn trong khung chat.</summary>
public record ChatBubble(string Text, bool FromUser, string? Meta = null, bool IsError = false)
{
    public Brush Background => IsError ? Brushes.MistyRose
        : FromUser ? new SolidColorBrush(Color.FromRgb(0xDC, 0xEB, 0xFB)) : Brushes.White;
    public HorizontalAlignment Align => FromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public Visibility MetaVisibility => string.IsNullOrEmpty(Meta) ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>Khung chat với trợ lý AI (UC07). Thao tác ghi dữ liệu luôn chờ nhân viên xác nhận.</summary>
public partial class ChatWindow : Window
{
    private static readonly Dictionary<string, string> ToolNames = new()
    {
        ["list_halls"] = "danh sách sảnh",
        ["find_available_halls"] = "lịch sảnh trống",
        ["find_bookings"] = "lượt đặt sảnh",
        ["get_menu_and_services"] = "thực đơn, dịch vụ",
        ["estimate_quote"] = "tính báo giá",
        ["hold_hall"] = "giữ sảnh",
        ["create_quote"] = "lưu báo giá",
    };

    private readonly ApiClient _api;
    private readonly ObservableCollection<ChatBubble> _messages = [];
    private string? _conversationId;

    /// <summary>Báo cho màn hình chính tải lại lịch khi chatbot vừa ghi dữ liệu.</summary>
    public event Action? DataChanged;

    public ChatWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        MessageList.ItemsSource = _messages;
        Greet();
        Loaded += (_, _) => InputBox.Focus();
    }

    private void Greet() => _messages.Add(new ChatBubble(
        "Chào bạn! Mình có thể tra sảnh trống, xem thực đơn, tính báo giá, và giữ sảnh hoặc lưu báo giá khi bạn xác nhận.", false));

    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        _conversationId = null;
        _messages.Clear();
        PendingPanel.Visibility = Visibility.Collapsed;
        Suggestions.Visibility = Visibility.Visible;
        Greet();
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter để gửi, Shift+Enter để xuống dòng.
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            Send_Click(sender, e);
        }
    }

    private async void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        InputBox.Text = (string)((Button)sender).Content;
        await SendAsync();
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async Task SendAsync()
    {
        var text = InputBox.Text.Trim();
        if (text.Length == 0 || !SendButton.IsEnabled) return;
        InputBox.Clear();
        Suggestions.Visibility = Visibility.Collapsed;
        _messages.Add(new ChatBubble(text, true));
        await CallAsync(() => _api.ChatAsync(new ChatRequest(_conversationId, text)));
    }

    private async void Approve_Click(object sender, RoutedEventArgs e) => await ConfirmAsync(true);

    private async void Decline_Click(object sender, RoutedEventArgs e) => await ConfirmAsync(false);

    private async Task ConfirmAsync(bool approve)
    {
        if (_conversationId is null) return;
        PendingPanel.Visibility = Visibility.Collapsed;
        _messages.Add(new ChatBubble(approve ? "Xác nhận." : "Không, đừng làm.", true));
        var changed = await CallAsync(() => _api.ChatConfirmAsync(_conversationId, approve));
        if (approve && changed) DataChanged?.Invoke();
    }

    /// <summary>Gọi API, hiện câu trả lời; trả về true nếu thành công.</summary>
    private async Task<bool> CallAsync(Func<Task<ChatReplyDto>> call)
    {
        SetBusy(true);
        var thinking = new ChatBubble("Đang tra cứu...", false);
        _messages.Add(thinking);
        Scroll();
        try
        {
            var reply = await call();
            _messages.Remove(thinking);
            _conversationId = string.IsNullOrEmpty(reply.ConversationId) ? null : reply.ConversationId;
            var meta = reply.ToolsUsed.Count == 0 ? null
                : "Đã dùng: " + string.Join(", ", reply.ToolsUsed.Distinct().Select(t => ToolNames.GetValueOrDefault(t, t)));
            if (!string.IsNullOrWhiteSpace(reply.Text) || meta is not null)
                _messages.Add(new ChatBubble(string.IsNullOrWhiteSpace(reply.Text) ? "(không có nội dung)" : PlainText.FromMarkdown(reply.Text), false, meta));

            PendingList.ItemsSource = reply.PendingActions;
            PendingPanel.Visibility = reply.PendingActions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            return true;
        }
        catch (ApiException ex)
        {
            _messages.Remove(thinking);
            _messages.Add(new ChatBubble(ex.Message, false, IsError: true));
            return false;
        }
        finally
        {
            // Đang chờ xác nhận thì khóa ô nhập để không gửi câu khác chen ngang.
            SetBusy(PendingPanel.Visibility == Visibility.Visible);
            if (PendingPanel.Visibility != Visibility.Visible) InputBox.Focus();
            Scroll();
        }
    }

    private void SetBusy(bool busy)
    {
        SendButton.IsEnabled = !busy;
        InputBox.IsEnabled = !busy;
    }

    private void Scroll() => Dispatcher.BeginInvoke(() => Scroller.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);
}
