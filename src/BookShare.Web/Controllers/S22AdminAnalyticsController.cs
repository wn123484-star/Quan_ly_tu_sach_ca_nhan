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
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-admin-01";
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";

        if (User.Identity?.IsAuthenticated == true && currentUserRole != "Admin")
        {
            TempData["StatusCode"] = 403;
            TempData["StatusTitle"] = "MÃ 403 FORBIDDEN: Phân khu Quản trị Hệ thống (Chỉ dành cho Admin)";
            TempData["StatusDetail"] = $"Tài khoản hiện tại có vai trò [{currentUserRole}]. Toàn bộ API cấp quyền, khóa tài khoản và chạy Background Jobs tại S22 đều bị khóa bởi [Authorize(Roles = \"Admin\")]. Vui lòng đăng nhập admin@bookshare.vn tại S01.";
        }

        var vm = await _adminService.GetAdminAnalyticsPageAsync(currentUserId, currentUserRole);
        return View("~/Views/Modules/S22_AdminAnalytics/Index.cshtml", vm);
    }

    // API số liệu cho 2 biểu đồ Chart.js (Phân bổ đơn mượn/bản sao & Doanh thu Sandbox)
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ChartData()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-admin-01";
        var vm = await _adminService.GetAdminAnalyticsPageAsync(currentUserId, "Admin");
        return Ok(new
        {
            libraryLoans = vm.LibraryLoansCount,
            p2pLoans = vm.P2PLoansCount,
            availableCopies = vm.AvailableCopiesCount,
            borrowedCopies = vm.BorrowedCopiesCount,
            reservedCopies = vm.ReservedCopiesCount,
            damagedCopies = vm.DamagedOrLostCopiesCount,
            membershipRevenue = vm.TotalMembershipRevenue,
            fineRevenue = vm.TotalFineRevenue,
            simulatedDeposits = vm.TotalSimulatedDeposits
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ChangeUserRole([FromBody] S22ChangeUserRoleDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-admin-01";
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
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-admin-01";
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
