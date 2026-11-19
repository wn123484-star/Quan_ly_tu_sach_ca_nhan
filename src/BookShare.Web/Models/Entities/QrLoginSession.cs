using System;

namespace BookShare.Web.Models.Entities;

// [BS-QRLOGIN-ENTITY] Phiên quét QR đăng nhập chéo Mobile -> Web PC
public partial class QrLoginSession
{
    public int Id { get; set; }

    public string Token { get; set; } = null!;

    public string Status { get; set; } = "Pending"; // Pending / Approved / Claimed / Expired

    public string? ApprovedByUserId { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AspNetUser? ApprovedByUser { get; set; }
}
