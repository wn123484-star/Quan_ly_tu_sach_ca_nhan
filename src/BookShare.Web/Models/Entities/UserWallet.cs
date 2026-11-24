using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class UserWallet
{
    public string UserId { get; set; } = null!;

    public decimal Balance { get; set; }

    public decimal TotalToppedUp { get; set; }

    public decimal TotalSpent { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual AspNetUser User { get; set; } = null!;
}
