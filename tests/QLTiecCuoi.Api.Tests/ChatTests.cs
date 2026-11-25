using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Api.Services.Chat;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Tests;

/// <summary>Claude giả lập: trả lần lượt các phản hồi soạn sẵn, ghi lại các yêu cầu đã gửi.</summary>
public class FakeChatModel(params string[] responses) : IChatModel
{
    private readonly Queue<string> _responses = new(responses);
    public List<MessageCreateParams> Requests { get; } = [];
    public bool IsConfigured { get; set; } = true;

    public Task<BetaMessage> SendAsync(MessageCreateParams request)
    {
        Requests.Add(request);
        return Task.FromResult(JsonSerializer.Deserialize<BetaMessage>(_responses.Dequeue())!);
    }

    public static string Text(string text, string stop = "end_turn") => Message(
        [new { type = "text", text }], stop);

    public static string ToolUse(string id, string name, object input, string? preface = null)
    {
        var blocks = new List<object>();
        if (preface is not null) blocks.Add(new { type = "text", text = preface });
        blocks.Add(new { type = "tool_use", id, name, input });
        return Message(blocks, "tool_use");
    }

    private static string Message(IEnumerable<object> content, string stop) => JsonSerializer.Serialize(new
    {
        id = "msg_" + Guid.NewGuid().ToString("N")[..8],
        type = "message",
        role = "assistant",
        model = "claude-opus-5-5",
        content,
        stop_reason = stop,
        stop_sequence = (string?)null,
        usage = new { input_tokens = 10, output_tokens = 5 },
    });
}

