using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class OtpVerification
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    public string Email { get; set; } = null!;

    public string OtpCodeHash { get; set; } = null!;

    public string Purpose { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public bool IsUsed { get; set; }

    public string? ResendMessageId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AspNetUser? User { get; set; }
}
