using System;

namespace BookShare.Web.Models.Entities;

// [BS-AGENT-AI-CHAT-ENTITY] Kênh chat trực tiếp giữa Độc giả, Thủ thư và Chủ sách P2P
public partial class SupportChatMessage
{
    public int Id { get; set; }

    public string SenderUserId { get; set; } = null!;

    public string? ReceiverUserId { get; set; }

    public string Channel { get; set; } = "Librarian";

    public string MessageText { get; set; } = null!;

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AspNetUser SenderUser { get; set; } = null!;

    public virtual AspNetUser? ReceiverUser { get; set; }
}
