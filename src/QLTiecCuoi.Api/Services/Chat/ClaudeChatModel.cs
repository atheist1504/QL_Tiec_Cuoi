using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace QLTiecCuoi.Api.Services.Chat;

/// <summary>Kết nối Claude qua SDK chính thức Anthropic (Messages API + tool calling).</summary>
public class ClaudeChatModel(Func<MessageCreateParams, Task<BetaMessage>> send, ChatOptions options, bool configured) : IChatModel
{
    public const string DefaultModel = "claude-opus-5-5";

    // Các model nhận dự phòng phía máy chủ khi bộ lọc an toàn từ chối nhầm. Haiku không hỗ trợ.
    private static readonly HashSet<string> FallbackModels =
        ["claude-fable-5-2", "claude-fable-5-1", "claude-mythos-5-2", "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5"];

    public ClaudeChatModel(AnthropicClient client, ChatOptions options, bool configured)
        : this(p => client.Beta.Messages.Create(p), options, configured) { }

    public bool IsConfigured => configured;
    public string DisplayName => $"Claude ({Model})";
    private string Model => string.IsNullOrWhiteSpace(options.Model) ? DefaultModel : options.Model;
    private Task<BetaMessage> Send(MessageCreateParams request) => send(request);

    public IChatSession NewSession() => new Session(this);

    private sealed class Session(ClaudeChatModel owner) : IChatSession
    {
        private readonly List<BetaMessageParam> _messages = [];

        public void AddUser(string text) => _messages.Add(new BetaUserMessageParam { Content = text });

        public void AddToolResults(IReadOnlyList<ChatToolOutput> results) => _messages.Add(new BetaUserMessageParam
        {
            Content = results.Select(r => (BetaContentBlockParam)new BetaToolResultBlockParam
            {
                ToolUseID = r.Id, Content = r.Result, IsError = r.IsError,
            }).ToList(),
        });

        public async Task<ModelTurn> NextAsync(string systemPrompt, IReadOnlyList<ChatToolSpec> tools)
        {
            BetaMessage resp;
            try
            {
                resp = await owner.Send(owner.BuildRequest(systemPrompt, tools, _messages));
            }
            catch (Exception ex) when (ex.GetType().Namespace?.StartsWith("Anthropic", StringComparison.Ordinal) == true
                                       || ex is HttpRequestException or TaskCanceledException)
            {
                throw new ChatModelException($"Không gọi được Claude API ({ex.GetType().Name}). Hãy thử lại sau.");
            }

            _messages.Add(new BetaAssistantMessageParam { Content = ToParams(resp.Content) });
            var text = string.Join("\n\n", resp.Content.Select(b => b.TryPickText(out var t) ? t.Text : null).OfType<string>());
            if (resp.StopReason == "refusal") return new ModelTurn(text, [], Refused: true);
            var calls = resp.StopReason == "tool_use"
                ? resp.Content.Select(b => b.TryPickToolUse(out var u) ? new ChatToolRequest(u.ID, u.Name, u.Input) : null)
                    .OfType<ChatToolRequest>().ToList()
                : [];
            return new ModelTurn(text, calls);
        }
    }

    private MessageCreateParams BuildRequest(string systemPrompt, IReadOnlyList<ChatToolSpec> tools, List<BetaMessageParam> messages)
    {
        var useFallback = FallbackModels.Contains(Model);
        return new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 16000,
            System = systemPrompt,
            Messages = [.. messages],
            Tools = tools.Select(s => (BetaToolUnion)new BetaTool
            {
                Name = s.Name,
                Description = s.Description,
                InputSchema = new()
                {
                    Properties = s.Properties.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)),
                    Required = [.. s.Required],
                },
            }).ToList(),
            OutputConfig = new BetaOutputConfig
            {
                Effort = (options.Effort ?? "medium").ToLowerInvariant() switch
                {
                    "low" => Effort.Low,
                    "high" => Effort.High,
                    _ => Effort.Medium,
                },
            },
            // Bị bộ lọc an toàn từ chối nhầm thì máy chủ tự chuyển sang model dự phòng phù hợp.
            Betas = useFallback ? ["server-side-fallback-2026-07-01"] : null,
            Fallbacks = useFallback ? new Default() : (BetaFallbacksParam?)null,
        };
    }

    /// <summary>Chuyển nội dung trả về thành tham số để gửi lại nguyên vẹn (giữ chữ ký của thinking).</summary>
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
