using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S06SearchDiscoveryService : IS06SearchDiscoveryService
{
    private readonly BookShareDbContext _db;

    public S06SearchDiscoveryService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S06SearchDiscoveryPageViewModel> SearchCopiesAsync(S06SearchDiscoveryFilterDto filter)
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new CategoryPortalDto
            {
                Id = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                IconClass = c.IconClass,
                BookCount = c.Books.Count
            })
            .ToListAsync();

        var pickupHubs = await _db.Libraries
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Id)
            .Select(l => new LibraryOptionDto
            {
                Id = l.Id,
                Name = l.Name,
                HubType = l.HubType
            })
            .ToListAsync();

        var query = _db.BookCopies
            .AsNoTracking()
            .Include(bc => bc.Book)
                .ThenInclude(b => b.Category)
            .Include(bc => bc.Book)
                .ThenInclude(b => b.Reservations)
            .Include(bc => bc.Library)
            .Include(bc => bc.OwnerUser)
            .Where(bc => bc.IsLendable)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim().ToLower();
            query = query.Where(bc =>
                bc.Book.Title.ToLower().Contains(kw) ||
                bc.Book.Author.ToLower().Contains(kw) ||
                bc.Book.Isbn.ToLower().Contains(kw) ||
                bc.Barcode.ToLower().Contains(kw));
        }

        if (!string.IsNullOrWhiteSpace(filter.Author))
        {
            var authorKw = filter.Author.Trim().ToLower();
            query = query.Where(bc => bc.Book.Author.ToLower().Contains(authorKw));
        }

        if (!string.IsNullOrWhiteSpace(filter.Isbn))
        {
            var isbnKw = filter.Isbn.Trim().Replace("-", "").ToLower();
            query = query.Where(bc => bc.Book.Isbn.Replace("-", "").ToLower().Contains(isbnKw));
        }

        if (filter.CategoryId.HasValue && filter.CategoryId.Value > 0)
        {
            query = query.Where(bc => bc.Book.CategoryId == filter.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.AreaFilter))
        {
            var areaKw = filter.AreaFilter.Trim().ToLower();
            query = query.Where(bc =>
                (bc.Library != null && bc.Library.Address.ToLower().Contains(areaKw)) ||
                (bc.OwnerUser != null && bc.OwnerUser.Address != null && bc.OwnerUser.Address.ToLower().Contains(areaKw)));
        }

        if (!string.IsNullOrWhiteSpace(filter.OwnerType) && filter.OwnerType != "all")
        {
            query = query.Where(bc => bc.OwnerType == filter.OwnerType);
        }

        if (!string.IsNullOrWhiteSpace(filter.StatusFilter) && filter.StatusFilter != "all")
        {
            query = query.Where(bc => bc.Status == filter.StatusFilter);
        }

        if (filter.MinTrustScore > 0)
        {
            query = query.Where(bc =>
                bc.OwnerType == "System" ||
                (bc.OwnerUser != null && bc.OwnerUser.TrustScore >= filter.MinTrustScore));
        }

        var matchedCopies = await query
            .OrderByDescending(bc => bc.Status == "Available")
            .ThenByDescending(bc => bc.Id)
            .ToListAsync();

        int totalItems = matchedCopies.Count;
        int availableCount = matchedCopies.Count(r => r.Status == "Available");
        int systemCount = matchedCopies.Count(r => r.OwnerType == "System");
        int p2pCount = matchedCopies.Count(r => r.OwnerType == "User");

        var allowedSizes = new[] { 6, 12, 24 };
        int pageSize = allowedSizes.Contains(filter.PageSize) ? filter.PageSize : 6;
        int totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        int currentPage = Math.Clamp(filter.Page, 1, totalPages);

        var pagedCopies = matchedCopies
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var results = pagedCopies.Select(bc => new S06DiscoveredCopyDto
        {
            BookCopyId = bc.Id,
            BookId = bc.BookId,
            Title = bc.Book.Title,
            Author = bc.Book.Author,
            ISBN = bc.Book.Isbn,
            CategoryName = bc.Book.Category.Name,
            PageCount = bc.Book.PageCount,
            CoverImageUrl = bc.Book.CoverImageUrl,
            Barcode = bc.Barcode,
            OwnerType = bc.OwnerType,
            OwnerName = bc.OwnerType == "System"
                ? (bc.Library?.Name ?? "Kho chung Thư viện")
                : (bc.OwnerUser?.FullName ?? "Độc giả P2P"),
            AreaAddress = bc.OwnerType == "System"
                ? (bc.Library?.Address ?? "Cầu Giấy, Hà Nội")
                : (bc.OwnerUser?.Address ?? "Hà Nội"),
            TrustScore = bc.OwnerType == "System"
                ? 100
                : (bc.OwnerUser?.TrustScore ?? 100),
            ShelfLocation = bc.ShelfLocation ?? "Kệ mở",
            Condition = bc.Condition,
            Status = bc.Status,
            QueueCount = bc.Book.Reservations.Count(r => r.Status == "Waiting")
        }).ToList();

        filter.Page = currentPage;
        filter.PageSize = pageSize;

        return new S06SearchDiscoveryPageViewModel
        {
            Filter = filter,
            Categories = categories,
            PickupHubs = pickupHubs,
            TotalMatchedCopies = totalItems,
            AvailableCopiesCount = availableCount,
            SystemCopiesCount = systemCount,
            P2PCopiesCount = p2pCount,
            CurrentPage = currentPage,
            PageSize = pageSize,
            TotalPages = totalPages,
            TotalItems = totalItems,
            Results = results
        };
    }

    public async Task<(bool Success, string Message, int LoanRequestId)> CreateQuickLoanRequestAsync(
        string borrowerUserId,
        QuickLoanRequestDto request)
    {
        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .FirstOrDefaultAsync(bc => bc.Id == request.BookCopyId);

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách vật lý này.", 0);
        }

        if (!copy.IsLendable)
        {
            return (false, "Bản sao sách này hiện đang khóa cho mượn.", 0);
        }

        if (copy.OwnerUserId == borrowerUserId)
        {
            return (false, "Đây là sách thuộc tủ cá nhân của chính bạn.", 0);
        }

        var existingPending = await _db.LoanRequests
            .AnyAsync(lr => lr.BookCopyId == copy.Id && lr.BorrowerId == borrowerUserId && lr.Status == "Pending");

        if (existingPending)
        {
            return (false, "Bạn đã gửi yêu cầu mượn cho cuốn sách này và đang chờ duyệt.", 0);
        }

        var days = Math.Clamp(request.DesiredDays, 3, 30);
        var loanType = copy.OwnerType == "System" ? "Library" : "P2P";

        var newLoan = new LoanRequest
        {
            BookCopyId = copy.Id,
            BorrowerId = borrowerUserId,
            ApproverId = copy.OwnerType == "User" ? copy.OwnerUserId : "u-lib-01",
            LoanType = loanType,
            Status = "Pending",
            RequestedAt = DateTime.UtcNow,
            DesiredDays = days,
            PickupHubId = request.PickupHubId ?? copy.LibraryId ?? 3,
            RequestNote = string.IsNullOrWhiteSpace(request.RequestNote)
                ? $"Đăng ký mượn sách '{copy.Book.Title}' ({days} ngày) từ Cổng Tìm kiếm S06."
                : request.RequestNote.Trim()
        };

        _db.LoanRequests.Add(newLoan);
        await _db.SaveChangesAsync();

        return (true, $"Đã gửi yêu cầu mượn bản sao [{copy.Barcode}] ({copy.Book.Title}) thành công! Mã phiếu: #{newLoan.Id}", newLoan.Id);
    }
}
