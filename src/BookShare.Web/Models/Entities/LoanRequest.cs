using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class LoanRequest
{
    public int Id { get; set; }

    public int BookCopyId { get; set; }

    public string BorrowerId { get; set; } = null!;

    public string? ApproverId { get; set; }

    public string LoanType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime RequestedAt { get; set; }

    public int DesiredDays { get; set; }

    public int? PickupHubId { get; set; }

    public string? RequestNote { get; set; }

    public virtual AspNetUser? Approver { get; set; }

    public virtual BookCopy BookCopy { get; set; } = null!;

    public virtual AspNetUser Borrower { get; set; } = null!;

    public virtual Loan? Loan { get; set; }

    public virtual Library? PickupHub { get; set; }
}
