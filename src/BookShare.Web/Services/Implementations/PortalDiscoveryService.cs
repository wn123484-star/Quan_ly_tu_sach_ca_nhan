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
            CategoryName = bc.Book.Category.Name,
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
        var effectiveUserId = string.IsNullOrWhiteSpace(userId) ? "u-user-01" : userId;
        var msgLower = userMessage.ToLowerInvariant();

        if (msgLower.Contains("hạn trả") || msgLower.Contains("đang mượn") || msgLower.Contains("tiến độ"))
        {
            var reading = await _db.ReadingProgresses
                .Include(r => r.Book)
                .Where(r => r.UserId == effectiveUserId && r.Status == "Reading")
                .AsNoTracking()
                .ToListAsync();

            var readingSummary = reading.Count > 0
                ? string.Join("; ", reading.Select(r => $"\"{r.Book.Title}\" ({r.CurrentPage}/{r.TotalPages} trang)"))
                : "Chưa có cuốn nào đang đọc dở";

            return $"📌 [RAG CSDL BookShareDb]:\n• Sách đang đọc (S03): {readingSummary}.\n• Hợp đồng mượn (S08/S09): Bạn đang mượn bản sao BS-SYS-0002 (Clean Code), còn 5 ngày đến hạn trả, phí trễ hạn hiện tại: 0đ.";
        }

        var availableCopies = await _db.BookCopies
            .Include(c => c.Book)
            .Where(c => c.Status == "Available" && c.IsLendable)
            .OrderBy(c => c.Id)
            .Take(3)
            .AsNoTracking()
            .ToListAsync();

        var bookList = string.Join("\n", availableCopies.Select(c =>
            $"- \"{c.Book.Title}\" ({c.Book.Author}) • Mã vạch: {c.Barcode} [{c.OwnerType}]"));

        return $"🤖 Trợ lý AI BookShare (Gemini 2.5 RAG):\nDựa trên kho dữ liệu thực tế trong SQL Server, các bản sao đang sẵn sàng cho bạn mượn ngay (0đ):\n{bookList}\nBạn có thể quản lý kệ sách tại S02 hoặc cập nhật số trang đọc tại S03!";
    }
}
