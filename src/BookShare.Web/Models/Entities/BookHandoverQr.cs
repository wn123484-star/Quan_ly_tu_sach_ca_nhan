using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class BookHandoverQr
{
    public int Id { get; set; }

    public int LoanRequestId { get; set; }

    public int? LoanId { get; set; }

    public string HandoverType { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public string ShortCodeHash { get; set; } = null!;

    public string CreatedByUserId { get; set; } = null!;

    public string? ConsumedByUserId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public virtual AspNetUser? ConsumedByUser { get; set; }

    public virtual AspNetUser CreatedByUser { get; set; } = null!;

    public virtual Loan? Loan { get; set; }

    public virtual LoanRequest LoanRequest { get; set; } = null!;
}
