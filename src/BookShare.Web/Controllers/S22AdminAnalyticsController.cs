using System.Security.Claims;
using BookShare.Web.BackgroundJobs;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S22AdminAnalyticsController : Controller
{
    private readonly IS22AdminAnalyticsService _adminService;
    private readonly BookShareDailyJobs _dailyJobs;

    public S22AdminAnalyticsController(
        IS22AdminAnalyticsService adminService,
        BookShareDailyJobs dailyJobs)
    {
        _adminService = adminService;
        _dailyJobs = dailyJobs;
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(currentUserId) ||
            !string.Equals(User.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.Ordinal))
        {
            return Forbid();
        }

        var vm = await _adminService.GetAdminAnalyticsPageAsync(currentUserId, "Admin");
        return View("~/Views/Modules/S22_AdminAnalytics/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ChangeUserRole([FromBody] S22ChangeUserRoleDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(adminUserId)) return Forbid();
        var (success, message) = await _adminService.ChangeUserRoleAsync(adminUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Admin đã cập nhật phân quyền Role (S22)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Cập nhật phân quyền Role thành công (S22)",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ToggleUserLock([FromBody] S22ToggleUserLockDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(adminUserId)) return Forbid();
        var (success, message) = await _adminService.ToggleUserLockoutAsync(adminUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Admin đã cập nhật trạng thái Khóa/Mở khóa tài khoản (S22)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã cập nhật trạng thái tài khoản (S22)",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TriggerDailyJob([FromBody] S22TriggerJobRequestDto request)
    {
        var (success, message) = await _dailyJobs.ExecuteJobByKeyAsync(request.JobKey);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã thực thi thành công Background Job (S22)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã thực thi Background Job thành công (S22)",
            message
        });
    }
}
