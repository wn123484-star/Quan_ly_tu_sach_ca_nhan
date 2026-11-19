namespace BookShare.Web.Models.ViewModels;

// [BS-AGENT-AI-COPILOT] DTO hành động tự thực thi của AI Copilot & Chat trực tiếp Thủ thư
public class AiCopilotActionDto
{
    public string Type { get; set; } = string.Empty; // SET_DARK_MODE / SET_THEME / SEND_EMAIL / UPDATE_PROGRESS / NAVIGATE
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class SupportChatMessageItemDto
{
    public int Id { get; set; }
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string SenderRole { get; set; } = "User";
    public string? ReceiverUserId { get; set; }
    public string Channel { get; set; } = "Librarian";
    public string MessageText { get; set; } = string.Empty;
    public bool IsMine { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SendSupportChatRequestDto
{
    public string Channel { get; set; } = "Librarian"; // Librarian / Owner
    public string? ReceiverUserId { get; set; }
    public string MessageText { get; set; } = string.Empty;
}
