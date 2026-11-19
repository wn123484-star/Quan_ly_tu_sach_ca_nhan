using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

// [BS-AGENT-AI-CHAT] Dịch vụ Chat trực tiếp giữa Độc giả, Thủ thư & Chủ sách P2P (lưu SQL SupportChatMessages)
public interface ISupportChatService
{
    Task<List<SupportChatMessageItemDto>> GetMessagesAsync(string currentUserId, string currentRole, string channel = "Librarian");
    Task<(bool Success, string Message, SupportChatMessageItemDto? SavedMessage)> SendMessageAsync(
        string senderUserId,
        string senderRole,
        SendSupportChatRequestDto request);
}
