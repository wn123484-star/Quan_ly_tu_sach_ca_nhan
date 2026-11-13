namespace BookShare.Web.Models.ViewModels;

/* =========================================================================
   S04: BOOK NOTES & QUOTES VIEWMODELS
   ========================================================================= */
public class S04BookNotesPageViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public int? SelectedBookId { get; set; }
    public bool ShowCommunityPublic { get; set; }
    public int TotalMyNotesCount { get; set; }
    public int PublicNotesCount { get; set; }
    public List<CatalogBookOptionDto> MyTrackedBooks { get; set; } = new();
    public List<S04NoteCardDto> Notes { get; set; } = new();
}

public class S04NoteCardDto
{
    public int NoteId { get; set; }
    public int ReadingProgressId { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string BookAuthor { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public int PageNumber { get; set; }
    public string? QuoteText { get; set; }
    public string NoteContent { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string OwnerFullName { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SaveBookNoteRequestDto
{
    public int? NoteId { get; set; }
    public int BookId { get; set; }
    public int PageNumber { get; set; } = 1;
    public string? QuoteText { get; set; }
    public string NoteContent { get; set; } = string.Empty;
    public bool IsPublic { get; set; } = true;
}

public class ToggleNotePublicRequestDto
{
    public int NoteId { get; set; }
    public bool IsPublic { get; set; }
}

public class DeleteBookNoteRequestDto
{
    public int NoteId { get; set; }
}

/* =========================================================================
   S05: ISBN AUTO CATALOG VIEWMODELS
   ========================================================================= */
public class S05IsbnCatalogPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = "User";
    public List<CategoryPortalDto> Categories { get; set; } = new();
    public List<LibraryOptionDto> Libraries { get; set; } = new();
    public List<IsbnPresetSampleDto> PresetSamples { get; set; } = new();
    public List<CatalogBookRecentDto> RecentCatalogBooks { get; set; } = new();
}

public class LibraryOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string HubType { get; set; } = string.Empty;
}

public class IsbnPresetSampleDto
{
    public string ISBN { get; set; } = string.Empty;
    public string ShortLabel { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
}

public class IsbnLookupResultDto
{
    public bool Found { get; set; }
    public string Source { get; set; } = "InternalCatalog";
    public string ISBN { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public int PublishedYear { get; set; } = 2024;
    public int PageCount { get; set; } = 320;
    public int CategoryId { get; set; } = 1;
    public string CoverImageUrl { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool AlreadyExistsInDb { get; set; }
    public int? ExistingBookId { get; set; }
}

public class SaveIsbnBookAndCopyRequestDto
{
    public string ISBN { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public int? PublishedYear { get; set; }
    public int PageCount { get; set; } = 250;
    public int CategoryId { get; set; } = 1;
    public string? CoverImageUrl { get; set; }
    public string? Summary { get; set; }
    public string TargetOwnerType { get; set; } = "User"; // User (Tủ cá nhân) hoặc System (Kho thư viện)
    public int? LibraryId { get; set; } = 1;
    public string? ShelfLocation { get; set; }
    public string Condition { get; set; } = "New";
    public bool IsLendable { get; set; } = true;
}

public class CatalogBookRecentDto
{
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public int? PublishedYear { get; set; }
    public int PageCount { get; set; }
    public string? CoverImageUrl { get; set; }
    public int CopyCount { get; set; }
}

/* =========================================================================
   S06: SEARCH & DISCOVERY VIEWMODELS
   ========================================================================= */
public class S06SearchDiscoveryFilterDto
{
    public string? Keyword { get; set; }
    public string? Author { get; set; }
    public string? Isbn { get; set; }
    public int? CategoryId { get; set; }
    public string? AreaFilter { get; set; }
    public string OwnerType { get; set; } = "all"; // all / System / User
    public string StatusFilter { get; set; } = "all"; // all / Available / Borrowed / Reserved
    public int MinTrustScore { get; set; } = 0;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 6;
}

public class S06SearchDiscoveryPageViewModel
{
    public S06SearchDiscoveryFilterDto Filter { get; set; } = new();
    public List<CategoryPortalDto> Categories { get; set; } = new();
    public List<LibraryOptionDto> PickupHubs { get; set; } = new();
    public int TotalMatchedCopies { get; set; }
    public int AvailableCopiesCount { get; set; }
    public int SystemCopiesCount { get; set; }
    public int P2PCopiesCount { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 6;
    public int TotalPages { get; set; } = 1;
    public int TotalItems { get; set; }
    public List<S06DiscoveredCopyDto> Results { get; set; } = new();
}

public class S06DiscoveredCopyDto
{
    public int BookCopyId { get; set; }
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public string? CoverImageUrl { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string OwnerType { get; set; } = "System";
    public string OwnerName { get; set; } = string.Empty;
    public string AreaAddress { get; set; } = string.Empty;
    public int TrustScore { get; set; } = 100;
    public string ShelfLocation { get; set; } = string.Empty;
    public string Condition { get; set; } = "Good";
    public string Status { get; set; } = "Available";
    public int QueueCount { get; set; }
}

public class QuickLoanRequestDto
{
    public int BookCopyId { get; set; }
    public int DesiredDays { get; set; } = 14;
    public int? PickupHubId { get; set; } = 3;
    public string? RequestNote { get; set; }
}

/* =========================================================================
   S07: LIBRARY CATALOG VIEWMODELS
   ========================================================================= */
public class S07LibraryCatalogPageViewModel
{
    public int? SelectedLibraryId { get; set; }
    public string StatusFilter { get; set; } = "all";
    public string? Keyword { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCount { get; set; }
    public int ReservedCount { get; set; }
    public int BorrowedCount { get; set; }
    public int DamagedOrLostCount { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 6;
    public int TotalPages { get; set; } = 1;
    public int TotalItems { get; set; }
    public List<S07LibraryPartnerDto> Libraries { get; set; } = new();
    public List<S07LibraryCopyItemDto> Copies { get; set; } = new();
}

public class S07UpdateShelfRequestDto
{
    public int BookCopyId { get; set; }
    public string ShelfLocation { get; set; } = string.Empty;
}

public class S07LibraryPartnerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ManagerName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string HubType { get; set; } = string.Empty;
    public string? OpenHours { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
}

public class S07LibraryCopyItemDto
{
    public int BookCopyId { get; set; }
    public int BookId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public string ShelfLocation { get; set; } = string.Empty;
    public string Condition { get; set; } = "Good";
    public string Status { get; set; } = "Available";
    public string OwnerType { get; set; } = "System";
}
