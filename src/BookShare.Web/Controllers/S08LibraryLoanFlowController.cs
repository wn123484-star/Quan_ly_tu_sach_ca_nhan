using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S08LibraryLoanFlowController : Controller
{
    private readonly IS08LibraryLoanService _libraryLoanService;

    public S08LibraryLoanFlowController(IS08LibraryLoanService libraryLoanService)
    {
        _libraryLoanService = libraryLoanService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var vm = await _libraryLoanService.GetLibraryLoanFlowAsync(currentUserId, currentUserRole);
        return View("~/Views/Modules/S08_LibraryLoanFlow/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRequest([FromBody] CreateLibraryLoanRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _libraryLoanService.CreateLibraryLoanRequestAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã tạo phiếu mượn Kho chung (S08)";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã tạo phiếu mượn Kho chung (S08)", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveOrReject([FromBody] ApproveRejectLibraryLoanDto request)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (User.Identity?.IsAuthenticated == true && role != "Librarian" && role != "Admin")
        {
            return StatusCode(403, new
            {
                message = $"MÃ 403 FORBIDDEN: Tài khoản vai trò [{role}] không được phép duyệt phiếu mượn Kho chung! Vui lòng đăng nhập tài khoản Thủ thư (thuthu@cmc.edu.vn) hoặc Admin tại S01."
            });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-lib-01";
        var (success, message) = await _libraryLoanService.ApproveOrRejectLibraryLoanAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Thủ thư đã xử lý phiếu mượn Kho chung (S08)";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã cập nhật trạng thái duyệt phiếu (S08)", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmHandover([FromBody] HandoverLibraryLoanDto request)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (User.Identity?.IsAuthenticated == true && role != "Librarian" && role != "Admin")
        {
            return StatusCode(403, new
            {
                message = $"MÃ 403 FORBIDDEN: Tài khoản vai trò [{role}] không có quyền xuất kho bàn giao sách Thư viện! Chỉ Thủ thư (Librarian) hoặc Admin mới được phép."
            });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-lib-01";
        var (success, message) = await _libraryLoanService.ConfirmHandoverLibraryLoanAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Xác nhận nhận sách tại Thư viện thành công (S08)";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã bàn giao sách Kho chung (S08)", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReturn([FromBody] ReturnLibraryLoanDto request)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (User.Identity?.IsAuthenticated == true && role != "Librarian" && role != "Admin")
        {
            return StatusCode(403, new
            {
                message = $"MÃ 403 FORBIDDEN: Tài khoản vai trò [{role}] không có quyền xác nhận thu hồi sách kho Thư viện! Chỉ Thủ thư (Librarian) hoặc Admin mới được phép."
            });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-lib-01";
        var (success, message) = await _libraryLoanService.ConfirmReturnLibraryLoanAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã thu hồi & kiểm định sách trả tại Thư viện (S08)";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã xác nhận trả sách Kho chung (S08)", message });
    }
}
