using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [S18] Dịch vụ quản lý hộp thư thông báo, đánh dấu đã đọc đơn lẻ/hàng loạt, đồng bộ xác minh OTP Email 6 số với Hồ sơ S01 và phát hành Email thông báo qua Resend.
/// </summary>
/// <remarks>Bảo mật: Giới hạn tần suất gửi OTP và khóa tạm khi nhập sai 5 lần.</remarks>
public class S18NotificationHubService : IS18NotificationHubService
{
    private readonly BookShareDbContext _db;
    private readonly IResendEmailService _resendEmailService;
    private readonly IAntiSpamRateLimiterService _rateLimiter;

    public S18NotificationHubService(
        BookShareDbContext db,
        IResendEmailService resendEmailService,
        IAntiSpamRateLimiterService rateLimiter)
    {
        _db = db;
        _resendEmailService = resendEmailService;
        _rateLimiter = rateLimiter;
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

        foreach (var item in items)
        {
            item.DetailUrl = ResolveNotificationDetailUrl(item.Id, item.Type, item.Title, item.Content);
        }

        return new S18NotificationHubPageViewModel
        {
            CurrentUserId = user.Id,
            CurrentUserName = user.FullName,
            CurrentUserEmail = user.Email ?? string.Empty,
            EmailConfirmed = user.EmailConfirmed,
            TotalCount = totalScoped,
            UnreadCount = unreadScoped,
            EmailSentCount = emailSentScoped,
            TypeFilter = typeFilter,
            Notifications = items
        };
    }

    public async Task<S18RecentPopupResultDto> GetRecentPopupNotificationsAsync(string currentUserId, int take = 8)
    {
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return new S18RecentPopupResultDto();
        }

        int safeTake = Math.Clamp(take, 1, 20);
        var query = _db.Notifications
            .AsNoTracking()
            .Include(n => n.User)
            .Where(n => n.UserId == currentUserId);

        int totalCount = await query.CountAsync();
        int unreadCount = await query.CountAsync(n => !n.IsRead);

        var items = await query
            .OrderBy(n => n.IsRead)
            .ThenByDescending(n => n.CreatedAt)
            .Take(safeTake)
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

        foreach (var item in items)
        {
            item.DetailUrl = ResolveNotificationDetailUrl(item.Id, item.Type, item.Title, item.Content);
        }

        return new S18RecentPopupResultDto
        {
            UnreadCount = unreadCount,
            TotalCount = totalCount,
            Items = items
        };
    }

    private static string ResolveNotificationDetailUrl(int notifId, string? type, string? title, string? content)
    {
        string combined = $"{title} {content}".ToLowerInvariant();
        if (string.Equals(type, "DueReminder", StringComparison.OrdinalIgnoreCase))
        {
            return "/S12LoanRenewalFine";
        }
        if (string.Equals(type, "QueueAvailable", StringComparison.OrdinalIgnoreCase))
        {
            return "/S11HotBookQueue";
        }
        if (string.Equals(type, "Dispute", StringComparison.OrdinalIgnoreCase))
        {
            return "/S19DisputeResolution";
        }
        if (string.Equals(type, "LoanApproved", StringComparison.OrdinalIgnoreCase))
        {
            return combined.Contains("p2p") ? "/S09P2PLoanFlow" : "/S08LibraryLoanFlow";
        }
        if (combined.Contains("gói") || combined.Contains("hội viên") || combined.Contains("premium") || combined.Contains("scholar"))
        {
            return "/S16MembershipTier";
        }
        if (combined.Contains("ví tiền ảo") || combined.Contains("hóa đơn") || combined.Contains("#tx-") || combined.Contains("cọc"))
        {
            return "/S17SandboxPayment";
        }
        if (combined.Contains("chủ nhiệm clb") || combined.Contains("nâng quyền"))
        {
            return "/S22AdminAnalytics";
        }
        if (combined.Contains("kết bạn") || combined.Contains("câu lạc bộ") || combined.Contains("clb"))
        {
            return "/S20ExchangeHubClub";
        }
        return $"/S18NotificationHub?highlight={notifId}";
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
        var (allowed, retrySec, rlMsg) = _rateLimiter.TryConsume(
            "s18-email-campaign",
            currentUserId,
            maxRequests: 8,
            window: TimeSpan.FromMinutes(5),
            customMessage: "Bạn đã kích hoạt quá nhiều chiến dịch gửi Email trong 5 phút (tối đa 8 lần). Vui lòng đợi giây lát để chống spam hộp thư độc giả.");
        if (!allowed)
        {
            throw new RateLimitExceededException(rlMsg, retrySec);
        }

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

            if (!AuthConfigHelper.IsValidEmailFormat(targetUser.Email))
            {
                return (false, "Tài khoản của bạn chưa có địa chỉ Email hợp lệ. Vui lòng vào Cài Đặt Tài Khoản điền Email trước khi gửi thư!", 0);
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
