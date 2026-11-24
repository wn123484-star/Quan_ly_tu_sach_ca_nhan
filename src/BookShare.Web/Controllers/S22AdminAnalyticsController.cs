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
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUserRole = User.Identity?.IsAuthenticated == true
            ? (User.FindFirstValue(ClaimTypes.Role) ?? "User")
            : "Guest";

        if (User.Identity?.IsAuthenticated == true && currentUserRole != "Admin")
        {
            return Forbid();
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            TempData["StatusCode"] = 403;
            TempData["StatusTitle"] = "Khu vực dành riêng cho Quản trị viên";
            TempData["StatusDetail"] = "Tài khoản hiện tại không có quyền truy cập Trung tâm Quản trị Hệ thống. Vui lòng đăng nhập bằng tài khoản Quản trị viên.";
        }

        var vm = await _adminService.GetAdminAnalyticsPageAsync(currentUserId, currentUserRole);
        return View("~/Views/Modules/S22_AdminAnalytics/Index.cshtml", vm);
    }

    // API số liệu cho biểu đồ phân bổ và dòng tiền
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ChartData()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
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
    public async Task<IActionResult> ReviewRoleUpgrade([FromBody] ReviewRoleUpgradeRequestDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _adminService.ReviewRoleUpgradeAsync(adminUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã xử lý đề nghị nâng quyền Chủ nhiệm CLB";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã xử lý đề nghị nâng quyền", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ChangeUserRole([FromBody] S22ChangeUserRoleDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _adminService.ChangeUserRoleAsync(adminUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã cập nhật phân quyền tài khoản thành công";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Cập nhật phân quyền tài khoản thành công",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ToggleUserLock([FromBody] S22ToggleUserLockDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _adminService.ToggleUserLockoutAsync(adminUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã cập nhật trạng thái khóa tài khoản";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã cập nhật trạng thái tài khoản",
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
        TempData["StatusTitle"] = "Đã thực thi tác vụ tự động thành công";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã thực thi tác vụ tự động thành công",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ManageSampleData([FromBody] S22ManageSampleDataDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _adminService.ManageSampleDataAsync(adminUserId, request.ActionMode);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã thực thi quản trị dữ liệu hệ thống";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Cập nhật dữ liệu hệ thống thành công",
            message
        });
    }
}
