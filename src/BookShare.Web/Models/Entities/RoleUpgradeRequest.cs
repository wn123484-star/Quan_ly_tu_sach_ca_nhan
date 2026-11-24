using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class RoleUpgradeRequest
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public string RequestedRole { get; set; } = null!;

    public string OrganizationName { get; set; } = null!;

    public string Reason { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? ReviewedByUserId { get; set; }

    public string? ReviewNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public virtual AspNetUser? ReviewedByUser { get; set; }

    public virtual AspNetUser User { get; set; } = null!;
}
