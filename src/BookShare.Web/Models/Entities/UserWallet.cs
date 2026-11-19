using System;

namespace BookShare.Web.Models.Entities;

// [BS-WALLET-ENTITY] Ví Bảo Lãnh BookShare Pay & Quản lý Cọc Escrow
public partial class UserWallet
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public decimal Balance { get; set; }

    public string PinHash { get; set; } = "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92";

    public DateTime UpdatedAt { get; set; }

    public virtual AspNetUser User { get; set; } = null!;
}
