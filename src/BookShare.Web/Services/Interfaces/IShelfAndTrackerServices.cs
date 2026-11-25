using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

/// <summary>
/// [S02] Giao diện dịch vụ quản lý Tủ sách cá nhân, Kệ sách tùy chỉnh và Bản sao chia sẻ P2P.
/// </summary>
/// <remarks>Triển khai bởi: S02PersonalShelfService.</remarks>
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

/// <summary>
/// [S03] Giao diện dịch vụ Theo dõi tiến độ đọc sách và xuất dữ liệu sao lưu.
/// </summary>
/// <remarks>Triển khai bởi: S03ReadingTrackerService.</remarks>
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
    }

/// <summary>
/// [CORE-HOME] Giao diện dịch vụ Trang chủ BookShare: danh mục khám phá sách đa tiêu chí và câu trả lời nhanh cho khách.
/// </summary>
/// <remarks>Triển khai bởi: PortalDiscoveryService.</remarks>
public interface IPortalDiscoveryService
{
    Task<PortalHomeViewModel> GetPortalHomeAsync(
        string? keyword = null,
        string sourceFilter = "all",
        string? categorySlug = null,
        string availabilityFilter = "all",
        string conditionFilter = "all",
        string sortBy = "available",
        int page = 1,
        int pageSize = 12);

    Task<string> GetQuickAiReplyAsync(string? userId, string userMessage);
}
