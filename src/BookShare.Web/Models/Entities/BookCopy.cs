using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class BookCopy
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public string Barcode { get; set; } = null!;

    public string OwnerType { get; set; } = null!;

    public int? LibraryId { get; set; }

    public string? OwnerUserId { get; set; }

    public string? ShelfLocation { get; set; }

    public string Condition { get; set; } = null!;

    public string Status { get; set; } = null!;

    public bool IsLendable { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Book Book { get; set; } = null!;

    public virtual Library? Library { get; set; }

    public virtual ICollection<LoanRequest> LoanRequests { get; set; } = new List<LoanRequest>();

    public virtual AspNetUser? OwnerUser { get; set; }

    public virtual ICollection<ShelfItem> ShelfItems { get; set; } = new List<ShelfItem>();
}
