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
        string typeFilter = "all")
    {
        var user = await _db.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId)
            ?? await _db.AspNetUsers.AsNoTracking().FirstAsync(u => u.Id == "u-user-01");

        var query = _db.Notifications
            .AsNoTracking()
            .Include(n => n.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(typeFilter) && typeFilter != "all")
        {
            query = query.Where(n => n.Type == typeFilter);
        }

        var items = await query
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

        var totalAll = await _db.Notifications.CountAsync();
        var unreadAll = await _db.Notifications.CountAsync(n => !n.IsRead);
        var emailSentAll = await _db.Notifications.CountAsync(n => n.EmailSentViaResend);

        return new S18NotificationHubPageViewModel
        {
            CurrentUserId = user.Id,
            CurrentUserName = user.FullName,
            TotalCount = totalAll,
            UnreadCount = unreadAll,
            EmailSentCount = emailSentAll,
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

        notif.IsRead = true;
        await _db.SaveChangesAsync();

        return (true, $"Đã đánh dấu thông báo #{notif.Id} (\"{notif.Title}\") là Đã đọc.");
    }

    public async Task<(bool Success, string Message, int UpdatedCount)> MarkAllNotificationsReadAsync(string currentUserId)
    {
        var unreadList = await _db.Notifications
            .Where(n => !n.IsRead)
            .ToListAsync();

        foreach (var item in unreadList)
        {
            item.IsRead = true;
        }

        await _db.SaveChangesAsync();
        return (true, $"Đã đánh dấu toàn bộ {unreadList.Count} thông báo trong trung tâm là Đã đọc!", unreadList.Count);
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
                    ? $"[Resend Email] Nhắc trả sách quá hạn {days} ngày: {bookTitle}"
                    : $"[Resend Email] Nhắc hạn trả sách: {bookTitle} (Còn {days} ngày)";

                string content = isOverdue
                    ? $"Xin chào {borrower.FullName}, bản sao [{barcode}] bạn mượn đã quá hạn {days} ngày. Vui lòng hoàn trả tại Thư viện/Hub để tránh phát sinh thêm phí trễ hạn."
                    : $"Xin chào {borrower.FullName}, bản sao [{barcode}] có hạn trả vào ngày {loan.DueDate:dd/MM/yyyy}. Chúc bạn đọc sách hiệu quả!";

                if (!string.IsNullOrWhiteSpace(borrower.Email))
                {
                    await _resendEmailService.SendNotificationEmailAsync(
                        borrower.Email,
                        title,
                        $"<div style='font-family:Roboto,sans-serif;padding:16px;'><h3>{title}</h3><p>{content}</p></div>");
                }

                _db.Notifications.Add(new Notification
                {
                    UserId = borrower.Id,
                    Title = title,
                    Content = content,
                    Type = "DueReminder",
                    IsRead = false,
                    EmailSentViaResend = true,
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
                string title = $"[Resend Email] Sách Hot đặt trước S11 đã sẵn sàng: {res.Book.Title}";
                string content = $"Xin chào {res.User.FullName}, bạn đang ở vị trí #{res.QueuePosition} trong hàng chờ cuốn \"{res.Book.Title}\". Hệ thống đã giữ chỗ ưu tiên 48 giờ cho bạn tại Kho chung ĐH CMC!";

                if (!string.IsNullOrWhiteSpace(res.User.Email))
                {
                    await _resendEmailService.SendNotificationEmailAsync(
                        res.User.Email,
                        title,
                        $"<div style='font-family:Roboto,sans-serif;padding:16px;'><h3>{title}</h3><p>{content}</p></div>");
                }

                _db.Notifications.Add(new Notification
                {
                    UserId = res.UserId,
                    Title = title,
                    Content = content,
                    Type = "QueueAvailable",
                    IsRead = false,
                    EmailSentViaResend = true,
                    CreatedAt = now
                });
                sentCount++;
            }
        }
        else
        {
            var targetUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == currentUserId)
                             ?? await _db.AspNetUsers.FirstAsync(u => u.Id == "u-user-01");

            string title = string.IsNullOrWhiteSpace(request.CustomTitle)
                ? "[Resend Email] Thông báo từ Hệ thống BookShare Portal"
                : request.CustomTitle.Trim();

            string content = string.IsNullOrWhiteSpace(request.CustomContent)
                ? "Hệ thống đã đồng bộ tiến độ đọc S03 và kiểm tra an toàn hạn mức mượn S16 của bạn."
                : request.CustomContent.Trim();

            if (!string.IsNullOrWhiteSpace(targetUser.Email))
            {
                await _resendEmailService.SendNotificationEmailAsync(
                    targetUser.Email,
                    title,
                    $"<div style='font-family:Roboto,sans-serif;padding:16px;'><h3>{title}</h3><p>{content}</p></div>");
            }

            _db.Notifications.Add(new Notification
            {
                UserId = targetUser.Id,
                Title = title,
                Content = content,
                Type = "System",
                IsRead = false,
                EmailSentViaResend = true,
                CreatedAt = now
            });
            sentCount = 1;
        }

        await _db.SaveChangesAsync();

        return (true,
            $"Đã kích hoạt IResendEmailService thành công: Gửi {sentCount} Email thông báo và lưu lịch sử vào bảng Notifications (EmailSentViaResend = 1)!",
            sentCount);
    }
}
