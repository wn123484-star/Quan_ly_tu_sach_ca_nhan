using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S11HotBookQueueService : IS11HotBookQueueService
{
    private readonly BookShareDbContext _db;

    public S11HotBookQueueService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S11HotBookQueuePageViewModel> GetHotBookQueuePageAsync(string currentUserId)
    {
        var user = await _db.AspNetUsers
            .Include(u => u.Memberships)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        string currentTier = user?.Memberships
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.MaxConcurrentLoans)
            .FirstOrDefault()?.Tier ?? "Free";

        var books = await _db.Books
            .AsNoTracking()
            .Include(b => b.BookCopies)
            .Include(b => b.Reservations)
            .OrderByDescending(b => b.Reservations.Count(r => r.Status == "Waiting" || r.Status == "Notified"))
            .ThenBy(b => b.BookCopies.Count(bc => bc.Status == "Available"))
            .Take(12)
            .Select(b => new S11HotBookDto
            {
                BookId = b.Id,
                Title = b.Title,
                Author = b.Author,
                ISBN = b.Isbn,
                CoverImageUrl = b.CoverImageUrl,
                TotalCopies = b.BookCopies.Count,
                AvailableCopies = b.BookCopies.Count(bc => bc.Status == "Available"),
                BorrowedOrReservedCopies = b.BookCopies.Count(bc => bc.Status != "Available"),
                WaitingCount = b.Reservations.Count(r => r.Status == "Waiting" || r.Status == "Notified")
            })
            .ToListAsync();

        var reservations = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Book)
            .Include(r => r.User)
                .ThenInclude(u => u.Memberships)
            .Where(r => r.Status == "Waiting" || r.Status == "Notified")
            .OrderBy(r => r.BookId)
            .ThenBy(r => r.QueuePosition)
            .ToListAsync();

        var queueDtos = reservations.Select(r =>
        {
            string uTier = r.User.Memberships
                .Where(m => m.IsActive)
                .OrderByDescending(m => m.MaxConcurrentLoans)
                .FirstOrDefault()?.Tier ?? "Free";

            return new S11QueueItemDto
            {
                ReservationId = r.Id,
                BookId = r.BookId,
                BookTitle = r.Book.Title,
                BookAuthor = r.Book.Author,
                CoverImageUrl = r.Book.CoverImageUrl,
                UserId = r.UserId,
                UserFullName = r.User.FullName,
                UserTier = uTier,
                QueuePosition = r.QueuePosition,
                Status = r.Status,
                CreatedAt = r.CreatedAt,
                NotifiedAt = r.NotifiedAt,
                ExpiresAt = r.ExpiresAt,
                IsCurrentUser = r.UserId == currentUserId
            };
        }).ToList();

        return new S11HotBookQueuePageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserName = user?.FullName ?? "Nguyễn Hùng (BIT250157)",
            CurrentUserTier = currentTier,
            HotBooks = books,
            ActiveReservations = queueDtos
        };
    }

    public async Task<(bool Success, string Message, int QueuePosition)> EnqueueReservationAsync(
        string currentUserId,
        int bookId)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId);
        if (book == null)
        {
            return (false, "Không tìm thấy đầu sách cần đặt trước.", 0);
        }

        var existing = await _db.Reservations
            .AnyAsync(r => r.BookId == bookId && r.UserId == currentUserId && (r.Status == "Waiting" || r.Status == "Notified"));

        if (existing)
        {
            return (false, $"Bạn đã có tên trong danh sách xếp hàng chờ của sách \"{book.Title}\".", 0);
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Memberships)
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        bool isPremium = user?.Memberships.Any(m => m.IsActive && m.Tier == "Premium") ?? false;

        var waitingQueue = await _db.Reservations
            .Include(r => r.User)
                .ThenInclude(u => u.Memberships)
            .Where(r => r.BookId == bookId && (r.Status == "Waiting" || r.Status == "Notified"))
            .OrderBy(r => r.QueuePosition)
            .ToListAsync();

        int assignedPosition;

        if (isPremium)
        {
            // Thành viên Premium được ưu tiên xếp ngay sau các thành viên Premium khác và đứng trước thành viên Free
            var firstFree = waitingQueue.FirstOrDefault(r =>
                r.Status == "Waiting" &&
                !r.User.Memberships.Any(m => m.IsActive && m.Tier == "Premium"));

            if (firstFree != null)
            {
                assignedPosition = firstFree.QueuePosition;
                foreach (var item in waitingQueue.Where(r => r.QueuePosition >= assignedPosition))
                {
                    item.QueuePosition += 1;
                }
            }
            else
            {
                assignedPosition = waitingQueue.Count + 1;
            }
        }
        else
        {
            assignedPosition = waitingQueue.Count + 1;
        }

        var reservation = new Reservation
        {
            BookId = bookId,
            UserId = currentUserId,
            QueuePosition = Math.Max(1, assignedPosition),
            Status = "Waiting",
            CreatedAt = DateTime.UtcNow
        };

        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync();

        string tierNote = isPremium
            ? $" (Đặc quyền gói Premium: Tự động ưu tiên lên vị trí #{reservation.QueuePosition})"
            : $" (Vị trí xếp hàng hiện tại: #{reservation.QueuePosition})";

        return (true, $"Đã đặt trước sách Hot \"{book.Title}\" thành công!{tierNote}", reservation.QueuePosition);
    }

    public async Task<(bool Success, string Message)> NotifyFirstInQueueAsync(int bookId)
    {
        var topItem = await _db.Reservations
            .Include(r => r.Book)
            .Include(r => r.User)
            .Where(r => r.BookId == bookId && r.Status == "Waiting")
            .OrderBy(r => r.QueuePosition)
            .FirstOrDefaultAsync();

        if (topItem == null)
        {
            return (false, "Hiện không có độc giả nào đang ở trạng thái 'Waiting' cho đầu sách này.");
        }

        var now = DateTime.UtcNow;
        topItem.Status = "Notified";
        topItem.NotifiedAt = now;
        topItem.ExpiresAt = now.AddHours(48);

        _db.Notifications.Add(new Notification
        {
            UserId = topItem.UserId,
            Title = $"[Hàng chờ S11] Đã đến lượt nhận sách \"{topItem.Book.Title}\"!",
            Content = $"Bạn đang ở vị trí #{topItem.QueuePosition}. Sách đã sẵn sàng tại kệ, vui lòng xác nhận mượn trong vòng 48 giờ (trước {topItem.ExpiresAt:dd/MM/yyyy HH:mm}).",
            Type = "QueueAvailable",
            IsRead = false,
            EmailSentViaResend = true,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        return (true, $"Đã gửi thông báo tự động cho độc giả đứng đầu hàng chờ (#{topItem.QueuePosition} - {topItem.User.FullName}) cho cuốn \"{topItem.Book.Title}\"! Giữ quyền ưu tiên trong 48 giờ.");
    }

    public async Task<(bool Success, string Message)> CancelReservationAsync(
        string currentUserId,
        int reservationId)
    {
        var res = await _db.Reservations
            .Include(r => r.Book)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (res == null)
        {
            return (false, "Không tìm thấy phiếu đặt trước cần hủy.");
        }

        int oldPos = res.QueuePosition;
        int bookId = res.BookId;
        res.Status = "Cancelled";

        // Đẩy các người phía sau lên 1 bậc
        var behind = await _db.Reservations
            .Where(r => r.BookId == bookId && (r.Status == "Waiting" || r.Status == "Notified") && r.QueuePosition > oldPos)
            .ToListAsync();

        foreach (var item in behind)
        {
            item.QueuePosition = Math.Max(1, item.QueuePosition - 1);
        }

        await _db.SaveChangesAsync();
        return (true, $"Đã hủy phiếu đặt trước sách \"{res.Book.Title}\" và tự động đôn thứ tự hàng chờ phía sau lên 1 bậc.");
    }
}
