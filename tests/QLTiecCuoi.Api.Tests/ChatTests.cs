using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Beta.Messages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QLTiecCuoi.Api.Data;
using QLTiecCuoi.Api.Services;
using QLTiecCuoi.Api.Services.Chat;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Tests;

/// <summary>Model giả lập không phụ thuộc nhà cung cấp: trả lần lượt các lượt soạn sẵn.</summary>
public class FakeChatModel(params ModelTurn[] turns) : IChatModel
{
    private readonly Queue<ModelTurn> _turns = new(turns);
    public bool IsConfigured { get; set; } = true;
    public string DisplayName => "Fake";
    public List<string> UserMessages { get; } = [];
    public List<ChatToolOutput> ToolResults { get; } = [];
    public int Calls { get; private set; }

    public IChatSession NewSession() => new Session(this);

    public static ModelTurn Text(string text) => new(text, []);

    public static ModelTurn Tool(string id, string name, object input, string text = "") =>
        new(text, [new ChatToolRequest(id, name, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(input)))]);

    private sealed class Session(FakeChatModel owner) : IChatSession
    {
        public void AddUser(string text) => owner.UserMessages.Add(text);
        public void AddToolResults(IReadOnlyList<ChatToolOutput> results) => owner.ToolResults.AddRange(results);

        public Task<ModelTurn> NextAsync(string systemPrompt, IReadOnlyList<ChatToolSpec> tools)
        {
            owner.Calls++;
            return Task.FromResult(owner._turns.Dequeue());
        }
    }
}

