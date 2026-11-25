using System;

namespace BookShare.Web.Models.Entities;

public partial class ClubMember
{
    public int Id { get; set; }

    public int HubId { get; set; }

    public string UserId { get; set; } = null!;

    public string MemberRole { get; set; } = "Member"; // Leader / ViceLeader / Member

    public string Status { get; set; } = "Approved"; // Approved / Pending / Rejected

    public string? JoinNote { get; set; }

    public string? AddedByUserId { get; set; }

    public DateTime JoinedAt { get; set; }

    public virtual Library Hub { get; set; } = null!;

    public virtual AspNetUser User { get; set; } = null!;

    public virtual AspNetUser? AddedByUser { get; set; }
}
