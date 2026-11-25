using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QLTiecCuoi.Api.Services.Chat;

/// <summary>
/// Kết nối các dịch vụ có API chuẩn OpenAI Chat Completions: Gemini (mặc định), Groq, OpenRouter, Ollama...
/// Gọi thẳng bằng HttpClient, không cần thư viện riêng.
/// </summary>
public class OpenAiCompatibleChatModel(HttpClient http, ChatOptions options, string? apiKey, string providerName) : IChatModel
{
    public const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/";
    public const string GeminiDefaultModel = "gemini-3.6-flash";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey) || options.AllowNoApiKey;
    public string DisplayName => $"{providerName} ({Model})";
    private string Model => string.IsNullOrWhiteSpace(options.Model) ? GeminiDefaultModel : options.Model;
    private string? Effort => options.Effort;
    private string BaseUrl
    {
        get
        {
            var url = string.IsNullOrWhiteSpace(options.BaseUrl) ? GeminiBaseUrl : options.BaseUrl;
            return url.EndsWith('/') ? url : url + "/";
        }
    }

    public IChatSession NewSession() => new Session(this);

    private sealed class Session(OpenAiCompatibleChatModel owner) : IChatSession
    {
        // Lưu nguyên văn tin nhắn model trả về (kể cả trường lạ như chữ ký suy nghĩ của Gemini) để gửi lại đúng như cũ.
        private readonly JsonArray _messages = [];
        private int _generatedIds;

        public void AddUser(string text) => _messages.Add(new JsonObject { ["role"] = "user", ["content"] = text });

        public void AddToolResults(IReadOnlyList<ChatToolOutput> results)
        {
            foreach (var r in results)
                _messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = r.Id, ["content"] = r.Result });
        }

        public async Task<ModelTurn> NextAsync(string systemPrompt, IReadOnlyList<ChatToolSpec> tools)
        {
            var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = systemPrompt } };
            foreach (var m in _messages) messages.Add(m!.DeepClone());

            var body = new JsonObject
            {
                ["model"] = owner.Model,
                ["messages"] = messages,
                ["tools"] = new JsonArray(tools.Select(ToolJson).ToArray<JsonNode?>()),
                ["tool_choice"] = "auto",
            };
            if (!string.IsNullOrWhiteSpace(owner.Effort)) body["reasoning_effort"] = owner.Effort;

            var reply = await owner.PostAsync(body);
            var choice = reply["choices"]?[0] ?? throw new ChatModelException("Phản hồi của dịch vụ AI không có nội dung.");
            var message = choice["message"]?.AsObject() ?? throw new ChatModelException("Phản hồi của dịch vụ AI không có nội dung.");
            var finish = choice["finish_reason"]?.GetValue<string>();

            var calls = new List<ChatToolRequest>();
            if (message["tool_calls"] is JsonArray toolCalls)
            {
                foreach (var tc in toolCalls.OfType<JsonObject>())
                {
                    // Một số dịch vụ trả id rỗng: tự tạo id và ghi vào tin nhắn được lưu để kết quả khớp.
                    var id = tc["id"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(id))
                    {
                        id = $"call_{++_generatedIds}";
                        tc["id"] = id;
                    }
                    var fn = tc["function"];
                    calls.Add(new ChatToolRequest(id, fn?["name"]?.GetValue<string>() ?? "", ParseArguments(fn?["arguments"])));
                }
            }
            _messages.Add(message.DeepClone());

            var text = message["content"] switch
            {
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                JsonArray parts => string.Join("", parts.Select(p => p?["text"]?.GetValue<string>() ?? "")),
                _ => "",
            };
            return new ModelTurn(text, calls, Refused: finish == "content_filter");
        }

        private static IReadOnlyDictionary<string, JsonElement>? ParseArguments(JsonNode? args)
        {
            try
            {
                var json = args is JsonValue v && v.TryGetValue<string>(out var s) ? s : args?.ToJsonString() ?? "{}";
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private static JsonObject ToolJson(ChatToolSpec s)
    {
        var fn = new JsonObject { ["name"] = s.Name, ["description"] = s.Description };
        // Công cụ không có tham số thì bỏ "parameters" (một số dịch vụ không nhận object rỗng).
        if (s.Properties.Count > 0)
        {
            fn["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = JsonSerializer.SerializeToNode(s.Properties),
                ["required"] = new JsonArray(s.Required.Select(r => (JsonNode?)r).ToArray()),
            };
        }
        return new JsonObject { ["type"] = "function", ["function"] = fn };
    }

    private async Task<JsonNode> PostAsync(JsonObject body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(apiKey)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ChatModelException($"Không kết nối được {providerName}. Kiểm tra mạng rồi thử lại.");
        }

        using (res)
        {
            var raw = await res.Content.ReadAsStringAsync();
            if (res.IsSuccessStatusCode)
                return JsonNode.Parse(raw) ?? throw new ChatModelException($"{providerName} trả về dữ liệu rỗng.");

            var detail = TryErrorMessage(raw);
            throw new ChatModelException(res.StatusCode switch
            {
                HttpStatusCode.TooManyRequests =>
                    $"Đã hết lượt gọi của {providerName} (gói miễn phí giới hạn theo phút và theo ngày). Đợi ít phút rồi thử lại.",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    $"{providerName} từ chối API key (HTTP {(int)res.StatusCode}). Kiểm tra lại key. {detail}",
                HttpStatusCode.NotFound =>
                    $"{providerName} không tìm thấy model \"{Model}\". Kiểm tra tên model trong cấu hình Chat:Model. {detail}",
                _ => $"{providerName} báo lỗi (HTTP {(int)res.StatusCode}). {detail}",
            });
        }
    }

    private static string TryErrorMessage(string raw)
    {
        try
        {
            var node = JsonNode.Parse(raw);
            var msg = (node is JsonArray arr ? arr[0] : node)?["error"]?["message"]?.GetValue<string>();
            return msg is null ? "" : msg.Length > 300 ? msg[..300] : msg;
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
