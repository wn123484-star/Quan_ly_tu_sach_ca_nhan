using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUserRole = User.Identity?.IsAuthenticated == true
            ? (User.FindFirstValue(ClaimTypes.Role) ?? "User")
            : "Guest";
        var vm = await _libraryLoanService.GetLibraryLoanFlowAsync(currentUserId, currentUserRole);
        return View("~/Views/Modules/S08_LibraryLoanFlow/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRequest([FromBody] CreateLibraryLoanRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _libraryLoanService.CreateLibraryLoanRequestAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã tạo phiếu mượn Kho chung Thư viện";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã tạo phiếu mượn Kho chung Thư viện", message });
    }

    [HttpPost]
    [Authorize(Roles = "Librarian,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveOrReject([FromBody] ApproveRejectLibraryLoanDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _libraryLoanService.ApproveOrRejectLibraryLoanAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Thủ thư đã xử lý phiếu mượn Kho chung";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã cập nhật trạng thái duyệt phiếu", message });
    }

    [HttpPost]
    [Authorize(Roles = "Librarian,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmHandover([FromBody] HandoverLibraryLoanDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _libraryLoanService.ConfirmHandoverLibraryLoanAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Xác nhận bàn giao sách tại Thư viện thành công";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã bàn giao sách Kho chung", message });
    }

    [HttpPost]
    [Authorize(Roles = "Librarian,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReturn([FromBody] ReturnLibraryLoanDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _libraryLoanService.ConfirmReturnLibraryLoanAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã thu hồi & kiểm định sách trả tại Thư viện";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã xác nhận trả sách Kho chung", message });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelRequest([FromBody] CancelLibraryLoanRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _libraryLoanService.CancelLibraryLoanRequestAsync(
                currentUserId,
                currentUserRole,
                request.LoanRequestId);

            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã hủy phiếu yêu cầu mượn sách";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã hủy phiếu yêu cầu mượn", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