public class ChatTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Day = new(2027, 1, 9);

    public ChatTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);

    private BookingService Bookings(AppDbContext db) => new(db, _clock, new BookingOptions());

    private ChatTools Tools(AppDbContext db) =>
        new(Bookings(db), new CatalogService(db), new QuoteService(db, _clock, new QuoteOptions { VatPercent = 10 }), _clock);

    private ChatService Chat(AppDbContext db, IChatModel model, ChatConversationStore store) =>
        new(model, Tools(db), store, new ChatOptions(), _clock);

    private static IReadOnlyDictionary<string, JsonElement> Input(object o) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(o))!;

    // ----- Công cụ -----

    [Fact]
    public async Task Tool_FindAvailable_ExcludesHeldSlots_AndSmallHalls()
    {
        using var db = NewDb();
        await Bookings(db).HoldAsync(new HoldRequest(1, Day, Shift.Dinner, "A", "1", 40, null));

        var (json, isError) = await Tools(db).ExecuteAsync("find_available_halls",
            Input(new { date_from = "2027-01-09", date_to = "2027-01-09", shift = "Dinner", min_tables = 40 }));

        Assert.False(isError);
        using var doc = JsonDocument.Parse(json);
        var halls = doc.RootElement.GetProperty("available").EnumerateArray().Select(x => x.GetProperty("hall_id").GetInt32()).ToList();
        Assert.Equal([2], halls); // Kim Cương đã có khách giữ; Ngọc Trai, Hồng Ngọc không đủ 40 bàn
    }

    [Fact]
    public async Task Tool_EstimateQuote_MatchesQuoteService()
    {
        using var db = NewDb();
        var (json, isError) = await Tools(db).ExecuteAsync("estimate_quote", Input(new
        {
            hall_id = 1, date = "2027-01-09", shift = "Dinner", tables = 40,
            menu_item_ids = new[] { 1, 4 }, services = new[] { new { service_item_id = 2, quantity = 1 }, new { service_item_id = 5, quantity = 10 } },
        }));
        Assert.False(isError);
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal(84_920_000, root.GetProperty("total").GetDecimal());
        Assert.Equal(212_000, root.GetProperty("per_guest_approx").GetDecimal()); // 84.920.000 / 400 khách = 212.300 → 212.000
    }

    [Theory]
    [InlineData("find_available_halls", "{\"date_from\":\"2027-01-01\",\"date_to\":\"2027-03-01\",\"shift\":\"Any\",\"min_tables\":0}")] // quá 31 ngày
    [InlineData("estimate_quote", "{\"hall_id\":1,\"date\":\"2027-01-09\",\"shift\":\"Night\",\"tables\":40,\"menu_item_ids\":[1],\"services\":[]}")]
    [InlineData("estimate_quote", "{\"hall_id\":1,\"date\":\"09/01/2027\",\"shift\":\"Dinner\",\"tables\":40,\"menu_item_ids\":[1],\"services\":[]}")]
    [InlineData("no_such_tool", "{}")]
    public async Task Tool_BadInput_ReturnsErrorResult_NotException(string tool, string input)
    {
        using var db = NewDb();
        var (json, isError) = await Tools(db).ExecuteAsync(tool, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input)!);
        Assert.True(isError);
        Assert.True(JsonDocument.Parse(json).RootElement.TryGetProperty("error", out _));
    }

    // ----- Vòng lặp hội thoại -----

    [Fact]
    public async Task Chat_ReadTool_RunsAndSendsResultBack()
    {
        using var db = NewDb();
        var model = new FakeChatModel(
            FakeChatModel.ToolUse("tu_1", "find_available_halls", new { date_from = "2027-01-09", date_to = "2027-01-09", shift = "Dinner", min_tables = 40 }),
            FakeChatModel.Text("Tối 09/01/2027 còn Sảnh Kim Cương và Sảnh Hoàng Gia."));

        var reply = await Chat(db, model, new()).SendAsync(new ChatRequest(null, "Tối thứ Bảy 9/1 còn sảnh nào cho 40 bàn?"));

        Assert.Equal("Tối 09/01/2027 còn Sảnh Kim Cương và Sảnh Hoàng Gia.", reply.Text);
        Assert.Equal(["find_available_halls"], reply.ToolsUsed);
        Assert.Empty(reply.PendingActions);
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(3, model.Requests[1].Messages.Count); // câu hỏi, tool_use, tool_result
        Assert.Equal(ChatTools.Specs.Count, model.Requests[0].Tools!.Count);
    }

    [Fact]
    public async Task Chat_WriteTool_WaitsForConfirmation_ThenExecutes()
    {
        using var db = NewDb();
        var store = new ChatConversationStore();
        var model = new FakeChatModel(
            FakeChatModel.ToolUse("tu_1", "hold_hall",
                new { hall_id = 2, date = "2027-01-09", shift = "Dinner", customer_name = "Lê Hoa", customer_phone = "0901234567", expected_tables = 40 },
                preface: "Mình sẽ giữ Sảnh Hoàng Gia cho chị Hoa."),
            FakeChatModel.Text("Đã giữ Sảnh Hoàng Gia đến hết 48 giờ."));

        var reply = await Chat(db, model, store).SendAsync(new ChatRequest(null, "Giữ Hoàng Gia tối 9/1 cho chị Lê Hoa 0901234567, 40 bàn"));

        var action = Assert.Single(reply.PendingActions);
        Assert.Contains("Sảnh Hoàng Gia", action.Details);
        Assert.Contains("Lê Hoa", action.Details);
        Assert.Equal(0, await db.Bookings.CountAsync()); // chưa xác nhận thì chưa ghi

        var after = await Chat(db, model, store).ConfirmAsync(reply.ConversationId, approve: true);

        Assert.Equal("Đã giữ Sảnh Hoàng Gia đến hết 48 giờ.", after.Text);
        var booking = Assert.Single(db.Bookings);
        Assert.Equal(2, booking.HallId);
        Assert.Equal(BookingStatus.Held, booking.Status);
    }

    [Fact]
    public async Task Chat_WriteTool_Declined_DoesNotWrite()
    {
        using var db = NewDb();
        var store = new ChatConversationStore();
        var model = new FakeChatModel(
            FakeChatModel.ToolUse("tu_1", "hold_hall",
                new { hall_id = 2, date = "2027-01-09", shift = "Dinner", customer_name = "Lê Hoa", customer_phone = "0901234567", expected_tables = 40 }),
            FakeChatModel.Text("Đã hủy, mình không giữ sảnh."));

        var reply = await Chat(db, model, store).SendAsync(new ChatRequest(null, "Giữ sảnh giúp mình"));
        var after = await Chat(db, model, store).ConfirmAsync(reply.ConversationId, approve: false);

        Assert.Equal("Đã hủy, mình không giữ sảnh.", after.Text);
        Assert.Equal(0, await db.Bookings.CountAsync());
    }

    [Fact]
    public async Task Chat_NewMessageWhilePending_IsRejected()
    {
        using var db = NewDb();
        var store = new ChatConversationStore();
        var model = new FakeChatModel(FakeChatModel.ToolUse("tu_1", "hold_hall",
            new { hall_id = 2, date = "2027-01-09", shift = "Dinner", customer_name = "A", customer_phone = "1", expected_tables = 40 }));
        var reply = await Chat(db, model, store).SendAsync(new ChatRequest(null, "Giữ sảnh"));

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Chat(db, model, store).SendAsync(new ChatRequest(reply.ConversationId, "Câu khác")));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public async Task Chat_Refusal_ReturnsPoliteMessage()
    {
        using var db = NewDb();
        var model = new FakeChatModel(FakeChatModel.Text("", stop: "refusal"));
        var reply = await Chat(db, model, new()).SendAsync(new ChatRequest(null, "..."));
        Assert.Contains("không hỗ trợ", reply.Text);
    }

    [Fact]
    public async Task Chat_NotConfigured_ReturnsUnavailable()
    {
        using var db = NewDb();
        var model = new FakeChatModel { IsConfigured = false };
        var ex = await Assert.ThrowsAsync<AppException>(() => Chat(db, model, new()).SendAsync(new ChatRequest(null, "Chào")));
        Assert.Equal(ErrorKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task Chat_SecondQuestion_ContinuesSameConversation()
    {
        using var db = NewDb();
        var store = new ChatConversationStore();
        var model = new FakeChatModel(FakeChatModel.Text("Chào bạn!"), FakeChatModel.Text("Có 4 sảnh."));
        var first = await Chat(db, model, store).SendAsync(new ChatRequest(null, "Chào"));
        var second = await Chat(db, model, store).SendAsync(new ChatRequest(first.ConversationId, "Có mấy sảnh?"));
        Assert.Equal(first.ConversationId, second.ConversationId);
        Assert.Equal(3, model.Requests[1].Messages.Count); // hỏi, đáp, hỏi tiếp
    }
}
