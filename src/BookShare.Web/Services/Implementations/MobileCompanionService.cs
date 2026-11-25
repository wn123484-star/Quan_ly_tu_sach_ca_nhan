using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [CORE-MOBILE] Dịch vụ tổng hợp dữ liệu tinh gọn cho màn hình di động: thẻ độc giả, phiếu mượn hiện tại, thông báo chưa đọc và đánh dấu đã đọc ngay khi mở từ thông báo đẩy.
/// </summary>
/// <remarks>Đồng bộ trực tiếp với CSDL chính BookShare.</remarks>
public class MobileCompanionService : IMobileCompanionService
{
    private readonly BookShareDbContext _db;

    public MobileCompanionService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<MobileUnreadNotificationsResponseDto> GetUnreadNotificationsAsync(
        string currentUserId,
        int sinceId = 0)
    {
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return new MobileUnreadNotificationsResponseDto();
        }

        var baseQuery = _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == currentUserId && !n.IsRead);

        int unreadCount = await baseQuery.CountAsync();
        int latestNotificationId = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == currentUserId)
            .Select(n => (int?)n.Id)
            .MaxAsync() ?? 0;

        var latestItems = await baseQuery
            .Where(n => n.Id > sinceId)
            .OrderByDescending(n => n.Id)
            .Take(10)
            .ToListAsync();

        return new MobileUnreadNotificationsResponseDto
        {
            UnreadCount = unreadCount,
            LatestNotificationId = latestNotificationId,
            Notifications = latestItems.Select(n => new MobileNotificationItemDto
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Content,
                Type = n.Type,
                TargetUrl = ResolveSafeTargetUrl(n),
                CreatedAt = n.CreatedAt
            }).ToList()
        };
    }

    public async Task<MobileSessionStateDto> GetSessionStateAsync(
        string? currentUserId,
        string? currentUserRole,
        string? fullName)
    {
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return new MobileSessionStateDto
            {
                Authenticated = false,
                UnreadCount = 0,
                ServerVersion = "BookShare-v5.6"
            };
        }

        int unreadCount = await _db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == currentUserId && !n.IsRead);

        return new MobileSessionStateDto
        {
            Authenticated = true,
            UserId = currentUserId,
            FullName = fullName,
            Role = string.IsNullOrWhiteSpace(currentUserRole) ? "User" : currentUserRole,
            UnreadCount = unreadCount,
            ServerVersion = "BookShare-v5.6"
        };
    }

    private static string ResolveSafeTargetUrl(Notification n)
    {
        if (n.Content.Contains("/S17SandboxPayment", StringComparison.OrdinalIgnoreCase))
        {
            return $"/S17SandboxPayment?notifId={n.Id}";
        }
        if (n.Type.Contains("Dispute", StringComparison.OrdinalIgnoreCase) ||
            n.Content.Contains("tranh chấp", StringComparison.OrdinalIgnoreCase))
        {
            return $"/S19DisputeResolution?notifId={n.Id}";
        }
        if (n.Title.Contains("kết bạn", StringComparison.OrdinalIgnoreCase) ||
            n.Content.Contains("kết bạn", StringComparison.OrdinalIgnoreCase) ||
            n.Title.Contains("CLB", StringComparison.OrdinalIgnoreCase) ||
            n.Content.Contains("Câu lạc bộ", StringComparison.OrdinalIgnoreCase))
        {
            return $"/S20ExchangeHubClub?notifId={n.Id}";
        }
        if (n.Type.Contains("Loan", StringComparison.OrdinalIgnoreCase) ||
            n.Content.Contains("mượn sách", StringComparison.OrdinalIgnoreCase))
        {
            return $"/S08LibraryLoanFlow?notifId={n.Id}";
        }
        return $"/S18NotificationHub?highlight={n.Id}&notifId={n.Id}";
    }
}
