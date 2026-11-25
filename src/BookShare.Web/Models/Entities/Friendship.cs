using System;

namespace BookShare.Web.Models.Entities;

/// <summary>
/// [S20] Thực thể kết bạn giữa hai thành viên BookShare (Gửi lời mời, Chấp nhận, Từ chối hoặc Hủy kết bạn).
/// </summary>
public partial class Friendship
{
    public int Id { get; set; }

    public string RequesterId { get; set; } = null!;

    public string AddresseeId { get; set; } = null!;

    public string Status { get; set; } = "Pending"; // Pending / Accepted / Rejected

    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public virtual AspNetUser Requester { get; set; } = null!;

    public virtual AspNetUser Addressee { get; set; } = null!;
}
