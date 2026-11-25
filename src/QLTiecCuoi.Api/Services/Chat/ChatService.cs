using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Services.Chat;

public class ChatOptions
{
    /// <summary>Model Claude dùng cho chatbot.</summary>
    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>Mức effort: low, medium, high. Chatbot tra cứu dùng medium.</summary>
    public string Effort { get; set; } = "medium";

    /// <summary>Số lần gọi model tối đa cho một câu hỏi (mỗi lần có thể gọi nhiều tool).</summary>
    public int MaxSteps { get; set; } = 8;

    /// <summary>Hội thoại không dùng quá số phút này thì bị xóa khỏi bộ nhớ.</summary>
    public int ConversationTtlMinutes { get; set; } = 120;
}

/// <summary>Gửi yêu cầu tới Claude. Tách ra interface để kiểm thử không cần gọi API thật.</summary>
public interface IChatModel
{
    bool IsConfigured { get; }
    Task<BetaMessage> SendAsync(MessageCreateParams request);
}

public class ClaudeChatModel(AnthropicClient client, bool configured) : IChatModel
{
    public bool IsConfigured => configured;
    public Task<BetaMessage> SendAsync(MessageCreateParams request) => client.Beta.Messages.Create(request);
}

/// <summary>Một lời gọi tool trong lượt đang chờ: đã có kết quả (tool đọc) hoặc chờ xác nhận (tool ghi).</summary>
internal record ToolCall(string ToolUseId, string Name, IReadOnlyDictionary<string, JsonElement> Input)
{
    public string? Result { get; set; }
    public bool IsError { get; set; }
}

internal class ChatConversation(string id)
{
    public string Id { get; } = id;
    public List<BetaMessageParam> Messages { get; } = [];
    public List<ToolCall>? Pending { get; set; }
    public DateTime LastUsed { get; set; }
    public SemaphoreSlim Lock { get; } = new(1, 1);
}

/// <summary>Lưu hội thoại trong bộ nhớ của API (đủ cho demo; mất khi khởi động lại API).</summary>
public class ChatConversationStore
{
    private readonly ConcurrentDictionary<string, ChatConversation> _items = new();

    internal ChatConversation GetOrCreate(string? id, DateTime now, TimeSpan ttl)
    {
        foreach (var old in _items.Values.Where(c => now - c.LastUsed > ttl).ToList())
            _items.TryRemove(old.Id, out _);
        if (id is not null && _items.TryGetValue(id, out var existing)) return existing;
        var c = new ChatConversation(Guid.NewGuid().ToString("N")) { LastUsed = now };
        _items[c.Id] = c;
        return c;
    }

    internal ChatConversation? Find(string id) => _items.GetValueOrDefault(id);

    internal void Remove(string id) => _items.TryRemove(id, out _);
}

