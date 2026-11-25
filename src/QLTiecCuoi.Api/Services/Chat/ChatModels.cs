using System.Text.Json;

namespace QLTiecCuoi.Api.Services.Chat;

/// <summary>Model yêu cầu gọi một công cụ. Input = null nghĩa là tham số không phải JSON hợp lệ.</summary>
public record ChatToolRequest(string Id, string Name, IReadOnlyDictionary<string, JsonElement>? Input);

/// <summary>Kết quả chạy công cụ gửi lại cho model.</summary>
public record ChatToolOutput(string Id, string Name, string Result, bool IsError);

/// <summary>Một lượt trả lời của model: chữ, các lời gọi công cụ, hoặc bị từ chối.</summary>
public record ModelTurn(string Text, List<ChatToolRequest> ToolCalls, bool Refused = false);

/// <summary>Lỗi khi gọi nhà cung cấp LLM (mạng, hết lượt, sai key...). Message hiện được cho người dùng.</summary>
public class ChatModelException(string message) : Exception(message);

/// <summary>
/// Nhà cung cấp LLM cho chatbot. Mỗi nhà cung cấp giữ lịch sử hội thoại theo định dạng riêng
/// (ví dụ khối thinking của Claude, chữ ký suy nghĩ của Gemini) và gửi lại nguyên vẹn.
/// Đổi nhà cung cấp bằng cấu hình "Chat:Provider", không phải sửa ChatService.
/// </summary>
public interface IChatModel
{
    bool IsConfigured { get; }
    string DisplayName { get; }
    IChatSession NewSession();
}

/// <summary>Lịch sử một hội thoại, chỉ nối thêm (append-only).</summary>
public interface IChatSession
{
    void AddUser(string text);
    void AddToolResults(IReadOnlyList<ChatToolOutput> results);

    /// <summary>Gửi lịch sử cho model, nối câu trả lời vào lịch sử rồi trả về.</summary>
    Task<ModelTurn> NextAsync(string systemPrompt, IReadOnlyList<ChatToolSpec> tools);
}
