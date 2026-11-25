using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

/// <summary>
/// [S04] Giao diện dịch vụ Sổ tay Ghi chú và Trích dẫn sách theo số trang.
/// </summary>
/// <remarks>Triển khai bởi: S04BookNotesService.</remarks>
public interface IS04BookNotesService
{
    Task<S04BookNotesPageViewModel> GetBookNotesPageAsync(
        string currentUserId,
        int? bookId = null,
        bool showCommunityPublic = false);

    Task<(bool Success, string Message, S04NoteCardDto? Note)> SaveBookNoteAsync(
        string currentUserId,
        SaveBookNoteRequestDto request);

    Task<(bool Success, string Message)> TogglePublicAsync(
        string currentUserId,
        int noteId,
        bool isPublic);

    Task<(bool Success, string Message)> DeleteBookNoteAsync(
        string currentUserId,
        int noteId);
}

/// <summary>
/// [S05] Giao diện dịch vụ Tra cứu ISBN tự động và Biên mục đầu sách/bản sao.
/// </summary>
/// <remarks>Triển khai bởi: S05IsbnCatalogService.</remarks>
public interface IS05IsbnCatalogService
{
    Task<S05IsbnCatalogPageViewModel> GetIsbnCatalogPageAsync(string currentUserId, string currentUserRole);

    Task<IsbnLookupResultDto> LookupByIsbnAsync(string isbn);

    Task<(bool Success, string Message, string Barcode, int BookId)> SaveBookAndCreateCopyAsync(
        string currentUserId,
        string currentUserRole,
        SaveIsbnBookAndCopyRequestDto request);

        Task<(bool Success, string Message)> UpdateCatalogBookAsync(
            UpdateCatalogBookRequestDto request);

        Task<(bool Success, string Message)> DeleteCatalogBookAsync(
            int bookId);
    }

/// <summary>
/// [S06] Giao diện dịch vụ Tìm kiếm đa tiêu chí, Lọc nâng cao và Gợi ý từ khóa tức thì.
/// </summary>
/// <remarks>Triển khai bởi: S06SearchDiscoveryService.</remarks>
public interface IS06SearchDiscoveryService
{
    Task<S06SearchDiscoveryPageViewModel> SearchCopiesAsync(S06SearchDiscoveryFilterDto filter);

    Task<(bool Success, string Message, int LoanRequestId)> CreateQuickLoanRequestAsync(
        string borrowerUserId,
        QuickLoanRequestDto request);
}

/// <summary>
/// [S07] Giao diện dịch vụ Danh mục Kho chung Thư viện đối tác và Điều chuyển vị trí kệ.
/// </summary>
/// <remarks>Triển khai bởi: S07LibraryCatalogService.</remarks>
public interface IS07LibraryCatalogService
{
    Task<S07LibraryCatalogPageViewModel> GetLibraryCatalogAsync(
        int? libraryId = null,
        string statusFilter = "all",
        string? keyword = null,
        int page = 1,
        int pageSize = 6);

    Task<(bool Success, string Message)> UpdateCopyShelfLocationAsync(
        int bookCopyId,
        string shelfLocation);
}
