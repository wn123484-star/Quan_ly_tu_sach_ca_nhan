using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class Dispute
{
    public int Id { get; set; }

    public int LoanId { get; set; }

    public string RaisedByUserId { get; set; } = null!;

    public string Reason { get; set; } = null!;

    public string? EvidenceUrlsJson { get; set; }

    public string Status { get; set; } = null!;

    public string? ResolvedByAdminId { get; set; }

    public string? ResolutionNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public virtual Loan Loan { get; set; } = null!;

    public virtual AspNetUser RaisedByUser { get; set; } = null!;

    public virtual AspNetUser? ResolvedByAdmin { get; set; }
}
