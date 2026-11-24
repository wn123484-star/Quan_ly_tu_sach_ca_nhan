using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class Transaction
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public int? LoanId { get; set; }

    public string TransactionType { get; set; } = null!;

    public decimal Amount { get; set; }

    public string PaymentGateway { get; set; } = null!;

    public string PaymentStatus { get; set; } = null!;

    public string? HmacSignature { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Loan? Loan { get; set; }

    public virtual AspNetUser User { get; set; } = null!;
}
