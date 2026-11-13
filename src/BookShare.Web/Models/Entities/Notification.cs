using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class Notification
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string Type { get; set; } = null!;

    public bool IsRead { get; set; }

    public bool EmailSentViaResend { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AspNetUser User { get; set; } = null!;
}
