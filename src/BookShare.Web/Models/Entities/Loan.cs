using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class Loan
{
    public int Id { get; set; }

    public int LoanRequestId { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime DueDate { get; set; }

    public DateTime? ReturnDate { get; set; }

    public int RenewCount { get; set; }

    public decimal FineAmount { get; set; }

    public string Status { get; set; } = null!;

    public virtual ICollection<ConditionReport> ConditionReports { get; set; } = new List<ConditionReport>();

    public virtual ICollection<Dispute> Disputes { get; set; } = new List<Dispute>();

    public virtual LoanRequest LoanRequest { get; set; } = null!;

    public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
