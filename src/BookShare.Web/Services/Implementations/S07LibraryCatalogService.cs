using BookShare.Web.Data;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S07LibraryCatalogService : IS07LibraryCatalogService
{
    private readonly BookShareDbContext _db;

    public S07LibraryCatalogService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S07LibraryCatalogPageViewModel> GetLibraryCatalogAsync(
        int? libraryId = null,
        string statusFilter = "all",
        string? keyword = null,
        int page = 1,
        int pageSize = 6)
    {
        var libraries = await _db.Libraries
            .AsNoTracking()
            .Include(l => l.ManagerUser)
            .Include(l => l.BookCopies)
            .Where(l => l.IsActive)
            .OrderBy(l => l.Id)
            .Select(l => new S07LibraryPartnerDto
            {
                Id = l.Id,
                Name = l.Name,
                Address = l.Address,
                ManagerName = l.ManagerUser != null ? l.ManagerUser.FullName : "Ban quản trị Kho chung",
                Phone = l.Phone,
                HubType = l.HubType,
                OpenHours = l.OpenHours,
                TotalCopies = l.BookCopies.Count,
                AvailableCopies = l.BookCopies.Count(bc => bc.Status == "Available")
            })
            .ToListAsync();

        var baseQuery = _db.BookCopies
            .AsNoTracking()
            .Include(bc => bc.Book)
                .ThenInclude(b => b.Category)
            .Include(bc => bc.Library)
            .Where(bc => bc.LibraryId != null)
            .AsQueryable();

        if (libraryId.HasValue && libraryId.Value > 0)
        {
            baseQuery = baseQuery.Where(bc => bc.LibraryId == libraryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            baseQuery = baseQuery.Where(bc =>
                bc.Barcode.ToLower().Contains(kw) ||
                bc.Book.Title.ToLower().Contains(kw) ||
                bc.Book.Author.ToLower().Contains(kw) ||
                bc.Book.Isbn.ToLower().Contains(kw) ||
                (bc.ShelfLocation != null && bc.ShelfLocation.ToLower().Contains(kw)));
        }

        var allCopiesInScope = await baseQuery
            .OrderBy(bc => bc.LibraryId)
            .ThenBy(bc => bc.ShelfLocation)
            .ThenBy(bc => bc.Barcode)
            .ToListAsync();

        var totalCopies = allCopiesInScope.Count;
        var availableCount = allCopiesInScope.Count(bc => bc.Status == "Available");
        var reservedCount = allCopiesInScope.Count(bc => bc.Status == "Reserved");
        var borrowedCount = allCopiesInScope.Count(bc => bc.Status == "Borrowed");
        var damagedCount = allCopiesInScope.Count(bc => bc.Status == "Damaged" || bc.Status == "Lost");

        var filteredList = allCopiesInScope;
        if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all")
        {
            filteredList = allCopiesInScope
                .Where(bc => bc.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var allowedSizes = new[] { 6, 12, 24 };
        int cleanPageSize = allowedSizes.Contains(pageSize) ? pageSize : 6;
        int totalItems = filteredList.Count;
        int totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)cleanPageSize));
        int currentPage = Math.Clamp(page, 1, totalPages);

        var pagedList = filteredList
            .Skip((currentPage - 1) * cleanPageSize)
            .Take(cleanPageSize)
            .ToList();

        var copyDtos = pagedList.Select(bc => new S07LibraryCopyItemDto
        {
            BookCopyId = bc.Id,
            BookId = bc.BookId,
            Barcode = bc.Barcode,
            Title = bc.Book.Title,
            Author = bc.Book.Author,
            ISBN = bc.Book.Isbn,
            CategoryName = bc.Book.Category.Name,
            CoverImageUrl = bc.Book.CoverImageUrl,
            LibraryId = bc.LibraryId ?? 1,
            LibraryName = bc.Library?.Name ?? "Kho Thư viện",
            ShelfLocation = bc.ShelfLocation ?? "Kệ A1",
            Condition = bc.Condition,
            Status = bc.Status,
            OwnerType = bc.OwnerType
        }).ToList();

        return new S07LibraryCatalogPageViewModel
        {
            SelectedLibraryId = libraryId,
            StatusFilter = statusFilter,
            Keyword = keyword,
            TotalCopies = totalCopies,
            AvailableCount = availableCount,
            ReservedCount = reservedCount,
            BorrowedCount = borrowedCount,
            DamagedOrLostCount = damagedCount,
            CurrentPage = currentPage,
            PageSize = cleanPageSize,
            TotalPages = totalPages,
            TotalItems = totalItems,
            Libraries = libraries,
            Copies = copyDtos
        };
    }

    public async Task<(bool Success, string Message)> UpdateCopyShelfLocationAsync(
        int bookCopyId,
        string shelfLocation)
    {
        if (string.IsNullOrWhiteSpace(shelfLocation))
        {
            return (false, "Vị trí kệ sách (ShelfLocation) không được để trống.");
        }

        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .FirstOrDefaultAsync(bc => bc.Id == bookCopyId);

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách trong kho.");
        }

        copy.ShelfLocation = shelfLocation.Trim();
        await _db.SaveChangesAsync();

        return (true, $"Thủ thư đã cập nhật vị trí kệ cho bản sao [{copy.Barcode}] ({copy.Book.Title}) sang [{copy.ShelfLocation}]!");
    }
}
