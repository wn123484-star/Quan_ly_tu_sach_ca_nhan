using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

public interface IS02PersonalShelfService
{
    Task<S02PersonalShelfPageViewModel> GetPersonalShelfAsync(string userId, int? shelfId = null);

    Task<(bool Success, string Message, int LendableCount)> ToggleLendableAsync(
        string currentUserId,
        int bookCopyId,
        bool isLendable);

    Task<(bool Success, string Message)> CreateShelfAsync(
        string currentUserId,
        CreateShelfRequestDto request);

    Task<(bool Success, string Message)> AddBookToShelfAsync(
        string currentUserId,
        AddBookToShelfRequestDto request);

    Task<(bool Success, string Message)> UpdateBookCopyAsync(
        string currentUserId,
        UpdateBookCopyRequestDto request);

    Task<(bool Success, string Message)> DeleteBookCopyAsync(
        string currentUserId,
        int bookCopyId);

        Task<(bool Success, string Message)> UpdateShelfAsync(
            string currentUserId,
            UpdateShelfRequestDto request);

        Task<(bool Success, string Message)> DeleteShelfAsync(
            string currentUserId,
            int shelfId);
    }

public interface IS03ReadingTrackerService
{
    Task<S03ReadingTrackerPageViewModel> GetReadingTrackerAsync(string userId);

    Task<(bool Success, string Message, string StatusLabel, double Percent)> UpdateReadingProgressAsync(
        string currentUserId,
        UpdateReadingProgressRequestDto request);

        Task<(bool Success, string Message)> AddBookToTrackerAsync(
            string currentUserId,
            AddTrackingBookRequestDto request);

        Task<(bool Success, string Message)> DeleteReadingProgressAsync(
            string currentUserId,
            int progressId);

    // [BS-READING-STUDIO] Reading Studio Im Lặng (Pomodoro, Lộ trình kịp hạn thuê, Voice-to-Quote, Flashcards)
    Task<ReadingStudioPageViewModel> GetReadingStudioAsync(string userId, int? bookId = null);

    Task<(bool Success, string Message, int NewCurrentPage, double NewPercent, double PagesPerHour, int StreakDays)> LogReadingSessionAsync(
        string currentUserId,
        LogReadingSessionRequestDto request);

    Task<(bool Success, string Message, int NoteId)> SaveVoiceQuoteAsync(
        string currentUserId,
        SaveVoiceQuoteRequestDto request);
    }

public interface IPortalDiscoveryService
{
    Task<PortalHomeViewModel> GetPortalHomeAsync(
        string? keyword = null,
        string sourceFilter = "all",
        string? categorySlug = null);

    Task<string> GetQuickAiReplyAsync(string? userId, string userMessage);
}
