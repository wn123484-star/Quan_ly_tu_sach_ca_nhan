using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

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

public interface IS05IsbnCatalogService
{
    Task<S05IsbnCatalogPageViewModel> GetIsbnCatalogPageAsync(string currentUserId, string currentUserRole);

    Task<IsbnLookupResultDto> LookupByIsbnAsync(string isbn);

    Task<(bool Success, string Message, string Barcode, int BookId)> SaveBookAndCreateCopyAsync(
        string currentUserId,
        SaveIsbnBookAndCopyRequestDto request);
}

public interface IS06SearchDiscoveryService
{
    Task<S06SearchDiscoveryPageViewModel> SearchCopiesAsync(S06SearchDiscoveryFilterDto filter);

    Task<(bool Success, string Message, int LoanRequestId)> CreateQuickLoanRequestAsync(
        string borrowerUserId,
        QuickLoanRequestDto request);
}

public interface IS07LibraryCatalogService
{
    Task<S07LibraryCatalogPageViewModel> GetLibraryCatalogAsync(
        int? libraryId = null,
        string statusFilter = "all",
        string? keyword = null);
}
