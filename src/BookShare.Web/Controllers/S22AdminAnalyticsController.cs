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
    private readonly ISystemIconService _iconService;
    private readonly BookShareDailyJobs _dailyJobs;

    public S22AdminAnalyticsController(
        IS22AdminAnalyticsService adminService,
        ISystemIconService iconService,
        BookShareDailyJobs dailyJobs)
    {
        _adminService = adminService;
        _iconService = iconService;
        _dailyJobs = dailyJobs;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string range = "all",
        string channel = "all",
        int categoryId = 0,
        string? userKeyword = null,
        string userRole = "all",
        string userLock = "all",
        string userTier = "all",
        int userPage = 1,
        string auditCategory = "all",
        string upgradeStatus = "Pending")
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

        var filter = new S22AdminAnalyticsFilterDto
        {
            TimeRange = range,
            ChannelFilter = channel,
            CategoryId = categoryId,
            UserKeyword = userKeyword,
            UserRoleFilter = userRole,
            UserLockFilter = userLock,
            UserTierFilter = userTier,
            UserPage = userPage,
            UserPageSize = 20,
            AuditCategoryFilter = auditCategory,
            UpgradeStatusFilter = upgradeStatus
        };

        var vm = await _adminService.GetAdminAnalyticsPageAsync(currentUserId, currentUserRole, filter);
        return View("~/Views/Modules/S22_AdminAnalytics/Index.cshtml", vm);
    }

    // API số liệu cho biểu đồ phân bổ và dòng tiền (hỗ trợ bộ lọc thời gian, kênh & thể loại)
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ChartData(string range = "all", string channel = "all", int categoryId = 0)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var vm = await _adminService.GetAdminAnalyticsPageAsync(currentUserId, "Admin", new S22AdminAnalyticsFilterDto
        {
            TimeRange = range,
            ChannelFilter = channel,
            CategoryId = categoryId
        });
        return Ok(new
        {
            range = vm.TimeRange,
            channel = vm.ChannelFilter,
            categoryId = vm.CategoryId,
            totalLoans = vm.TotalLoans,
            openDisputes = vm.TotalOpenDisputes,
            aiMessages = vm.TotalAiMessages,
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
    public async Task<IActionResult> DeleteUser([FromBody] S22DeleteUserDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _adminService.DeleteUserAsync(adminUserId, request?.UserId ?? string.Empty);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã xóa tài khoản thành viên thành công";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã xóa tài khoản thành công",
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

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetIconSettings()
    {
        var items = await _iconService.GetAllIconsAsync();
        return Ok(new { items });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> UploadCustomIcon([FromForm] string iconKey, IFormFile file)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, item) = await _iconService.UploadCustomIconAsync(adminUserId, iconKey, file);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Cập nhật biểu tượng tùy chỉnh thành công",
            message,
            item
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ToggleIconMode([FromBody] ToggleIconModeDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, item) = await _iconService.ToggleIconModeAsync(
            adminUserId,
            request?.IconKey ?? string.Empty,
            request?.UseCustomImage ?? false);

        if (!success)
        {
            return BadRequest(new { message, item });
        }

        return Ok(new
        {
            title = "Đã chuyển đổi chế độ hiển thị biểu tượng",
            message,
            item
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ResetIconToDefault([FromBody] ResetIconRequestDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _iconService.ResetIconToDefaultAsync(
            adminUserId,
            request?.IconKey ?? string.Empty);

        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Khôi phục biểu tượng mặc định thành công",
            message
        });
    }
}