/// <summary>Máy chủ HTTP giả: trả lần lượt các phản hồi soạn sẵn, ghi lại yêu cầu.</summary>
public class FakeHttpHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode, string)> _responses = new(responses);
    public List<(HttpRequestMessage Request, JsonNode Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add((request, JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!));
        var (status, body) = _responses.Dequeue();
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
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

    private static readonly object HoldInput = new
    {
        hall_id = 2, date = "2027-01-09", shift = "Dinner", customer_name = "Lê Hoa", customer_phone = "0901234567", expected_tables = 40,
    };

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

    // ----- Vòng lặp hội thoại (không phụ thuộc nhà cung cấp) -----

    [Fact]
    public async Task Chat_ReadTool_RunsAndSendsResultBack()
    {
        using var db = NewDb();
        var model = new FakeChatModel(
            FakeChatModel.Tool("t1", "find_available_halls", new { date_from = "2027-01-09", date_to = "2027-01-09", shift = "Dinner", min_tables = 40 }),
            FakeChatModel.Text("Tối 09/01/2027 còn Sảnh Kim Cương và Sảnh Hoàng Gia."));

        var reply = await Chat(db, model, new()).SendAsync(new ChatRequest(null, "Tối thứ Bảy 9/1 còn sảnh nào cho 40 bàn?"));

        Assert.Equal("Tối 09/01/2027 còn Sảnh Kim Cương và Sảnh Hoàng Gia.", reply.Text);
        Assert.Equal(["find_available_halls"], reply.ToolsUsed);
        Assert.Empty(reply.PendingActions);
        var result = Assert.Single(model.ToolResults);
        Assert.Equal("t1", result.Id);
        Assert.Contains("Sảnh Hoàng Gia", result.Result);
    }

    [Fact]
    public async Task Chat_WriteTool_WaitsForConfirmation_ThenExecutes()
    {
        using var db = NewDb();
        var store = new ChatConversationStore();
        var model = new FakeChatModel(
            FakeChatModel.Tool("t1", "hold_hall", HoldInput, "Mình sẽ giữ Sảnh Hoàng Gia cho chị Hoa."),
            FakeChatModel.Text("Đã giữ Sảnh Hoàng Gia đến hết 48 giờ."));

        var reply = await Chat(db, model, store).SendAsync(new ChatRequest(null, "Giữ Hoàng Gia tối 9/1 cho chị Lê Hoa 0901234567, 40 bàn"));

        var action = Assert.Single(reply.PendingActions);
        Assert.Contains("Sảnh Hoàng Gia", action.Details);
        Assert.Contains("Lê Hoa", action.Details);
        Assert.Equal("Mình sẽ giữ Sảnh Hoàng Gia cho chị Hoa.", reply.Text);
        Assert.Equal(0, await db.Bookings.CountAsync()); // chưa xác nhận thì chưa ghi

        var after = await Chat(db, model, store).ConfirmAsync(reply.ConversationId, approve: true);

        Assert.Equal("Đã giữ Sảnh Hoàng Gia đến hết 48 giờ.", after.Text);
        var booking = Assert.Single(db.Bookings);
        Assert.Equal(2, booking.HallId);
        var result = Assert.Single(model.ToolResults).Result;
        Assert.Contains("booking_id", result);
        Assert.Contains("\"hold_expires_at_local\":\"09:00 03/12/2026\"", result); // đồng hồ giả lập ở múi UTC: 01/12 09:00 + 48 giờ
    }

    [Fact]
    public async Task Chat_WriteToolWithoutText_GetsDefaultPrompt()
    {
        using var db = NewDb();
        var model = new FakeChatModel(FakeChatModel.Tool("t1", "hold_hall", HoldInput));
        var reply = await Chat(db, model, new()).SendAsync(new ChatRequest(null, "Giữ sảnh"));
        Assert.Contains("xác nhận", reply.Text);
    }

    [Fact]
    public async Task Chat_WriteTool_Declined_DoesNotWrite()
    {
        using var db = NewDb();
        var store = new ChatConversationStore();
        var model = new FakeChatModel(FakeChatModel.Tool("t1", "hold_hall", HoldInput), FakeChatModel.Text("Đã hủy, mình không giữ sảnh."));

        var reply = await Chat(db, model, store).SendAsync(new ChatRequest(null, "Giữ sảnh giúp mình"));
        var after = await Chat(db, model, store).ConfirmAsync(reply.ConversationId, approve: false);

        Assert.Equal("Đã hủy, mình không giữ sảnh.", after.Text);
        Assert.Equal(0, await db.Bookings.CountAsync());
        Assert.Contains("declined", Assert.Single(model.ToolResults).Result);
    }

    [Fact]
    public async Task Chat_NewMessageWhilePending_IsRejected()
    {
        using var db = NewDb();
        var store = new ChatConversationStore();
        var model = new FakeChatModel(FakeChatModel.Tool("t1", "hold_hall", HoldInput));
        var reply = await Chat(db, model, store).SendAsync(new ChatRequest(null, "Giữ sảnh"));

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Chat(db, model, store).SendAsync(new ChatRequest(reply.ConversationId, "Câu khác")));
        Assert.Equal(ErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public async Task Chat_InvalidToolArguments_ReturnErrorToModel()
    {
        using var db = NewDb();
        var model = new FakeChatModel(new ModelTurn("", [new ChatToolRequest("t1", "hold_hall", null)]), FakeChatModel.Text("Xin lỗi, mình thử lại."));
        var reply = await Chat(db, model, new()).SendAsync(new ChatRequest(null, "Giữ sảnh"));
        Assert.Empty(reply.PendingActions);
        Assert.True(Assert.Single(model.ToolResults).IsError);
    }

    [Fact]
    public async Task Chat_Refusal_ReturnsPoliteMessage()
    {
        using var db = NewDb();
        var model = new FakeChatModel(new ModelTurn("", [], Refused: true));
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
        Assert.Equal(["Chào", "Có mấy sảnh?"], model.UserMessages);
    }

    // ----- Kết nối Gemini / chuẩn OpenAI -----

    private static string Completion(object message, string finish = "stop") =>
        JsonSerializer.Serialize(new { choices = new[] { new { index = 0, message, finish_reason = finish } } });

    [Fact]
    public async Task Gemini_ToolCallRoundTrip_KeepsExtraFields_AndFillsMissingIds()
    {
        var handler = new FakeHttpHandler(
            (HttpStatusCode.OK, Completion(new
            {
                role = "assistant",
                content = (string?)null,
                tool_calls = new[] { new { id = "", type = "function", function = new { name = "list_halls", arguments = "{}" } } },
                extra_content = new { google = new { thought_signature = "sig-123" } },
            }, "tool_calls")),
            (HttpStatusCode.OK, Completion(new { role = "assistant", content = "Nhà hàng có 4 sảnh." })));
        var model = new OpenAiCompatibleChatModel(new HttpClient(handler), new ChatOptions(), "test-key", "Gemini");
        var session = model.NewSession();
        session.AddUser("Có mấy sảnh?");

        var first = await session.NextAsync("system", ChatTools.Specs);
        var call = Assert.Single(first.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("list_halls", call.Name);
        Assert.NotNull(call.Input);

        session.AddToolResults([new ChatToolOutput(call.Id, call.Name, "[]", false)]);
        var second = await session.NextAsync("system", ChatTools.Specs);
        Assert.Equal("Nhà hàng có 4 sảnh.", second.Text);

        var (req, body) = handler.Requests[1];
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", req.Headers.Authorization!.ToString());
        Assert.Equal(OpenAiCompatibleChatModel.GeminiDefaultModel, body["model"]!.GetValue<string>());
        var messages = body["messages"]!.AsArray();
        Assert.Equal(["system", "user", "assistant", "tool"], messages.Select(m => m!["role"]!.GetValue<string>()));
        Assert.Equal("sig-123", messages[2]!["extra_content"]!["google"]!["thought_signature"]!.GetValue<string>()); // gửi lại nguyên văn
        Assert.Equal("call_1", messages[2]!["tool_calls"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("call_1", messages[3]!["tool_call_id"]!.GetValue<string>());

        var tools = body["tools"]!.AsArray();
        Assert.Equal(ChatTools.Specs.Count, tools.Count);
        Assert.Null(tools.Single(t => t!["function"]!["name"]!.GetValue<string>() == "list_halls")!["function"]!["parameters"]);
        Assert.NotNull(tools.Single(t => t!["function"]!["name"]!.GetValue<string>() == "hold_hall")!["function"]!["parameters"]);
    }

    [Fact]
    public async Task Gemini_BadArgumentsJson_GivesNullInput()
    {
        var handler = new FakeHttpHandler((HttpStatusCode.OK, Completion(new
        {
            role = "assistant",
            tool_calls = new[] { new { id = "a", type = "function", function = new { name = "hold_hall", arguments = "{không phải json" } } },
        }, "tool_calls")));
        var session = new OpenAiCompatibleChatModel(new HttpClient(handler), new ChatOptions(), "k", "Gemini").NewSession();
        session.AddUser("x");
        Assert.Null(Assert.Single((await session.NextAsync("s", ChatTools.Specs)).ToolCalls).Input);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "hết lượt")]
    [InlineData(HttpStatusCode.Unauthorized, "API key")]
    [InlineData(HttpStatusCode.NotFound, "không tìm thấy model")]
    public async Task Gemini_HttpErrors_BecomeFriendlyMessages(HttpStatusCode status, string expected)
    {
        var handler = new FakeHttpHandler((status, "[{\"error\":{\"message\":\"detail\"}}]"));
        var session = new OpenAiCompatibleChatModel(new HttpClient(handler), new ChatOptions(), "k", "Gemini").NewSession();
        session.AddUser("x");
        var ex = await Assert.ThrowsAsync<ChatModelException>(() => session.NextAsync("s", ChatTools.Specs));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Gemini_ErrorDuringChat_ReturnsUnavailable_AndResetsConversation()
    {
        using var db = NewDb();
        var store = new ChatConversationStore();
        var handler = new FakeHttpHandler((HttpStatusCode.TooManyRequests, "{}"));
        var model = new OpenAiCompatibleChatModel(new HttpClient(handler), new ChatOptions(), "k", "Gemini");
        var ex = await Assert.ThrowsAsync<AppException>(() => Chat(db, model, store).SendAsync(new ChatRequest(null, "Chào")));
        Assert.Equal(ErrorKind.Unavailable, ex.Kind);
        Assert.Contains("hết lượt", ex.Message);
    }

    // ----- Kết nối Claude -----

    private static BetaMessage ClaudeMessage(object[] content, string stop) => JsonSerializer.Deserialize<BetaMessage>(JsonSerializer.Serialize(new
    {
        id = "msg_1", type = "message", role = "assistant", model = "claude-opus-5-5", content,
        stop_reason = stop, stop_sequence = (string?)null, usage = new { input_tokens = 10, output_tokens = 5 },
    }))!;

    [Fact]
    public async Task Claude_ToolCallRoundTrip_AndFallbackOnlyForSupportedModels()
    {
        var requests = new List<MessageCreateParams>();
        var replies = new Queue<BetaMessage>([
            ClaudeMessage([new { type = "tool_use", id = "tu_1", name = "list_halls", input = new { } }], "tool_use"),
            ClaudeMessage([new { type = "text", text = "Có 4 sảnh." }], "end_turn"),
        ]);
        var model = new ClaudeChatModel(p => { requests.Add(p); return Task.FromResult(replies.Dequeue()); }, new ChatOptions(), true);
        var session = model.NewSession();
        session.AddUser("Có mấy sảnh?");

        var first = await session.NextAsync("system", ChatTools.Specs);
        Assert.Equal("tu_1", Assert.Single(first.ToolCalls).Id);
        session.AddToolResults([new ChatToolOutput("tu_1", "list_halls", "[]", false)]);
        var second = await session.NextAsync("system", ChatTools.Specs);

        Assert.Equal("Có 4 sảnh.", second.Text);
        Assert.Equal(3, requests[1].Messages.Count); // hỏi, tool_use, tool_result
        Assert.NotNull(requests[0].Fallbacks);       // claude-opus-5-5 có dự phòng

        var haiku = new ClaudeChatModel(p => { requests.Add(p); return Task.FromResult(ClaudeMessage([new { type = "text", text = "ok" }], "end_turn")); },
            new ChatOptions { Model = "claude-haiku-5-5" }, true).NewSession();
        haiku.AddUser("x");
        await haiku.NextAsync("s", ChatTools.Specs);
        Assert.Null(requests[^1].Fallbacks);          // Haiku không hỗ trợ dự phòng phía máy chủ
    }
}
