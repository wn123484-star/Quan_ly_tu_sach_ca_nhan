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

public class DeleteReadingProgressRequestDto
{
    public int ProgressId { get; set; }
}

/* =========================================================================
   [BS-READING-STUDIO] VIEWMODELS & DTOS CHO BOOKSHARE READING STUDIO (IM LẶNG 100%)
   ========================================================================= */
public class ReadingStudioPageViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public int SelectedBookId { get; set; }
    public int? SelectedProgressId { get; set; }
    public string BookTitle { get; set; } = "Chọn một cuốn sách để bắt đầu phiên đọc";
    public string BookAuthor { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; } = 200;
    public double PercentCompleted => TotalPages > 0 ? Math.Round(CurrentPage * 100.0 / TotalPages, 1) : 0;

    // Đối chiếu hợp đồng thuê sách (Loans) để tính tốc độ kịp hạn trả
    public int? ActiveLoanId { get; set; }
    public string? ActiveLoanBarcode { get; set; }
    public DateTime? ActiveLoanDueDate { get; set; }
    public int DaysRemainingUntilDue { get; set; }
    public int RemainingPages => Math.Max(0, TotalPages - CurrentPage);
    public int RequiredPagesPerDay { get; set; }
    public bool CanQuickRenew { get; set; }
    public decimal QuickRenewFee { get; set; }

    // Thống kê hiệu suất đọc (từ ReadingSessions)
    public double PagesPerHour { get; set; }
    public int ReadingStreakDays { get; set; }
    public int TotalMinutesLogged { get; set; }
    public int TotalSessionsLogged { get; set; }

    public List<ReadingStudioBookOptionDto> AvailableBooks { get; set; } = new();
    public List<ReadingSessionItemDto> RecentSessions { get; set; } = new();
    public List<BookNoteItemDto> QuoteFlashcards { get; set; } = new();
}

public class ReadingStudioBookOptionDto
{
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public bool IsActiveLoan { get; set; }
}

public class ReadingSessionItemDto
{
    public int SessionId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int PagesRead { get; set; }
    public DateOnly SessionDate { get; set; }
}

public class LogReadingSessionRequestDto
{
    public int BookId { get; set; }
    public int DurationMinutes { get; set; } = 25;
    public int PagesRead { get; set; } = 10;
}

public class SaveVoiceQuoteRequestDto
{
    public int BookId { get; set; }
    public int PageNumber { get; set; } = 1;
    public string QuoteText { get; set; } = string.Empty;
    public string? NoteContent { get; set; }
}

public class QuickRenewLoanRequestDto
{
    public int LoanId { get; set; }
}
