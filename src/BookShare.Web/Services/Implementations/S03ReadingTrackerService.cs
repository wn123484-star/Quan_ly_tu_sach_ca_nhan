using System.Text.Json;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S03ReadingTrackerService : IS03ReadingTrackerService
{
    private readonly BookShareDbContext _db;

    public S03ReadingTrackerService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S03ReadingTrackerPageViewModel> GetReadingTrackerAsync(string userId)
    {
        var user = await _db.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        var progresses = await _db.ReadingProgresses
            .Include(rp => rp.Book)
                .ThenInclude(b => b.Category)
            .Include(rp => rp.BookNotes)
            .Where(rp => rp.UserId == userId)
            .OrderByDescending(rp => rp.UpdatedAt)
            .AsNoTracking()
            .ToListAsync();

        var items = progresses.Select(rp => new ReadingProgressItemDto
        {
            Id = rp.Id,
            BookId = rp.BookId,
            Title = rp.Book.Title,
            Author = rp.Book.Author,
            CategoryName = rp.Book.Category.Name,
            CoverImageUrl = rp.Book.CoverImageUrl,
            CurrentPage = rp.CurrentPage,
            TotalPages = rp.TotalPages,
            Priority = rp.Priority,
            TargetDeadline = rp.TargetDeadline,
            Status = rp.Status,
            PersonalRating = rp.PersonalRating,
            Notes = rp.BookNotes
                .OrderBy(n => n.PageNumber)
                .Select(n => new BookNoteItemDto
                {
                    Id = n.Id,
                    PageNumber = n.PageNumber,
                    QuoteText = n.QuoteText,
                    NoteContent = n.NoteContent
                })
                .ToList()
        }).ToList();

        int completedCount = items.Count(i => i.Status == "Completed");
        int readingCount = items.Count(i => i.Status == "Reading");
        int wantCount = items.Count(i => i.Status == "WantToRead");
        int totalPagesRead = items.Sum(i => i.CurrentPage);
        int goalTarget = 10;
        int goalPercent = Math.Min(100, (int)Math.Round(completedCount * 100.0 / goalTarget));

        var trackedBookIds = items.Select(i => i.BookId).ToHashSet();
        var untrackedBooks = await _db.Books
            .Where(b => !trackedBookIds.Contains(b.Id))
            .OrderBy(b => b.Title)
            .Select(b => new CatalogBookOptionDto
            {
                Id = b.Id,
                Title = b.Title,
                Author = b.Author,
                ISBN = b.Isbn
            })
            .ToListAsync();

        var chartPayload = new
        {
            completedCount,
            readingCount,
            wantToReadCount = wantCount,
            bookProgressLabels = items.Select(i => i.Title.Length > 22 ? i.Title[..22] + "..." : i.Title).ToArray(),
            bookProgressPercents = items.Select(i => i.PercentCompleted).ToArray(),
            books = items
        };

        return new S03ReadingTrackerPageViewModel
        {
            UserId = userId,
            UserFullName = user?.FullName ?? "Nguyễn Hùng (BIT250157)",
            TotalTrackedBooks = items.Count,
            CompletedCount = completedCount,
            ReadingCount = readingCount,
            WantToReadCount = wantCount,
            HighPriorityCount = items.Count(i => i.Priority == "High"),
            MediumPriorityCount = items.Count(i => i.Priority == "Medium"),
            LowPriorityCount = items.Count(i => i.Priority == "Low"),
            TotalPagesRead = totalPagesRead,
            YearlyGoalTarget = goalTarget,
            YearlyGoalPercent = goalPercent,
            ChartJsonPayload = JsonSerializer.Serialize(chartPayload),
            ProgressList = items,
            UntrackedBooks = untrackedBooks
        };
    }

    public async Task<(bool Success, string Message, string StatusLabel, double Percent)> UpdateReadingProgressAsync(
        string currentUserId,
        UpdateReadingProgressRequestDto request)
    {
        var rp = await _db.ReadingProgresses
            .Include(r => r.Book)
            .FirstOrDefaultAsync(r => r.Id == request.ProgressId);

        if (rp == null)
        {
            return (false, "Không tìm thấy bản ghi tiến độ đọc sách.", "", 0);
        }

        // Chống IDOR theo đúng RULES.md
        if (rp.UserId != currentUserId)
        {
            throw new UnauthorizedAccessException("Chặn IDOR: Bạn không thể sửa tiến độ đọc sách của người dùng khác.");
        }

        int clampedPage = Math.Clamp(request.CurrentPage, 0, rp.TotalPages);
        rp.CurrentPage = clampedPage;

        var allowedPriorities = new[] { "Low", "Medium", "High" };
        if (allowedPriorities.Contains(request.Priority))
        {
            rp.Priority = request.Priority;
        }

        if (request.PersonalRating.HasValue && request.PersonalRating.Value is >= 1 and <= 5)
        {
            rp.PersonalRating = request.PersonalRating.Value;
        }

        if (clampedPage == 0)
        {
            rp.Status = "WantToRead";
            rp.FinishedAt = null;
        }
        else if (clampedPage >= rp.TotalPages)
        {
            rp.Status = "Completed";
            rp.StartedAt ??= DateTime.UtcNow;
            rp.FinishedAt = DateTime.UtcNow;
        }
        else
        {
            rp.Status = "Reading";
            rp.StartedAt ??= DateTime.UtcNow;
            rp.FinishedAt = null;
        }

        rp.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        double pct = Math.Round((double)rp.CurrentPage * 100.0 / rp.TotalPages, 1);
        string statusLabel = rp.Status switch
        {
            "Completed" => "Đã đọc xong (100%)",
            "Reading" => $"Đang đọc ({pct}%)",
            _ => "Muốn đọc (0%)"
        };

        return (true, $"Đã lưu tiến độ cuốn \"{rp.Book.Title}\": Trang {rp.CurrentPage}/{rp.TotalPages} ({pct}%).", statusLabel, pct);
    }

    public async Task<(bool Success, string Message)> AddBookToTrackerAsync(
        string currentUserId,
        AddTrackingBookRequestDto request)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == request.BookId);
        if (book == null)
        {
            return (false, "Không tìm thấy đầu sách cần theo dõi.");
        }

        bool alreadyTracked = await _db.ReadingProgresses
            .AnyAsync(r => r.UserId == currentUserId && r.BookId == request.BookId);
        if (alreadyTracked)
        {
            return (false, "Đầu sách này đã có trong danh sách theo dõi đọc của bạn.");
        }

        var allowedPriorities = new[] { "Low", "Medium", "High" };
        string priority = allowedPriorities.Contains(request.Priority) ? request.Priority : "Medium";

        var rp = new ReadingProgress
        {
            UserId = currentUserId,
            BookId = book.Id,
            CurrentPage = 0,
            TotalPages = book.PageCount,
            Priority = priority,
            TargetDeadline = request.TargetDeadline ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21)),
            Status = "WantToRead",
            UpdatedAt = DateTime.UtcNow
        };

        _db.ReadingProgresses.Add(rp);
        await _db.SaveChangesAsync();

        return (true, $"Đã thêm \"{book.Title}\" ({book.PageCount} trang) vào trình theo dõi tiến độ đọc (S03)!");
    }
}
