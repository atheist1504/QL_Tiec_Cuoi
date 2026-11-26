using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Services.Chat;

public class ChatOptions
{
    /// <summary>Nhà cung cấp: "Gemini" (mặc định), "Claude" hoặc "OpenAICompatible" (Groq, OpenRouter, Ollama...).</summary>
    public string Provider { get; set; } = "Gemini";

    /// <summary>Tên model; bỏ trống thì dùng mặc định của nhà cung cấp.</summary>
    public string? Model { get; set; }

    /// <summary>Địa chỉ API chuẩn OpenAI (chỉ dùng cho Gemini/OpenAICompatible); bỏ trống = Gemini.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Mức suy nghĩ (low, medium, high). Bỏ trống = mặc định của model.</summary>
    public string? Effort { get; set; }

    /// <summary>Cho phép chạy không cần API key (ví dụ Ollama trên máy).</summary>
    public bool AllowNoApiKey { get; set; }

    /// <summary>Số lần gọi model tối đa cho một câu hỏi (mỗi lần có thể gọi nhiều công cụ).</summary>
    public int MaxSteps { get; set; } = 8;

    /// <summary>Hội thoại không dùng quá số phút này thì bị xóa khỏi bộ nhớ.</summary>
    public int ConversationTtlMinutes { get; set; } = 120;
}

/// <summary>Một lời gọi công cụ trong lượt đang chờ: đã có kết quả (công cụ đọc) hoặc chờ xác nhận (công cụ ghi).</summary>
internal record ToolCall(ChatToolRequest Request)
{
    public string? Result { get; set; }
    public bool IsError { get; set; }
}

internal class ChatConversation(string id, IChatSession session)
{
    public string Id { get; } = id;
    public IChatSession Session { get; } = session;
    public List<ToolCall>? Pending { get; set; }
    public DateTime LastUsed { get; set; }
    public SemaphoreSlim Lock { get; } = new(1, 1);
}

/// <summary>Lưu hội thoại trong bộ nhớ của API (đủ cho demo; mất khi khởi động lại API).</summary>
public class ChatConversationStore
{
    private readonly ConcurrentDictionary<string, ChatConversation> _items = new();

    internal ChatConversation GetOrCreate(string? id, Func<IChatSession> newSession, DateTime now, TimeSpan ttl)
    {
        foreach (var old in _items.Values.Where(c => now - c.LastUsed > ttl).ToList())
            _items.TryRemove(old.Id, out _);
        if (id is not null && _items.TryGetValue(id, out var existing)) return existing;
        var c = new ChatConversation(Guid.NewGuid().ToString("N"), newSession()) { LastUsed = now };
        _items[c.Id] = c;
        return c;
    }

    internal ChatConversation? Find(string id) => _items.GetValueOrDefault(id);

    internal void Remove(string id) => _items.TryRemove(id, out _);
}

/// <summary>
/// Chatbot trợ lý kinh doanh (UC07). Model trả lời bằng tiếng Việt, tra cứu qua các công cụ đọc;
/// công cụ ghi (giữ sảnh, lưu báo giá) dừng lại chờ nhân viên xác nhận rồi mới chạy.
/// Không phụ thuộc nhà cung cấp LLM: xem IChatModel.
/// </summary>
public class ChatService(IChatModel model, ChatTools tools, ChatConversationStore store, ChatOptions options, TimeProvider clock)
{
    public const int MaxMessageLength = 2000;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ChatReplyDto> SendAsync(ChatRequest req)
    {
        if (!model.IsConfigured)
            throw new AppException(ErrorKind.Unavailable,
                $"Chatbot chưa được cấu hình API key cho {model.DisplayName}. Xem hướng dẫn trong README (mục Chatbot).");
        var text = req.Message?.Trim() ?? "";
        if (text.Length == 0) throw new AppException(ErrorKind.Validation, "Vui lòng nhập câu hỏi.");
        if (text.Length > MaxMessageLength) throw new AppException(ErrorKind.Validation, $"Câu hỏi tối đa {MaxMessageLength} ký tự.");

        var conv = store.GetOrCreate(req.ConversationId, model.NewSession, Now, TimeSpan.FromMinutes(options.ConversationTtlMinutes));
        await conv.Lock.WaitAsync();
        try
        {
            if (conv.Pending is not null)
                throw new AppException(ErrorKind.Conflict, "Hãy xác nhận hoặc từ chối thao tác chatbot đang đề xuất trước khi hỏi tiếp.");
            conv.LastUsed = Now;
            conv.Session.AddUser(text);
            return await RunAsync(conv, []);
        }
        finally
        {
            conv.Lock.Release();
        }
    }

