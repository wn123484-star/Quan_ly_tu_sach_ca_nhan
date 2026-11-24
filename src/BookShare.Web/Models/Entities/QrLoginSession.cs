using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class QrLoginSession
{
    public int Id { get; set; }

    public string TokenHash { get; set; } = null!;

    public string FlowType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? InitiatorUserId { get; set; }

    public string? ApprovedUserId { get; set; }

    public string? DeviceInfo { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public virtual AspNetUser? ApprovedUser { get; set; }

    public virtual AspNetUser? InitiatorUser { get; set; }
}
