using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class ConditionReport
{
    public int Id { get; set; }

    public int LoanId { get; set; }

    public string ReportType { get; set; } = null!;

    public string ReportedByUserId { get; set; } = null!;

    public string ConditionGrade { get; set; } = null!;

    public string? Note { get; set; }

    public string ImageUrl { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Loan Loan { get; set; } = null!;

    public virtual AspNetUser ReportedByUser { get; set; } = null!;
}
