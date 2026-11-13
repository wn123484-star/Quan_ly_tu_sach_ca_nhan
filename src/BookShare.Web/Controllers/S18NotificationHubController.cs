using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
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
    public async Task<IActionResult> Index(string typeFilter = "all")
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _notificationService.GetNotificationHubPageAsync(currentUserId, typeFilter);
        return View("~/Views/Modules/S18_NotificationHub/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead([FromBody] S18MarkNotificationRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _notificationService.MarkNotificationReadAsync(currentUserId, request.NotificationId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Đã đánh dấu thông báo đã đọc (S18)",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, updatedCount) = await _notificationService.MarkAllNotificationsReadAsync(currentUserId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã đánh dấu tất cả thông báo là đã đọc (S18)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã đọc tất cả thông báo (S18)",
            message,
            updatedCount
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TriggerResendEmail([FromBody] S18TriggerResendCampaignDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, sentCount) = await _notificationService.TriggerResendEmailCampaignAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã gửi chiến dịch Email qua IResendEmailService (S18)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã gửi Email qua Resend API (S18)",
            message,
            sentCount
        });
    }
}
