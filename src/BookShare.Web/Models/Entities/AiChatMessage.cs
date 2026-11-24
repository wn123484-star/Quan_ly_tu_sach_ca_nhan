using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class AiChatMessage
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    public string UserId { get; set; } = null!;

    public string Sender { get; set; } = null!;

    public string MessageText { get; set; } = null!;

    public int PromptTokens { get; set; }

    public string? ReferencedBookIdsJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AiChatSession Session { get; set; } = null!;

    public virtual AspNetUser User { get; set; } = null!;
}
