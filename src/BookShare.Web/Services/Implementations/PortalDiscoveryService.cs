using BookShare.Web.Data;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class PortalDiscoveryService : IPortalDiscoveryService
{
    private readonly BookShareDbContext _db;

    public PortalDiscoveryService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<PortalHomeViewModel> GetPortalHomeAsync(
        string? keyword = null,
        string sourceFilter = "all",
        string? categorySlug = null)
    {
        var categories = await _db.Categories
            .Select(c => new CategoryPortalDto
            {
                Id = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                IconClass = c.IconClass,
                BookCount = c.Books.Count
            })
            .AsNoTracking()
            .ToListAsync();

        var totalBooksCount = await _db.Books.CountAsync();
        var totalCopiesCount = await _db.BookCopies.CountAsync();

        var query = _db.BookCopies
            .Include(bc => bc.Book)
                .ThenInclude(b => b.Category)
            .Include(bc => bc.Book)
                .ThenInclude(b => b.Reservations)
            .Include(bc => bc.Library)
            .Include(bc => bc.OwnerUser)
            .Where(bc => bc.IsLendable)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(bc =>
                bc.Book.Title.Contains(kw) ||
                bc.Book.Author.Contains(kw) ||
                bc.Book.Isbn.Contains(kw) ||
                bc.Barcode.Contains(kw));
        }

        if (sourceFilter is "System" or "User")
        {
            query = query.Where(bc => bc.OwnerType == sourceFilter);
        }

        if (!string.IsNullOrWhiteSpace(categorySlug) && categorySlug != "all")
        {
            query = query.Where(bc => bc.Book.Category.Slug == categorySlug);
        }

        var copies = await query
            .OrderBy(bc => bc.Status == "Available" ? 0 : 1)
            .ThenBy(bc => bc.Id)
            .Take(12)
            .ToListAsync();

        var cards = copies.Select(bc => new PortalBookCardDto
        {
            BookCopyId = bc.Id,
            BookId = bc.BookId,
            Title = bc.Book.Title,
            Author = bc.Book.Author,
            ISBN = bc.Book.Isbn,
            CategoryName = bc.Book.Category?.Name ?? "Chưa phân loại",
            PageCount = bc.Book.PageCount,
            CoverImageUrl = bc.Book.CoverImageUrl,
            Barcode = bc.Barcode,
            OwnerType = bc.OwnerType,
            OwnerDisplayName = bc.OwnerType == "System"
                ? (bc.Library?.Name ?? "Kho chung Thư viện ĐH CMC")
                : $"{bc.OwnerUser?.FullName ?? "Chủ sách P2P"} (P2P)",
            OwnerTrustScore = bc.OwnerType == "User" ? (bc.OwnerUser?.TrustScore ?? 100) : null,
            LocationDisplay = bc.ShelfLocation ?? (bc.Library?.Name ?? "Trạm CLB Cầu Giấy"),
            Condition = bc.Condition,
            Status = bc.Status,
            QueueCount = bc.Book.Reservations.Count(r => r.Status == "Waiting" || r.Status == "Notified")
        }).ToList();

        return new PortalHomeViewModel
        {
            Keyword = keyword,
            SourceFilter = sourceFilter,
            CategorySlug = categorySlug,
            TotalBooksCount = totalBooksCount,
            TotalCopiesCount = totalCopiesCount,
            Categories = categories,
            BookCards = cards
        };
    }

    public async Task<string> GetQuickAiReplyAsync(string? userId, string userMessage)
    {
        var effectiveUserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        var msgLower = (userMessage ?? string.Empty).ToLowerInvariant();

        if (msgLower.Contains("hạn trả") || msgLower.Contains("đang mượn") || msgLower.Contains("tiến độ"))
        {
            if (effectiveUserId == null)
            {
                return "📌 Vui lòng đăng nhập tài khoản BookShare để tra cứu tiến độ đọc sách và các hợp đồng mượn cá nhân của bạn.";
            }

            var reading = await _db.ReadingProgresses
                .Include(r => r.Book)
                .Where(r => r.UserId == effectiveUserId && r.Status == "Reading")
                .AsNoTracking()
                .ToListAsync();

            var readingSummary = reading.Count > 0
                ? string.Join("; ", reading.Select(r => $"\"{r.Book.Title}\" ({r.CurrentPage}/{r.TotalPages} trang)"))
                : "Chưa có cuốn nào đang đọc dở";

            var activeLoans = await _db.Loans
                .Include(l => l.LoanRequest)
                    .ThenInclude(lr => lr.BookCopy)
                        .ThenInclude(bc => bc.Book)
                .Where(l => l.LoanRequest.BorrowerId == effectiveUserId && (l.Status == "Active" || l.Status == "Overdue"))
                .AsNoTracking()
                .ToListAsync();

            var loanSummary = activeLoans.Count > 0
                ? string.Join("; ", activeLoans.Select(l => $"[{l.LoanRequest.BookCopy.Barcode}] \"{l.LoanRequest.BookCopy.Book.Title}\" (Hạn trả: {l.DueDate:dd/MM/yyyy}, Phí phạt: {l.FineAmount:N0}đ)"))
                : "Hiện chưa có hợp đồng mượn nào đang mở";

            return $"📌 [Trạng thái đọc & Mượn trả của bạn]:\n• Sách đang đọc: {readingSummary}.\n• Hợp đồng mượn hiện tại: {loanSummary}.";
        }

        var availableCopies = await _db.BookCopies
            .Include(c => c.Book)
            .Where(c => c.Status == "Available" && c.IsLendable)
            .OrderBy(c => c.Id)
            .Take(3)
            .AsNoTracking()
            .ToListAsync();

        if (availableCopies.Count == 0)
        {
            return "🤖 Trợ lý AI BookShare:\nHiện kho sách cộng đồng đang ở trạng thái trống và sẵn sàng tiếp nhận đầu sách mới. Bạn có thể tự thêm sách vào Tủ sách cá nhân hoặc liên hệ Quản trị viên để khởi tạo kho sách cộng đồng!";
        }

        var bookList = string.Join("\n", availableCopies.Select(c =>
            $"- \"{c.Book.Title}\" ({c.Book.Author}) • Mã vạch: {c.Barcode} [{(c.OwnerType == "System" ? "Kho thư viện" : "Thành viên chia sẻ")}]"));

        return $"🤖 Trợ lý AI BookShare:\nDựa trên kho sách đang mở mượn, các bản sao sẵn sàng cho bạn mượn ngay (0đ):\n{bookList}\nBạn có thể quản lý Tủ sách cá nhân hoặc cập nhật tiến độ đọc trong Không gian đọc!";
    }
}
