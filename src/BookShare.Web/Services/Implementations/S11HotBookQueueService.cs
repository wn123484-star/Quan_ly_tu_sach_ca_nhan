using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S11HotBookQueueService : IS11HotBookQueueService
{
    private readonly BookShareDbContext _db;
    private readonly IResendEmailService _resendEmailService;
    private readonly IAntiSpamRateLimiterService _rateLimiter;

    public S11HotBookQueueService(
        BookShareDbContext db,
        IResendEmailService resendEmailService,
        IAntiSpamRateLimiterService rateLimiter)
    {
        _db = db;
        _resendEmailService = resendEmailService;
        _rateLimiter = rateLimiter;
    }

    public async Task<S11HotBookQueuePageViewModel> GetHotBookQueuePageAsync(string currentUserId)
    {
        var nowUtc = DateTime.UtcNow;
        var user = await _db.AspNetUsers
            .Include(u => u.Memberships)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        string currentTier = MembershipTierCatalog.GetEffectivePolicy(user?.Memberships, nowUtc).Tier;

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
            string uTier = MembershipTierCatalog.GetEffectivePolicy(r.User?.Memberships, nowUtc).Tier;

            return new S11QueueItemDto
            {
                ReservationId = r.Id,
                BookId = r.BookId,
                BookTitle = r.Book.Title,
                BookAuthor = r.Book.Author,
                CoverImageUrl = r.Book.CoverImageUrl,
                UserId = r.UserId,
                UserFullName = r.User?.FullName ?? "Độc giả BookShare",
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
            CurrentUserName = user?.FullName ?? "Độc giả BookShare",
            CurrentUserTier = currentTier,
            HotBooks = books,
            ActiveReservations = queueDtos
        };
    }

    public async Task<(bool Success, string Message, int QueuePosition)> EnqueueReservationAsync(
        string currentUserId,
        int bookId)
    {
        var (allowed, retrySec, rlMsg) = _rateLimiter.TryConsume(
            "s11-enqueue",
            currentUserId,
            maxRequests: 12,
            window: TimeSpan.FromMinutes(5),
            customMessage: "Bạn đang gửi yêu cầu xếp hàng đặt trước quá nhanh. Vui lòng đợi giây lát rồi thử lại.");
        if (!allowed)
        {
            throw new RateLimitExceededException(rlMsg, retrySec);
        }

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

        if (user != null && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, "Tài khoản của bạn đang bị khóa tạm thời nên không thể xếp hàng đặt trước.", 0);
        }

        bool alreadyBorrowingBook = await _db.Loans.AnyAsync(l =>
            l.LoanRequest.BorrowerId == currentUserId &&
            l.LoanRequest.BookCopy.BookId == bookId &&
            (l.Status == "Active" || l.Status == "Overdue"));
        if (alreadyBorrowingBook)
        {
            return (false, $"Bạn hiện đang mượn cuốn \"{book.Title}\" rồi, không cần xếp hàng đặt trước thêm.", 0);
        }

        var nowUtc = DateTime.UtcNow;
        var userPlan = MembershipTierCatalog.GetEffectivePolicy(user?.Memberships, nowUtc);
        int maxQueueSlots = userPlan.Tier switch
        {
            "Scholar" => 15,
            "Premium" => 10,
            "Standard" => 6,
            _ => 4
        };
        int myActiveQueues = await _db.Reservations.CountAsync(r =>
            r.UserId == currentUserId && (r.Status == "Waiting" || r.Status == "Notified"));
        if (myActiveQueues >= maxQueueSlots)
        {
            return (false, $"Bạn đã đạt giới hạn tối đa {maxQueueSlots} lượt đặt trước Sách Hot đồng thời của gói {userPlan.DisplayName}. Vui lòng hủy bớt lượt cũ hoặc nâng cấp gói hội viên!", 0);
        }
        bool isPriorityTier = userPlan.PriorityRank > 0;

        var waitingQueue = await _db.Reservations
            .Include(r => r.User)
                .ThenInclude(u => u.Memberships)
            .Where(r => r.BookId == bookId && (r.Status == "Waiting" || r.Status == "Notified"))
            .OrderBy(r => r.QueuePosition)
            .ToListAsync();

        int assignedPosition;

        if (isPriorityTier)
        {
            // Xếp ưu tiên theo bậc gói hội viên: Scholar (3) > Premium (2) > Standard (1) > Free (0)
            var firstLowerTier = waitingQueue.FirstOrDefault(r =>
                r.Status == "Waiting" &&
                MembershipTierCatalog.GetEffectivePolicy(r.User?.Memberships, nowUtc).PriorityRank < userPlan.PriorityRank);

            if (firstLowerTier != null)
            {
                assignedPosition = firstLowerTier.QueuePosition;
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

        string tierNote = isPriorityTier
            ? $" (Đặc quyền gói {userPlan.DisplayName}: Tự động ưu tiên lên vị trí #{reservation.QueuePosition})"
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

        string title = $"[Hàng chờ Sách Hot] Đã đến lượt nhận sách \"{topItem.Book.Title}\"!";
        string content = $"Bạn đang ở vị trí #{topItem.QueuePosition}. Sách đã sẵn sàng tại kệ, vui lòng xác nhận mượn trong vòng 48 giờ (trước {topItem.ExpiresAt:dd/MM/yyyy HH:mm}).";

        bool sentViaApi = false;
        if (!string.IsNullOrWhiteSpace(topItem.User.Email))
        {
            var (sent, _, _) = await _resendEmailService.SendNotificationEmailAsync(
                topItem.User.Email,
                title,
                $"<div style='font-family:Roboto,sans-serif;padding:16px;'><h3>{title}</h3><p>{content}</p></div>");
            sentViaApi = sent;
        }

        _db.Notifications.Add(new Notification
        {
            UserId = topItem.UserId,
            Title = title,
            Content = content,
            Type = "QueueAvailable",
            IsRead = false,
            EmailSentViaResend = sentViaApi,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        return (true, $"Đã gửi thông báo cho độc giả đứng đầu hàng chờ (#{topItem.QueuePosition} - {topItem.User.FullName}) cho cuốn \"{topItem.Book.Title}\"! Giữ quyền ưu tiên trong 48 giờ.");
    }

    public async Task<(bool Success, string Message)> CancelReservationAsync(
        string currentUserId,
        string currentUserRole,
        int reservationId)
    {
        var res = await _db.Reservations
            .Include(r => r.Book)
            .FirstOrDefaultAsync(r => r.Id == reservationId);

        if (res == null)
        {
            return (false, "Không tìm thấy phiếu đặt trước cần hủy.");
        }

        bool isPrivileged = currentUserRole is "Librarian" or "Admin";
        if (res.UserId != currentUserId && !isPrivileged)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền hủy lượt đặt trước của người dùng khác.");
        }

        if (res.Status != "Waiting" && res.Status != "Notified")
        {
            return (false, $"Phiếu đặt trước #{res.Id} đã ở trạng thái '{res.Status}', không thể hủy lặp lại.");
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
