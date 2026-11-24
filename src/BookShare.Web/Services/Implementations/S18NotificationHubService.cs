using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S18NotificationHubService : IS18NotificationHubService
{
    private readonly BookShareDbContext _db;
    private readonly IResendEmailService _resendEmailService;

    public S18NotificationHubService(BookShareDbContext db, IResendEmailService resendEmailService)
    {
        _db = db;
        _resendEmailService = resendEmailService;
    }

    public async Task<S18NotificationHubPageViewModel> GetNotificationHubPageAsync(
        string currentUserId,
        string currentUserRole = "User",
        string typeFilter = "all",
        bool viewAll = false)
    {
        var user = !string.IsNullOrWhiteSpace(currentUserId)
            ? await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            : null;

        if (user == null)
        {
            return new S18NotificationHubPageViewModel
            {
                CurrentUserId = string.Empty,
                CurrentUserName = "Khách",
                TypeFilter = typeFilter
            };
        }

        bool showAllForAdmin = currentUserRole == "Admin" && viewAll;

        var baseQuery = _db.Notifications
            .AsNoTracking()
            .Include(n => n.User)
            .AsQueryable();

        if (!showAllForAdmin)
        {
            baseQuery = baseQuery.Where(n => n.UserId == user.Id);
        }

        var totalScoped = await baseQuery.CountAsync();
        var unreadScoped = await baseQuery.CountAsync(n => !n.IsRead);
        var emailSentScoped = await baseQuery.CountAsync(n => n.EmailSentViaResend);

        var filteredQuery = baseQuery;
        if (!string.IsNullOrWhiteSpace(typeFilter) && typeFilter != "all")
        {
            filteredQuery = filteredQuery.Where(n => n.Type == typeFilter);
        }

        var items = await filteredQuery
            .OrderBy(n => n.IsRead)
            .ThenByDescending(n => n.CreatedAt)
            .Select(n => new S18NotificationItemDto
            {
                Id = n.Id,
                UserId = n.UserId,
                UserFullName = n.User.FullName,
                UserEmail = n.User.Email ?? string.Empty,
                Title = n.Title,
                Content = n.Content,
                Type = n.Type,
                IsRead = n.IsRead,
                EmailSentViaResend = n.EmailSentViaResend,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();

        return new S18NotificationHubPageViewModel
        {
            CurrentUserId = user.Id,
            CurrentUserName = user.FullName,
            TotalCount = totalScoped,
            UnreadCount = unreadScoped,
            EmailSentCount = emailSentScoped,
            TypeFilter = typeFilter,
            Notifications = items
        };
    }

    public async Task<(bool Success, string Message)> MarkNotificationReadAsync(string currentUserId, int notificationId)
    {
        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId);
        if (notif == null)
        {
            return (false, "Không tìm thấy thông báo cần cập nhật.");
        }

        if (notif.UserId != currentUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền đánh dấu đã đọc thông báo của thành viên khác.");
        }

        notif.IsRead = true;
        await _db.SaveChangesAsync();

        return (true, $"Đã đánh dấu thông báo #{notif.Id} là đã đọc.");
    }

    public async Task<(bool Success, string Message, int UpdatedCount)> MarkAllNotificationsReadAsync(string currentUserId)
    {
        var unreadList = await _db.Notifications
            .Where(n => n.UserId == currentUserId && !n.IsRead)
            .ToListAsync();

        foreach (var item in unreadList)
        {
            item.IsRead = true;
        }

        await _db.SaveChangesAsync();
        return (true, $"Đã đánh dấu toàn bộ {unreadList.Count} thông báo của bạn là đã đọc!", unreadList.Count);
    }

    public async Task<(bool Success, string Message, int SentCount)> TriggerResendEmailCampaignAsync(
        string currentUserId,
        S18TriggerResendCampaignDto request)
    {
        var now = DateTime.UtcNow;
        int sentCount = 0;

        if (request.CampaignType == "DueReminder")
        {
            var activeLoans = await _db.Loans
                .Include(l => l.LoanRequest)
                    .ThenInclude(lr => lr.BookCopy)
                        .ThenInclude(bc => bc.Book)
                .Include(l => l.LoanRequest)
                    .ThenInclude(lr => lr.Borrower)
                .Where(l => l.Status == "Active" || l.Status == "Overdue")
                .ToListAsync();

            foreach (var loan in activeLoans)
            {
                var borrower = loan.LoanRequest.Borrower;
                var bookTitle = loan.LoanRequest.BookCopy.Book.Title;
                var barcode = loan.LoanRequest.BookCopy.Barcode;
                bool isOverdue = now > loan.DueDate;
                int days = (int)Math.Ceiling(Math.Abs((loan.DueDate - now).TotalDays));

                string title = isOverdue
                    ? $"Nhắc trả sách quá hạn {days} ngày: {bookTitle}"
                    : $"Nhắc hạn trả sách: {bookTitle} (Còn {days} ngày)";

                string content = isOverdue
                    ? $"Xin chào {borrower.FullName}, bản sao [{barcode}] bạn mượn đã quá hạn {days} ngày. Vui lòng hoàn trả tại thư viện hoặc điểm trạm."
                    : $"Xin chào {borrower.FullName}, bản sao [{barcode}] có hạn trả vào ngày {loan.DueDate:dd/MM/yyyy}. Chúc bạn đọc sách hiệu quả!";

                bool sentViaApi = false;
                if (!string.IsNullOrWhiteSpace(borrower.Email))
                {
                    var (sent, _, _) = await _resendEmailService.SendNotificationEmailAsync(
                        borrower.Email,
                        title,
                        $"<div style='font-family:Roboto,sans-serif;padding:16px;'><h3>{title}</h3><p>{content}</p></div>");
                    sentViaApi = sent;
                }

                _db.Notifications.Add(new Notification
                {
                    UserId = borrower.Id,
                    Title = title,
                    Content = content,
                    Type = "DueReminder",
                    IsRead = false,
                    EmailSentViaResend = sentViaApi,
                    CreatedAt = now
                });
                sentCount++;
            }
        }
        else if (request.CampaignType == "QueueAvailable")
        {
            var reservations = await _db.Reservations
                .Include(r => r.Book)
                .Include(r => r.User)
                .Where(r => r.Status == "Notified" || r.Status == "Waiting")
                .OrderBy(r => r.QueuePosition)
                .Take(5)
                .ToListAsync();

            foreach (var res in reservations)
            {
                string title = $"Sách đặt trước đã sẵn sàng: {res.Book.Title}";
                string content = $"Xin chào {res.User.FullName}, bạn đang ở vị trí #{res.QueuePosition} trong hàng chờ cuốn \"{res.Book.Title}\". Hệ thống đã giữ chỗ ưu tiên 48 giờ cho bạn.";

                bool sentViaApi = false;
                if (!string.IsNullOrWhiteSpace(res.User.Email))
                {
                    var (sent, _, _) = await _resendEmailService.SendNotificationEmailAsync(
                        res.User.Email,
                        title,
                        $"<div style='font-family:Roboto,sans-serif;padding:16px;'><h3>{title}</h3><p>{content}</p></div>");
                    sentViaApi = sent;
                }

                _db.Notifications.Add(new Notification
                {
                    UserId = res.UserId,
                    Title = title,
                    Content = content,
                    Type = "QueueAvailable",
                    IsRead = false,
                    EmailSentViaResend = sentViaApi,
                    CreatedAt = now
                });
                sentCount++;
            }
        }
        else
        {
            var targetUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == currentUserId);
            if (targetUser == null)
            {
                return (false, "Không tìm thấy tài khoản nhận thông báo.", 0);
            }

            string title = string.IsNullOrWhiteSpace(request.CustomTitle)
                ? "Thông báo từ Hệ thống BookShare"
                : request.CustomTitle.Trim();

            string content = string.IsNullOrWhiteSpace(request.CustomContent)
                ? "Hệ thống đã cập nhật tiến độ đọc sách và trạng thái mượn trả của bạn."
                : request.CustomContent.Trim();

            bool sentViaApi = false;
            if (!string.IsNullOrWhiteSpace(targetUser.Email))
            {
                var (sent, _, _) = await _resendEmailService.SendNotificationEmailAsync(
                    targetUser.Email,
                    title,
                    $"<div style='font-family:Roboto,sans-serif;padding:16px;'><h3>{title}</h3><p>{content}</p></div>");
                sentViaApi = sent;
            }

            _db.Notifications.Add(new Notification
            {
                UserId = targetUser.Id,
                Title = title,
                Content = content,
                Type = "System",
                IsRead = false,
                EmailSentViaResend = sentViaApi,
                CreatedAt = now
            });
            sentCount = 1;
        }

        await _db.SaveChangesAsync();

        return (true,
            $"Đã phát {sentCount} thông báo trong Trung tâm Thông báo thành công!",
            sentCount);
    }

    public async Task<(bool Success, string Message)> DeleteNotificationAsync(
        string currentUserId,
        string currentUserRole,
        int notificationId)
    {
        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId);
        if (notif == null)
        {
            return (false, "Không tìm thấy thông báo cần xóa.");
        }

        if (notif.UserId != currentUserId && currentUserRole != "Admin")
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xóa thông báo của người dùng khác.");
        }

        _db.Notifications.Remove(notif);
        await _db.SaveChangesAsync();

        return (true, $"Đã xóa thông báo #{notificationId} khỏi hộp thư!");
    }
        }
