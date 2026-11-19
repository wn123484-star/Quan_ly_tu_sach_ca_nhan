using System;
using System.Collections.Generic;

namespace BookShare.Web.Models.Entities;

public partial class BookNote
{
    public int Id { get; set; }

    public int ReadingProgressId { get; set; }

    public int PageNumber { get; set; }

    public string? QuoteText { get; set; }

    public string NoteContent { get; set; } = null!;

    public bool IsPublic { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ReadingProgress ReadingProgress { get; set; } = null!;
}
