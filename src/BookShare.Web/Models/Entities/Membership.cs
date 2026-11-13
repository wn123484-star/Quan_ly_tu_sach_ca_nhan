using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class Membership
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public string Tier { get; set; } = null!;

    public int MaxConcurrentLoans { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public bool IsActive { get; set; }

    public virtual AspNetUser User { get; set; } = null!;
}
