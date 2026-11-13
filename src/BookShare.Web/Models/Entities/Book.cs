using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class Book
{
    public int Id { get; set; }

    public string Title { get; set; } = null!;

    public string Author { get; set; } = null!;

    public string Isbn { get; set; } = null!;

    public string? Publisher { get; set; }

    public int? PublishedYear { get; set; }

    public int PageCount { get; set; }

    public int CategoryId { get; set; }

    public string? CoverImageUrl { get; set; }

    public string? Summary { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<BookCopy> BookCopies { get; set; } = new List<BookCopy>();

    public virtual Category Category { get; set; } = null!;

    public virtual ICollection<ReadingProgress> ReadingProgresses { get; set; } = new List<ReadingProgress>();

    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
