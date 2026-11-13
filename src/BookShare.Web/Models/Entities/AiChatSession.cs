using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class AiChatSession
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string ModelName { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<AiChatMessage> AiChatMessages { get; set; } = new List<AiChatMessage>();

    public virtual AspNetUser User { get; set; } = null!;
}