    /// <summary>Nhân viên đồng ý hoặc từ chối các thao tác ghi đang chờ; chatbot trả lời tiếp.</summary>
    public async Task<ChatReplyDto> ConfirmAsync(string conversationId, bool approve)
    {
        var conv = store.Find(conversationId)
            ?? throw new AppException(ErrorKind.NotFound, "Hội thoại đã hết hạn. Hãy bắt đầu hội thoại mới.");
        await conv.Lock.WaitAsync();
        try
        {
            var pending = conv.Pending ?? throw new AppException(ErrorKind.Validation, "Không có thao tác nào đang chờ xác nhận.");
            var used = new List<string>();
            foreach (var call in pending.Where(c => c.Result is null))
            {
                if (approve)
                {
                    (call.Result, call.IsError) = await tools.ExecuteAsync(call.Request.Name, call.Request.Input!);
                    used.Add(call.Request.Name);
                }
                else
                {
                    call.Result = JsonSerializer.Serialize(new { declined = "Nhân viên đã từ chối, thao tác không được thực hiện." });
                }
            }
            conv.Pending = null;
            conv.LastUsed = Now;
            conv.Session.AddToolResults(Outputs(pending));
            return await RunAsync(conv, used);
        }
        finally
        {
            conv.Lock.Release();
        }
    }

    private async Task<ChatReplyDto> RunAsync(ChatConversation conv, List<string> toolsUsed)
    {
        var texts = new List<string>();
        for (var step = 0; step < options.MaxSteps; step++)
        {
            ModelTurn turn;
            try
            {
                turn = await conv.Session.NextAsync(SystemPrompt(), ChatTools.Specs);
            }
            catch (ChatModelException ex)
            {
                // Bỏ hội thoại để lượt sau bắt đầu lại sạch (lịch sử có thể đang dở dang).
                store.Remove(conv.Id);
                throw new AppException(ErrorKind.Unavailable, ex.Message);
            }

            if (!string.IsNullOrWhiteSpace(turn.Text)) texts.Add(turn.Text.Trim());
            if (turn.Refused)
                return Reply(conv, ["Xin lỗi, mình không hỗ trợ được yêu cầu này. Bạn hỏi về sảnh, thực đơn hoặc báo giá nhé."], toolsUsed, []);
            if (turn.ToolCalls.Count == 0)
                return Reply(conv, texts, toolsUsed, []);

            var calls = turn.ToolCalls.Select(r => new ToolCall(r)).ToList();
            foreach (var call in calls)
            {
                if (call.Request.Input is null)
                {
                    call.Result = JsonSerializer.Serialize(new { error = "Tham số công cụ không phải JSON hợp lệ." });
                    call.IsError = true;
                }
                else if (!ChatTools.IsWrite(call.Request.Name))
                {
                    (call.Result, call.IsError) = await tools.ExecuteAsync(call.Request.Name, call.Request.Input);
                    toolsUsed.Add(call.Request.Name);
                }
            }

            var writes = calls.Where(c => c.Result is null).ToList();
            if (writes.Count > 0)
            {
                conv.Pending = calls;
                var actions = new List<ChatPendingActionDto>();
                foreach (var w in writes) actions.Add(await tools.DescribeAsync(w.Request.Name, w.Request.Input!));
                if (texts.Count == 0) texts.Add("Mình đề xuất thao tác bên dưới, bạn kiểm tra rồi bấm xác nhận giúp nhé.");
                return Reply(conv, texts, toolsUsed, actions);
            }

            conv.Session.AddToolResults(Outputs(calls));
        }

        store.Remove(conv.Id);
        return new ChatReplyDto("", "Câu hỏi cần quá nhiều bước tra cứu. Bạn hỏi ngắn gọn, cụ thể hơn giúp mình nhé.", [], toolsUsed);
    }

