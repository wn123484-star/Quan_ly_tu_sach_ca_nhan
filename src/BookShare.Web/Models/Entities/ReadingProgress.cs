using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class ReadingProgress
{
    public int Id { get; set; }

    public string UserId { get; set; } = null!;

    public int BookId { get; set; }

    public int CurrentPage { get; set; }

    public int TotalPages { get; set; }

    public string Priority { get; set; } = null!;

    public DateOnly? TargetDeadline { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int? PersonalRating { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Book Book { get; set; } = null!;

    public virtual ICollection<BookNote> BookNotes { get; set; } = new List<BookNote>();

    public virtual AspNetUser User { get; set; } = null!;
}
