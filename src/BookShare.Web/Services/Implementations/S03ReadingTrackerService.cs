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
            UserFullName = user?.FullName ?? "Độc giả BookShare",
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

        return (true, $"Đã thêm \"{book.Title}\" ({book.PageCount} trang) vào bảng theo dõi tiến độ đọc!");
    }

    public async Task<(bool Success, string Message)> DeleteReadingProgressAsync(
        string currentUserId,
        int progressId)
    {
        var rp = await _db.ReadingProgresses
            .Include(r => r.Book)
            .Include(r => r.BookNotes)
            .FirstOrDefaultAsync(r => r.Id == progressId);

        if (rp == null)
        {
            return (false, "Không tìm thấy mục tiêu đọc sách cần xóa.");
        }

        if (rp.UserId != currentUserId)
        {
            throw new UnauthorizedAccessException("Chặn IDOR: Bạn không có quyền xóa mục tiêu đọc sách của người dùng khác.");
        }

        if (rp.BookNotes.Any())
        {
            _db.BookNotes.RemoveRange(rp.BookNotes);
        }

        _db.ReadingProgresses.Remove(rp);
        await _db.SaveChangesAsync();

        return (true, $"Đã xóa cuốn \"{rp.Book.Title}\" khỏi danh sách theo dõi tiến độ đọc!");
    }

    // [BS-READING-STUDIO] Nạp dữ liệu Reading Studio đối chiếu trực tiếp Loans + ReadingSessions + BookNotes
    public async Task<ReadingStudioPageViewModel> GetReadingStudioAsync(string userId, int? bookId = null)
    {
        var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        var activeLoans = await _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .Where(l => l.LoanRequest.BorrowerId == userId && (l.Status == "Active" || l.Status == "Overdue"))
            .OrderBy(l => l.DueDate)
            .ToListAsync();

        var progresses = await _db.ReadingProgresses
            .AsNoTracking()
            .Include(rp => rp.Book)
            .Include(rp => rp.BookNotes)
            .Where(rp => rp.UserId == userId)
            .OrderByDescending(rp => rp.UpdatedAt)
            .ToListAsync();

        var loanedBookIds = activeLoans.Select(l => l.LoanRequest.BookCopy.BookId).ToHashSet();

        var bookOptions = new List<ReadingStudioBookOptionDto>();
        foreach (var l in activeLoans)
        {
            var b = l.LoanRequest.BookCopy.Book;
            if (bookOptions.Any(x => x.BookId == b.Id)) continue;
            var rp = progresses.FirstOrDefault(p => p.BookId == b.Id);
            bookOptions.Add(new ReadingStudioBookOptionDto
            {
                BookId = b.Id,
                Title = b.Title,
                Author = b.Author,
                CurrentPage = rp?.CurrentPage ?? 0,
                TotalPages = rp?.TotalPages ?? b.PageCount,
                IsActiveLoan = true
            });
        }
        foreach (var rp in progresses)
        {
            if (bookOptions.Any(x => x.BookId == rp.BookId)) continue;
            bookOptions.Add(new ReadingStudioBookOptionDto
            {
                BookId = rp.BookId,
                Title = rp.Book.Title,
                Author = rp.Book.Author,
                CurrentPage = rp.CurrentPage,
                TotalPages = rp.TotalPages,
                IsActiveLoan = loanedBookIds.Contains(rp.BookId)
            });
        }

        if (!bookOptions.Any())
        {
            var fallbackBooks = await _db.Books.AsNoTracking().OrderBy(b => b.Id).Take(8).ToListAsync();
            foreach (var b in fallbackBooks)
            {
                bookOptions.Add(new ReadingStudioBookOptionDto
                {
                    BookId = b.Id,
                    Title = b.Title,
                    Author = b.Author,
                    CurrentPage = 0,
                    TotalPages = b.PageCount,
                    IsActiveLoan = false
                });
            }
        }

        int selectedBookId = bookId ?? bookOptions.FirstOrDefault()?.BookId ?? 0;
        var selectedBook = selectedBookId > 0
            ? await _db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == selectedBookId)
            : null;
        var selectedRp = progresses.FirstOrDefault(p => p.BookId == selectedBookId);
        var activeLoanForBook = activeLoans.FirstOrDefault(l => l.LoanRequest.BookCopy.BookId == selectedBookId)
            ?? activeLoans.FirstOrDefault();

        int curPage = selectedRp?.CurrentPage ?? 0;
        int totPages = selectedRp?.TotalPages ?? selectedBook?.PageCount ?? 200;
        int remPages = Math.Max(0, totPages - curPage);

        int daysLeft = 0;
        int reqPerDay = 0;
        if (activeLoanForBook != null)
        {
            daysLeft = Math.Max(0, (int)Math.Ceiling((activeLoanForBook.DueDate - DateTime.UtcNow).TotalDays));
            reqPerDay = remPages > 0 ? (int)Math.Ceiling(remPages / (double)Math.Max(1, daysLeft)) : 0;
        }
        else if (selectedRp?.TargetDeadline != null)
        {
            var dl = selectedRp.TargetDeadline.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            daysLeft = Math.Max(0, (int)Math.Ceiling((dl - DateTime.UtcNow).TotalDays));
            reqPerDay = remPages > 0 ? (int)Math.Ceiling(remPages / (double)Math.Max(1, daysLeft)) : 0;
        }

        var sessions = await _db.ReadingSessions
            .AsNoTracking()
            .Include(s => s.Book)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(30)
            .ToListAsync();

        int totalMins = sessions.Sum(s => s.DurationMinutes);
        int totalPagesInSessions = sessions.Sum(s => s.PagesRead);
        double pagesPerHour = totalMins > 0 ? Math.Round(totalPagesInSessions * 60.0 / totalMins, 1) : 0;
        int streak = ComputeReadingStreak(sessions.Select(s => s.SessionDate).Distinct().OrderByDescending(d => d).ToList());

        var flashcards = progresses
            .Where(p => selectedBookId == 0 || p.BookId == selectedBookId)
            .SelectMany(p => p.BookNotes)
            .OrderByDescending(n => n.CreatedAt)
            .Take(15)
            .Select(n => new BookNoteItemDto
            {
                Id = n.Id,
                PageNumber = n.PageNumber,
                QuoteText = n.QuoteText,
                NoteContent = n.NoteContent
            })
            .ToList();

        if (!flashcards.Any())
        {
            flashcards = progresses
                .SelectMany(p => p.BookNotes)
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .Select(n => new BookNoteItemDto
                {
                    Id = n.Id,
                    PageNumber = n.PageNumber,
                    QuoteText = n.QuoteText,
                    NoteContent = n.NoteContent
                })
                .ToList();
        }

        return new ReadingStudioPageViewModel
        {
            UserId = userId,
            UserFullName = user?.FullName ?? "Độc giả BookShare",
            SelectedBookId = selectedBookId,
            SelectedProgressId = selectedRp?.Id,
            BookTitle = selectedBook?.Title ?? "Chưa có sách trong thư viện",
            BookAuthor = selectedBook?.Author ?? string.Empty,
            CoverImageUrl = selectedBook?.CoverImageUrl,
            CurrentPage = curPage,
            TotalPages = totPages,
            ActiveLoanId = activeLoanForBook?.Id,
            ActiveLoanBarcode = activeLoanForBook?.LoanRequest.BookCopy.Barcode,
            ActiveLoanDueDate = activeLoanForBook?.DueDate,
            DaysRemainingUntilDue = daysLeft,
            RequiredPagesPerDay = reqPerDay,
            CanQuickRenew = activeLoanForBook != null && activeLoanForBook.Status == "Active" && activeLoanForBook.RenewCount < 2,
            QuickRenewFee = activeLoanForBook != null ? activeLoanForBook.LoanRequest.BookCopy.DailyRentalFee * 7m : 0m,
            PagesPerHour = pagesPerHour,
            ReadingStreakDays = streak,
            TotalMinutesLogged = totalMins,
            TotalSessionsLogged = sessions.Count,
            AvailableBooks = bookOptions,
            RecentSessions = sessions.Take(8).Select(s => new ReadingSessionItemDto
            {
                SessionId = s.Id,
                BookTitle = s.Book.Title,
                DurationMinutes = s.DurationMinutes,
                PagesRead = s.PagesRead,
                SessionDate = s.SessionDate
            }).ToList(),
            QuoteFlashcards = flashcards
        };
    }

    public async Task<(bool Success, string Message, int NewCurrentPage, double NewPercent, double PagesPerHour, int StreakDays)> LogReadingSessionAsync(
        string currentUserId,
        LogReadingSessionRequestDto request)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == request.BookId)
            ?? await _db.Books.OrderBy(b => b.Id).FirstOrDefaultAsync();

        if (book == null)
        {
            return (false, "Không tìm thấy đầu sách để ghi nhận phiên đọc.", 0, 0, 0, 0);
        }

        int mins = Math.Clamp(request.DurationMinutes, 1, 300);
        int pages = Math.Clamp(request.PagesRead, 0, 500);
        var now = DateTime.UtcNow;

        _db.ReadingSessions.Add(new ReadingSession
        {
            UserId = currentUserId,
            BookId = book.Id,
            DurationMinutes = mins,
            PagesRead = pages,
            SessionDate = DateOnly.FromDateTime(now),
            CreatedAt = now
        });

        var rp = await _db.ReadingProgresses
            .FirstOrDefaultAsync(p => p.UserId == currentUserId && p.BookId == book.Id);

        if (rp == null)
        {
            rp = new ReadingProgress
            {
                UserId = currentUserId,
                BookId = book.Id,
                CurrentPage = Math.Min(book.PageCount, pages),
                TotalPages = book.PageCount,
                Priority = "High",
                TargetDeadline = DateOnly.FromDateTime(now.AddDays(14)),
                Status = pages >= book.PageCount ? "Completed" : (pages > 0 ? "Reading" : "WantToRead"),
                StartedAt = now,
                FinishedAt = pages >= book.PageCount ? now : null,
                UpdatedAt = now
            };
            _db.ReadingProgresses.Add(rp);
        }
        else
        {
            rp.CurrentPage = Math.Clamp(rp.CurrentPage + pages, 0, rp.TotalPages);
            rp.Status = rp.CurrentPage >= rp.TotalPages ? "Completed" : (rp.CurrentPage > 0 ? "Reading" : "WantToRead");
            rp.StartedAt ??= now;
            rp.FinishedAt = rp.Status == "Completed" ? now : null;
            rp.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();

        var allSessions = await _db.ReadingSessions
            .AsNoTracking()
            .Where(s => s.UserId == currentUserId)
            .ToListAsync();

        int totalMins = allSessions.Sum(s => s.DurationMinutes);
        int totalPages = allSessions.Sum(s => s.PagesRead);
        double pph = totalMins > 0 ? Math.Round(totalPages * 60.0 / totalMins, 1) : 0;
        int streak = ComputeReadingStreak(allSessions.Select(s => s.SessionDate).Distinct().OrderByDescending(d => d).ToList());
        double pct = rp.TotalPages > 0 ? Math.Round(rp.CurrentPage * 100.0 / rp.TotalPages, 1) : 0;

        return (true,
            $"Đã lưu phiên đọc {mins} phút (+{pages} trang) cho \"{book.Title}\"! Tiến độ mới: {rp.CurrentPage}/{rp.TotalPages} ({pct}%).",
            rp.CurrentPage,
            pct,
            pph,
            streak);
    }

    public async Task<(bool Success, string Message, int NoteId)> SaveVoiceQuoteAsync(
        string currentUserId,
        SaveVoiceQuoteRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.QuoteText))
        {
            return (false, "Nội dung trích dẫn không được để trống.", 0);
        }

        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == request.BookId)
            ?? await _db.Books.OrderBy(b => b.Id).FirstOrDefaultAsync();

        if (book == null)
        {
            return (false, "Không tìm thấy đầu sách tương ứng để lưu trích dẫn.", 0);
        }

        var now = DateTime.UtcNow;
        var rp = await _db.ReadingProgresses
            .FirstOrDefaultAsync(p => p.UserId == currentUserId && p.BookId == book.Id);

        if (rp == null)
        {
            rp = new ReadingProgress
            {
                UserId = currentUserId,
                BookId = book.Id,
                CurrentPage = Math.Clamp(request.PageNumber, 1, book.PageCount),
                TotalPages = book.PageCount,
                Priority = "Medium",
                Status = "Reading",
                StartedAt = now,
                UpdatedAt = now
            };
            _db.ReadingProgresses.Add(rp);
            await _db.SaveChangesAsync();
        }

        var note = new BookNote
        {
            ReadingProgressId = rp.Id,
            PageNumber = Math.Max(1, request.PageNumber),
            QuoteText = request.QuoteText.Trim(),
            NoteContent = string.IsNullOrWhiteSpace(request.NoteContent)
                ? $"Trích dẫn giọng nói từ Reading Studio (Sách: {book.Title})."
                : request.NoteContent.Trim(),
            IsPublic = false,
            CreatedAt = now
        };

        _db.BookNotes.Add(note);
        await _db.SaveChangesAsync();

        return (true, $"Đã lưu câu trích dẫn trang {note.PageNumber} vào Sổ Tay Ghi Chú & Bộ thẻ Flashcards!", note.Id);
    }

    private static int ComputeReadingStreak(List<DateOnly> distinctDescDates)
    {
        if (!distinctDescDates.Any()) return 0;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cursor = distinctDescDates[0];
        if (cursor < today.AddDays(-1)) return 0;

        int streak = 0;
        foreach (var d in distinctDescDates)
        {
            if (d == cursor)
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }
            else if (d < cursor)
            {
                break;
            }
        }
        return streak;
    }
}