    private static List<ChatToolOutput> Outputs(List<ToolCall> calls) =>
        calls.Select(c => new ChatToolOutput(c.Request.Id, c.Request.Name, c.Result ?? "{}", c.IsError)).ToList();

    private static ChatReplyDto Reply(ChatConversation conv, List<string> texts, List<string> toolsUsed, List<ChatPendingActionDto> actions) =>
        new(conv.Id, string.Join("\n\n", texts).Trim(), actions, toolsUsed);

    private string SystemPrompt()
    {
        var vi = CultureInfo.GetCultureInfo("vi-VN");
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return $"""
            Bạn là trợ lý kinh doanh của một nhà hàng tiệc cưới, trò chuyện với nhân viên kinh doanh của nhà hàng bằng tiếng Việt.
            Hôm nay là {vi.DateTimeFormat.GetDayName(today.DayOfWeek)}, ngày {today:dd/MM/yyyy} ({today:yyyy-MM-dd}).

            Nghiệp vụ:
            - Mỗi ngày có 2 ca: trưa (Lunch) và tối (Dinner). Mỗi sảnh mỗi ca chỉ một tiệc. Mỗi bàn 10 khách.
            - Chỉ nhận đặt từ ngày mai trở đi. Giữ sảnh tạm có hiệu lực 48 giờ, khách đặt cọc thì sảnh mới được chốt.
            - Giá báo = tiền thuê sảnh + giá món mỗi bàn × số bàn + dịch vụ × số lượng, cộng VAT.

            Cách làm việc:
            - Mọi thông tin về sảnh trống, giá, thực đơn phải lấy từ công cụ. Không tự đoán hay tự tính số tiền; dùng estimate_quote để có con số.
            - Hỏi về tiệc đã có (ngày nào có tiệc, khách nào đặt, đã cọc chưa, lượt giữ nào sắp hết hạn) thì dùng find_bookings.
            - Phân biệt đúng trạng thái: "đang giữ chỗ" là chưa cọc và có hạn giữ; chỉ nói "đã đặt" hoặc "đã cọc" khi trạng thái là đã đặt cọc.
            - Khi nhân viên nói ngày tương đối ("thứ Bảy tuần sau"), tự đổi ra ngày cụ thể và nói rõ ngày đó trong câu trả lời.
            - Nếu thiếu thông tin cần thiết (ngày, số bàn, tên hoặc số điện thoại khách), hỏi lại ngắn gọn.
            - Chỉ gọi hold_hall hoặc create_quote khi nhân viên muốn thực hiện. Nhân viên sẽ bấm xác nhận trên app; nói rõ bạn đề xuất làm gì.
            - Trả lời ngắn gọn, đi thẳng vào kết quả. Viết tiền dạng 30.000.000 đ.
            - Luôn viết đúng chính tả tiếng Việt có viết hoa đầu câu, kể cả khi nhân viên gõ chữ thường; giữ nguyên cách viết tên sảnh, tên khách như dữ liệu công cụ trả về.
            - Khung chat chỉ hiện chữ thường: không dùng markdown (không **in đậm**, không #, không bảng). Liệt kê thì mỗi mục một dòng bắt đầu bằng "- ".
            - Ngoài phạm vi nhà hàng tiệc cưới thì lịch sự từ chối.
            """;
    }
}
