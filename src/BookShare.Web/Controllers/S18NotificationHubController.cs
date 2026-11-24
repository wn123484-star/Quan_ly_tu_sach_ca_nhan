using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S18NotificationHubController : Controller
{
    private readonly IS18NotificationHubService _notificationService;

    public S18NotificationHubController(IS18NotificationHubService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string typeFilter = "all", bool viewAll = false)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUserRole = User.Identity?.IsAuthenticated == true
            ? (User.FindFirstValue(ClaimTypes.Role) ?? "User")
            : "Guest";
        var vm = await _notificationService.GetNotificationHubPageAsync(currentUserId, currentUserRole, typeFilter, viewAll);
        return View("~/Views/Modules/S18_NotificationHub/Index.cshtml", vm);
    }

    // API cho chuông thông báo trên header
    [HttpGet]
    public async Task<IActionResult> UnreadCount()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(new { unreadCount = 0 });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var vm = await _notificationService.GetNotificationHubPageAsync(currentUserId, currentUserRole);
        // Chỉ đếm thông báo của chính người đang đăng nhập
        var myUnread = vm.Notifications.Where(n => n.UserId == currentUserId && !n.IsRead).ToList();
        return Ok(new
        {
            unreadCount = myUnread.Count,
            latestTitle = myUnread.OrderByDescending(n => n.CreatedAt).FirstOrDefault()?.Title
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead([FromBody] S18MarkNotificationRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _notificationService.MarkNotificationReadAsync(currentUserId, request.NotificationId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Đã đánh dấu thông báo đã đọc",
            message
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, updatedCount) = await _notificationService.MarkAllNotificationsReadAsync(currentUserId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã đánh dấu tất cả thông báo là đã đọc";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã đọc tất cả thông báo",
            message,
            updatedCount
        });
    }

    [HttpPost]
    [Authorize(Roles = "Librarian,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TriggerResendEmail([FromBody] S18TriggerResendCampaignDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, sentCount) = await _notificationService.TriggerResendEmailCampaignAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã gửi chiến dịch Email thông báo";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã gửi Email thông báo thành công",
            message,
            sentCount
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNotification([FromBody] S18DeleteNotificationRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _notificationService.DeleteNotificationAsync(
                currentUserId,
                currentUserRole,
                request.NotificationId);

            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã xóa thông báo";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã xóa thông báo", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
