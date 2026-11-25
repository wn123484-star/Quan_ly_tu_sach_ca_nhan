namespace BookShare.Web.Models.ViewModels;

public class PortalHomeViewModel
{
    public string? Keyword { get; set; }
    public string SourceFilter { get; set; } = "all";
    public string? CategorySlug { get; set; }
    public string AvailabilityFilter { get; set; } = "all"; // all / Available / Borrowed
    public string ConditionFilter { get; set; } = "all";    // all / New / Good / Fair / Worn
    public string SortBy { get; set; } = "available";       // available / newest / title / pages
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
    public int TotalFilteredCopies { get; set; }
    public int TotalPages => PageSize > 0 ? Math.Max(1, (int)Math.Ceiling(TotalFilteredCopies / (double)PageSize)) : 1;
    public int TotalBooksCount { get; set; }
    public int TotalCopiesCount { get; set; }
    public int AvailableCopiesCount { get; set; }
    public List<CategoryPortalDto> Categories { get; set; } = new();
    public List<PortalBookCardDto> BookCards { get; set; } = new();
}

public class CategoryPortalDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string IconClass { get; set; } = "bi-book";
    public int BookCount { get; set; }
}

public class PortalBookCardDto
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
    public string OwnerDisplayName { get; set; } = string.Empty;
    public int? OwnerTrustScore { get; set; }
    public string LocationDisplay { get; set; } = string.Empty;
    public string Condition { get; set; } = "Good";
    public string Status { get; set; } = "Available";
    public int QueueCount { get; set; }
}
