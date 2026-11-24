using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S02PersonalShelfService : IS02PersonalShelfService
{
    private readonly BookShareDbContext _db;

    public S02PersonalShelfService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S02PersonalShelfPageViewModel> GetPersonalShelfAsync(string userId, int? shelfId = null)
    {
        var user = await _db.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        var hasAnyShelf = await _db.Shelves.AnyAsync(s => s.UserId == userId);
        if (!hasAnyShelf && !string.IsNullOrWhiteSpace(userId))
        {
            var userExists = await _db.AspNetUsers.AnyAsync(u => u.Id == userId);
            if (userExists)
            {
                _db.Shelves.Add(new Shelf
                {
                    UserId = userId,
                    Name = "Kệ sách chính của tôi",
                    Description = "Kệ sách cá nhân mặc định",
                    IsPublic = true,
                    DisplayOrder = 1
                });
                await _db.SaveChangesAsync();
            }
        }

        var shelves = await _db.Shelves
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new ShelfSummaryDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                IsPublic = s.IsPublic,
                ItemCount = s.ShelfItems.Count
            })
            .ToListAsync();

        var copiesQuery = _db.BookCopies
            .Include(c => c.Book)
                .ThenInclude(b => b.Category)
            .Include(c => c.ShelfItems)
                .ThenInclude(si => si.Shelf)
            .Where(c => c.OwnerUserId == userId)
            .AsQueryable();

        var allUserCopies = await copiesQuery
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        int totalCount = allUserCopies.Count;
        int lendableCount = allUserCopies.Count(c => c.IsLendable);

        var filteredCopies = shelfId.HasValue
            ? allUserCopies.Where(c => c.ShelfItems.Any(si => si.ShelfId == shelfId.Value)).ToList()
            : allUserCopies;

        var copyDtos = filteredCopies.Select(c => new PersonalBookCopyDto
        {
            BookCopyId = c.Id,
            BookId = c.BookId,
            Title = c.Book.Title,
            Author = c.Book.Author,
            ISBN = c.Book.Isbn,
            CategoryName = c.Book.Category.Name,
            CoverImageUrl = c.Book.CoverImageUrl,
            Barcode = c.Barcode,
            Condition = c.Condition,
            Status = c.Status,
            IsLendable = c.IsLendable,
            ShelfNames = c.ShelfItems.Select(si => si.Shelf.Name).ToList()
        }).ToList();

        var catalogBooks = await _db.Books
            .OrderBy(b => b.Title)
            .Select(b => new CatalogBookOptionDto
            {
                Id = b.Id,
                Title = b.Title,
                Author = b.Author,
                ISBN = b.Isbn
            })
            .ToListAsync();

        return new S02PersonalShelfPageViewModel
        {
            UserId = userId,
            UserFullName = user?.FullName ?? "Độc giả BookShare",
            TrustScore = user?.TrustScore ?? 100,
            SelectedShelfId = shelfId,
            TotalCopiesCount = totalCount,
            LendableCopiesCount = lendableCount,
            Shelves = shelves,
            Copies = copyDtos,
            CatalogBooks = catalogBooks
        };
    }

    public async Task<(bool Success, string Message, int LendableCount)> ToggleLendableAsync(
        string currentUserId,
        int bookCopyId,
        bool isLendable)
    {
        var copy = await _db.BookCopies
            .Include(c => c.Book)
            .FirstOrDefaultAsync(c => c.Id == bookCopyId);

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách trong CSDL.", 0);
        }

        // Kiểm tra quyền sở hữu bản sao
        if (copy.OwnerUserId != currentUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền thay đổi trạng thái cho mượn đối với sách của người khác.");
        }

        if (!isLendable && (copy.Status is "Borrowed" or "Reserved" or "InTransit"))
        {
            return (false, $"Bản sao [{copy.Barcode}] hiện đang trong quá trình mượn hoặc giữ chỗ nên chưa thể tắt quyền cho mượn.", 0);
        }

        if (isLendable && (copy.Status is "Damaged" or "Lost"))
        {
            return (false, $"Bản sao [{copy.Barcode}] hiện đang ở trạng thái hư hỏng hoặc thất lạc nên không thể mở cho mượn.", 0);
        }

        copy.IsLendable = isLendable;
        await _db.SaveChangesAsync();

        int lendableCount = await _db.BookCopies
            .CountAsync(c => c.OwnerUserId == currentUserId && c.IsLendable);

        string stateText = isLendable ? "MỞ cho mượn P2P" : "KHÓA cho mượn (Chỉ lưu trữ cá nhân)";
        return (true, $"Đã chuyển bản sao {copy.Barcode} ({copy.Book.Title}) sang trạng thái: {stateText}.", lendableCount);
    }

    public async Task<(bool Success, string Message)> CreateShelfAsync(
        string currentUserId,
        CreateShelfRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return (false, "Tên kệ sách không được để trống.");
        }

        int nextOrder = await _db.Shelves.CountAsync(s => s.UserId == currentUserId) + 1;
        var shelf = new Shelf
        {
            UserId = currentUserId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsPublic = request.IsPublic,
            DisplayOrder = nextOrder
        };

        _db.Shelves.Add(shelf);
        await _db.SaveChangesAsync();

        return (true, $"Đã tạo kệ sách mới \"{shelf.Name}\" thành công!");
    }

    public async Task<(bool Success, string Message)> AddBookToShelfAsync(
        string currentUserId,
        AddBookToShelfRequestDto request)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == request.BookId);
        if (book == null)
        {
            return (false, "Đầu sách không tồn tại trong danh mục.");
        }

        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == currentUserId);
        var allowedConditions = new[] { "New", "Good", "Fair", "Worn" };
        string condition = allowedConditions.Contains(request.Condition) ? request.Condition : "Good";

        string barcode = await GenerateUniquePersonalBarcodeAsync();
        int? defaultHubId = await _db.Libraries
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderByDescending(l => l.HubType == "ClubStation" || l.HubType == "DropoffHub")
            .ThenBy(l => l.Id)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync();

        var copy = new BookCopy
        {
            BookId = book.Id,
            Barcode = barcode,
            OwnerType = "User",
            LibraryId = defaultHubId,
            OwnerUserId = currentUserId,
            ShelfLocation = $"Tủ sách {user?.FullName ?? "Cá nhân"}",
            Condition = condition,
            Status = "Available",
            IsLendable = request.IsLendable,
            CreatedAt = DateTime.UtcNow
        };

        _db.BookCopies.Add(copy);
        await _db.SaveChangesAsync();

        if (request.ShelfId > 0)
        {
            var shelf = await _db.Shelves.FirstOrDefaultAsync(s => s.Id == request.ShelfId && s.UserId == currentUserId);
            if (shelf != null)
            {
                _db.ShelfItems.Add(new ShelfItem
                {
                    ShelfId = shelf.Id,
                    BookCopyId = copy.Id,
                    AddedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
        }

        return (true, $"Đã thêm bản sao \"{book.Title}\" (Mã vạch: {barcode}) vào tủ sách cá nhân!");
    }

    public async Task<(bool Success, string Message)> UpdateBookCopyAsync(
        string currentUserId,
        UpdateBookCopyRequestDto request)
    {
        var copy = await _db.BookCopies
            .Include(c => c.Book)
            .Include(c => c.ShelfItems)
            .FirstOrDefaultAsync(c => c.Id == request.BookCopyId);

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách cần chỉnh sửa.");
        }

        if (copy.OwnerUserId != currentUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền chỉnh sửa bản sao sách của người khác.");
        }

        var allowedConditions = new[] { "New", "Good", "Fair", "Worn" };
        if (allowedConditions.Contains(request.Condition))
        {
            copy.Condition = request.Condition;
        }

        if (request.ShelfId.HasValue && request.ShelfId.Value > 0)
        {
            var targetShelf = await _db.Shelves
                .FirstOrDefaultAsync(s => s.Id == request.ShelfId.Value && s.UserId == currentUserId);

            if (targetShelf != null)
            {
                _db.ShelfItems.RemoveRange(copy.ShelfItems);
                _db.ShelfItems.Add(new ShelfItem
                {
                    ShelfId = targetShelf.Id,
                    BookCopyId = copy.Id,
                    AddedAt = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync();
        return (true, $"Đã cập nhật tình trạng ({copy.Condition}) và kệ sách cho bản sao [{copy.Barcode}] — \"{copy.Book.Title}\"!");
    }

    public async Task<(bool Success, string Message)> DeleteBookCopyAsync(
        string currentUserId,
        int bookCopyId)
    {
        var copy = await _db.BookCopies
            .Include(c => c.Book)
            .Include(c => c.ShelfItems)
            .Include(c => c.LoanRequests)
                .ThenInclude(lr => lr.Loan)
            .FirstOrDefaultAsync(c => c.Id == bookCopyId);

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách cần xóa.");
        }

        if (copy.OwnerUserId != currentUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xóa bản sao sách của người khác.");
        }

        if (copy.Status == "Borrowed" || copy.LoanRequests.Any(lr => lr.Loan != null))
        {
            return (false, $"Bản sao [{copy.Barcode}] đang có lịch sử giao dịch mượn trả nên không thể xóa. Bạn có thể tắt chế độ cho cộng đồng mượn để ngừng chia sẻ.");
        }

        _db.ShelfItems.RemoveRange(copy.ShelfItems);
        _db.LoanRequests.RemoveRange(copy.LoanRequests);
        _db.BookCopies.Remove(copy);
        await _db.SaveChangesAsync();

        return (true, $"Đã xóa bản sao [{copy.Barcode}] (\"{copy.Book.Title}\") khỏi tủ sách cá nhân!");
    }

    public async Task<(bool Success, string Message)> DeleteShelfAsync(
        string currentUserId,
        int shelfId)
    {
        var shelf = await _db.Shelves
            .Include(s => s.ShelfItems)
            .FirstOrDefaultAsync(s => s.Id == shelfId);

        if (shelf == null)
        {
            return (false, "Không tìm thấy kệ sách cần xóa.");
        }

        if (shelf.UserId != currentUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xóa kệ sách của người khác.");
        }

        _db.ShelfItems.RemoveRange(shelf.ShelfItems);
        _db.Shelves.Remove(shelf);
        await _db.SaveChangesAsync();

        return (true, $"Đã xóa kệ sách \"{shelf.Name}\" khỏi tủ sách cá nhân!");
    }

    public async Task<(bool Success, string Message)> UpdateShelfAsync(
        string currentUserId,
        UpdateShelfRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return (false, "Tên kệ sách không được để trống.");
        }

        var shelf = await _db.Shelves.FirstOrDefaultAsync(s => s.Id == request.ShelfId);
        if (shelf == null)
        {
            return (false, "Không tìm thấy kệ sách cần cập nhật.");
        }

        if (shelf.UserId != currentUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền chỉnh sửa kệ sách của người dùng khác.");
        }

        shelf.Name = request.Name.Trim();
        shelf.Description = request.Description?.Trim();
        shelf.IsPublic = request.IsPublic;
        await _db.SaveChangesAsync();

        return (true, $"Đã cập nhật thông tin kệ sách \"{shelf.Name}\" thành công!");
    }

    private async Task<string> GenerateUniquePersonalBarcodeAsync()
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            string candidate = $"BS-P2P-{Random.Shared.Next(1000, 9999)}";
            if (!await _db.BookCopies.AnyAsync(c => c.Barcode == candidate))
            {
                return candidate;
            }
        }
        return $"BS-P2P-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
    }
}
