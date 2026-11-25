using Microsoft.AspNetCore.Mvc;
using QLTiecCuoi.Api.Services.Chat;
using QLTiecCuoi.Shared;

namespace QLTiecCuoi.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController(ChatService service) : ControllerBase
{
    /// <summary>Gửi câu hỏi cho chatbot. Bỏ trống conversationId để bắt đầu hội thoại mới.</summary>
    [HttpPost]
    public Task<ChatReplyDto> Send(ChatRequest req) => service.SendAsync(req);

    /// <summary>Xác nhận hoặc từ chối thao tác ghi dữ liệu chatbot đề xuất.</summary>
    [HttpPost("{conversationId}/confirm")]
    public Task<ChatReplyDto> Confirm(string conversationId, ChatConfirmRequest req) => service.ConfirmAsync(conversationId, req.Approve);
}
