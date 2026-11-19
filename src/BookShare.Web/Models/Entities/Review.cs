using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class Review
{
    public int Id { get; set; }

    public string ReviewerId { get; set; } = null!;

    public string TargetType { get; set; } = null!;

    public string TargetId { get; set; } = null!;

    public int? LoanId { get; set; }

    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Loan? Loan { get; set; }

    public virtual AspNetUser Reviewer { get; set; } = null!;
}
