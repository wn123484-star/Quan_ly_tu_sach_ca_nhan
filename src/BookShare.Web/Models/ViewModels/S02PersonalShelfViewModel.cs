namespace BookShare.Web.Models.ViewModels;

public class S02PersonalShelfPageViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public int TrustScore { get; set; }
    public int? SelectedShelfId { get; set; }
    public int TotalCopiesCount { get; set; }
    public int LendableCopiesCount { get; set; }
    public List<ShelfSummaryDto> Shelves { get; set; } = new();
    public List<PersonalBookCopyDto> Copies { get; set; } = new();
    public List<CatalogBookOptionDto> CatalogBooks { get; set; } = new();
}

public class ShelfSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public int ItemCount { get; set; }
}

public class PersonalBookCopyDto
{
    public int BookCopyId { get; set; }
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string Condition { get; set; } = "Good";
    public string Status { get; set; } = "Available";
    public bool IsLendable { get; set; }
    public List<string> ShelfNames { get; set; } = new();
}

public class CatalogBookOptionDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
}

public class ToggleLendableRequestDto
{
    public int BookCopyId { get; set; }
    public bool IsLendable { get; set; }
}

public class CreateShelfRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsPublic { get; set; } = true;
}

public class AddBookToShelfRequestDto
{
    public int BookId { get; set; }
    public int ShelfId { get; set; }
    public string Condition { get; set; } = "Good";
    public bool IsLendable { get; set; } = true;
}
