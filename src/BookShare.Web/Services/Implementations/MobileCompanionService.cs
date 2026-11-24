using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

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

        var latestItems = await baseQuery
            .Where(n => n.Id > sinceId)
            .OrderByDescending(n => n.Id)
            .Take(10)
            .ToListAsync();

        return new MobileUnreadNotificationsResponseDto
        {
            UnreadCount = unreadCount,
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
                ServerVersion = "BookShare-Sprint3"
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
            ServerVersion = "BookShare-Sprint3"
        };
    }

    private static string ResolveSafeTargetUrl(Notification n)
    {
        if (n.Content.Contains("/S17SandboxPayment", StringComparison.OrdinalIgnoreCase))
        {
            return "/S17SandboxPayment";
        }
        if (n.Type.Contains("Dispute", StringComparison.OrdinalIgnoreCase) ||
            n.Content.Contains("tranh chấp", StringComparison.OrdinalIgnoreCase))
        {
            return "/S19DisputeResolution";
        }
        if (n.Type.Contains("Loan", StringComparison.OrdinalIgnoreCase) ||
            n.Content.Contains("mượn sách", StringComparison.OrdinalIgnoreCase))
        {
            return "/S08LibraryLoanFlow";
        }
        return "/S18NotificationHub";
    }
}
