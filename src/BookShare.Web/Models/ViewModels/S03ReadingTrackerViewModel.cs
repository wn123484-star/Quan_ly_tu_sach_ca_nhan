namespace BookShare.Web.Models.ViewModels;

public class S03ReadingTrackerPageViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public int TotalTrackedBooks { get; set; }
    public int CompletedCount { get; set; }
    public int ReadingCount { get; set; }
    public int WantToReadCount { get; set; }
    public int HighPriorityCount { get; set; }
    public int MediumPriorityCount { get; set; }
    public int LowPriorityCount { get; set; }
    public int TotalPagesRead { get; set; }
    public int YearlyGoalTarget { get; set; } = 15;
    public int YearlyGoalPercent { get; set; }
    public string ChartJsonPayload { get; set; } = "{}";
    public List<ReadingProgressItemDto> ProgressList { get; set; } = new();
    public List<CatalogBookOptionDto> UntrackedBooks { get; set; } = new();
}

public class ReadingProgressItemDto
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public double PercentCompleted => TotalPages > 0 ? Math.Round((double)CurrentPage * 100.0 / TotalPages, 1) : 0;
    public string Priority { get; set; } = "Medium";
    public DateOnly? TargetDeadline { get; set; }
    public string Status { get; set; } = "Reading";
    public int? PersonalRating { get; set; }
    public List<BookNoteItemDto> Notes { get; set; } = new();
}

public class BookNoteItemDto
{
    public int Id { get; set; }
    public int PageNumber { get; set; }
    public string? QuoteText { get; set; }
    public string NoteContent { get; set; } = string.Empty;
}

public class UpdateReadingProgressRequestDto
{
    public int ProgressId { get; set; }
    public int CurrentPage { get; set; }
    public string Priority { get; set; } = "Medium";
    public int? PersonalRating { get; set; }
}

public class AddTrackingBookRequestDto
{
    public int BookId { get; set; }
    public string Priority { get; set; } = "Medium";
    public DateOnly? TargetDeadline { get; set; }
}