/// <summary>
/// Chatbot trợ lý kinh doanh (UC07): Claude trả lời bằng tiếng Việt, tra cứu qua các tool đọc;
/// tool ghi (giữ sảnh, lưu báo giá) dừng lại chờ nhân viên xác nhận rồi mới chạy.
/// Hội thoại chỉ được nối thêm (append-only), không sửa lượt cũ.
/// </summary>
public class ChatService(IChatModel model, ChatTools tools, ChatConversationStore store, ChatOptions options, TimeProvider clock)
{
    public const int MaxMessageLength = 2000;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ChatReplyDto> SendAsync(ChatRequest req)
    {
        if (!model.IsConfigured)
            throw new AppException(ErrorKind.Unavailable,
                "Chatbot chưa được cấu hình API key của Claude. Xem hướng dẫn trong README (mục Chatbot).");
        var text = req.Message?.Trim() ?? "";
        if (text.Length == 0) throw new AppException(ErrorKind.Validation, "Vui lòng nhập câu hỏi.");
        if (text.Length > MaxMessageLength) throw new AppException(ErrorKind.Validation, $"Câu hỏi tối đa {MaxMessageLength} ký tự.");

        var conv = store.GetOrCreate(req.ConversationId, Now, TimeSpan.FromMinutes(options.ConversationTtlMinutes));
        await conv.Lock.WaitAsync();
        try
        {
            if (conv.Pending is not null)
                throw new AppException(ErrorKind.Conflict, "Hãy xác nhận hoặc từ chối thao tác chatbot đang đề xuất trước khi hỏi tiếp.");
            conv.LastUsed = Now;
            conv.Messages.Add(new BetaUserMessageParam { Content = text });
            return await RunAsync(conv, [], []);
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
                    (call.Result, call.IsError) = await tools.ExecuteAsync(call.Name, call.Input);
                    used.Add(call.Name);
                }
                else
                {
                    call.Result = JsonSerializer.Serialize(new { declined = "Nhân viên đã từ chối, thao tác không được thực hiện." });
                }
            }
            conv.Pending = null;
            conv.LastUsed = Now;
            conv.Messages.Add(ToolResults(pending));
            return await RunAsync(conv, used, []);
        }
        finally
        {
            conv.Lock.Release();
        }
    }

    private async Task<ChatReplyDto> RunAsync(ChatConversation conv, List<string> toolsUsed, List<string> texts)
    {
        for (var step = 0; step < options.MaxSteps; step++)
        {
            BetaMessage resp;
            try
            {
                resp = await model.SendAsync(BuildRequest(conv.Messages));
            }
            catch (Exception ex) when (ex.GetType().Namespace?.StartsWith("Anthropic", StringComparison.Ordinal) == true
                                       || ex is HttpRequestException or TaskCanceledException)
            {
                // Bỏ câu hỏi chưa trả lời được để hội thoại vẫn hợp lệ ở lượt sau.
                store.Remove(conv.Id);
                throw new AppException(ErrorKind.Unavailable, $"Không gọi được Claude API ({ex.GetType().Name}). Hãy thử lại sau.");
            }

            conv.Messages.Add(new BetaAssistantMessageParam { Content = ToParams(resp.Content) });
            texts.AddRange(resp.Content.Select(b => b.TryPickText(out var t) ? t.Text : null).OfType<string>());

            if (resp.StopReason == "refusal")
                return Reply(conv, ["Xin lỗi, mình không hỗ trợ được yêu cầu này. Bạn hỏi về sảnh, thực đơn hoặc báo giá nhé."], toolsUsed, []);

            var uses = resp.Content.Select(b => b.TryPickToolUse(out var u) ? u : null).OfType<BetaToolUseBlock>().ToList();
            if (uses.Count == 0 || resp.StopReason != "tool_use")
                return Reply(conv, texts, toolsUsed, []);

            var calls = uses.Select(u => new ToolCall(u.ID, u.Name, u.Input)).ToList();
            foreach (var call in calls.Where(c => !ChatTools.IsWrite(c.Name)))
            {
                (call.Result, call.IsError) = await tools.ExecuteAsync(call.Name, call.Input);
                toolsUsed.Add(call.Name);
            }

            var writes = calls.Where(c => c.Result is null).ToList();
            if (writes.Count > 0)
            {
                conv.Pending = calls;
                var actions = new List<ChatPendingActionDto>();
                foreach (var w in writes) actions.Add(await tools.DescribeAsync(w.Name, w.Input));
                return Reply(conv, texts, toolsUsed, actions);
            }

            conv.Messages.Add(ToolResults(calls));
        }

        // Quá số bước: bỏ hội thoại để lượt sau bắt đầu sạch.
        store.Remove(conv.Id);
        return new ChatReplyDto("", "Câu hỏi cần quá nhiều bước tra cứu. Bạn hỏi ngắn gọn, cụ thể hơn giúp mình nhé.", [], toolsUsed);
    }

    private static ChatReplyDto Reply(ChatConversation conv, List<string> texts, List<string> toolsUsed, List<ChatPendingActionDto> actions) =>
        new(conv.Id, string.Join("\n\n", texts.Where(t => !string.IsNullOrWhiteSpace(t))).Trim(), actions, toolsUsed);

    private MessageCreateParams BuildRequest(List<BetaMessageParam> messages) => new()
    {
        Model = options.Model,
        MaxTokens = 16000,
        System = SystemPrompt(),
        Messages = [.. messages],
        Tools = [.. ToolDefinitions],
        OutputConfig = new BetaOutputConfig { Effort = ParseEffort(options.Effort) },
        // Bị bộ lọc an toàn từ chối nhầm thì server tự chuyển sang model dự phòng phù hợp.
        Betas = ["server-side-fallback-2026-07-01"],
        Fallbacks = new Default(),
    };

    private static Effort ParseEffort(string value) => value.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        _ => Effort.Medium,
    };

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
            - Khi nhân viên nói ngày tương đối ("thứ Bảy tuần sau"), tự đổi ra ngày cụ thể và nói rõ ngày đó trong câu trả lời.
            - Nếu thiếu thông tin cần thiết (ngày, số bàn, tên hoặc số điện thoại khách), hỏi lại ngắn gọn.
            - Chỉ gọi hold_hall hoặc create_quote khi nhân viên muốn thực hiện. Nhân viên sẽ bấm xác nhận trên app; nói rõ bạn đề xuất làm gì.
            - Trả lời ngắn gọn, đi thẳng vào kết quả. Viết tiền dạng 30.000.000 đ. Không dùng bảng markdown; liệt kê bằng gạch đầu dòng khi có nhiều mục.
            - Ngoài phạm vi nhà hàng tiệc cưới thì lịch sự từ chối.
            """;
    }

    private static readonly List<BetaToolUnion> ToolDefinitions = ChatTools.Specs
        .Select(s => (BetaToolUnion)new BetaTool
        {
            Name = s.Name,
            Description = s.Description,
            InputSchema = new()
            {
                Properties = s.Properties.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)),
                Required = [.. s.Required],
            },
        })
        .ToList();

    private static BetaUserMessageParam ToolResults(List<ToolCall> calls) => new()
    {
        Content = calls.Select(c => (BetaContentBlockParam)new BetaToolResultBlockParam
        {
            ToolUseID = c.ToolUseId,
            Content = c.Result ?? "{}",
            IsError = c.IsError,
        }).ToList(),
    };

    /// <summary>Chuyển nội dung trả về thành tham số để gửi lại nguyên vẹn ở lượt sau (giữ chữ ký của thinking).</summary>
    private static List<BetaContentBlockParam> ToParams(IReadOnlyList<BetaContentBlock> blocks)
    {
        var list = new List<BetaContentBlockParam>();
        foreach (var b in blocks)
        {
            if (b.TryPickText(out var t)) list.Add(new BetaTextBlockParam { Text = t.Text });
            else if (b.TryPickThinking(out var th)) list.Add(new BetaThinkingBlockParam { Thinking = th.Thinking, Signature = th.Signature });
            else if (b.TryPickRedactedThinking(out var r)) list.Add(new BetaRedactedThinkingBlockParam { Data = r.Data });
            else if (b.TryPickToolUse(out var u)) list.Add(new BetaToolUseBlockParam { ID = u.ID, Name = u.Name, Input = u.Input });
            else list.Add(new BetaContentBlockParam(b.Json)); // khối khác (ví dụ thông tin fallback): gửi lại nguyên JSON
        }
        return list;
    }
}
