using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S08LibraryLoanFlowController : Controller
{
    private readonly IS08LibraryLoanService _libraryLoanService;
    private readonly IRecaptchaService _recaptchaService;

    public S08LibraryLoanFlowController(
        IS08LibraryLoanService libraryLoanService,
        IRecaptchaService recaptchaService)
    {
        _libraryLoanService = libraryLoanService;
        _recaptchaService = recaptchaService;
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
        // [BS-SEC-RECAPTCHA] Bảo vệ tạo phiếu thuê/mượn Kho chung bằng Google reCAPTCHA v3
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (captchaOk, _, captchaMsg) = await _recaptchaService.VerifyTokenAsync(request.RecaptchaToken, "library_loan", remoteIp);
        if (!captchaOk)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = captchaMsg });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveOrReject([FromBody] ApproveRejectLibraryLoanDto request)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (User.Identity?.IsAuthenticated != true || (role != "Librarian" && role != "Admin"))
        {
            var displayRole = string.IsNullOrWhiteSpace(role) ? "Khách (Chưa đăng nhập)" : role;
            return StatusCode(403, new
            {
                message = $"MÃ 403 FORBIDDEN: Tài khoản vai trò [{displayRole}] không được phép duyệt phiếu mượn Kho chung! Vui lòng đăng nhập tài khoản Thủ thư (thuthu@cmc.edu.vn) hoặc Admin."
            });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-lib-01";
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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmHandover([FromBody] HandoverLibraryLoanDto request)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (User.Identity?.IsAuthenticated != true || (role != "Librarian" && role != "Admin"))
        {
            var displayRole = string.IsNullOrWhiteSpace(role) ? "Khách (Chưa đăng nhập)" : role;
            return StatusCode(403, new
            {
                message = $"MÃ 403 FORBIDDEN: Tài khoản vai trò [{displayRole}] không có quyền xuất kho bàn giao sách Thư viện! Chỉ Thủ thư (Librarian) hoặc Admin mới được phép."
            });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-lib-01";
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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReturn([FromBody] ReturnLibraryLoanDto request)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (User.Identity?.IsAuthenticated != true || (role != "Librarian" && role != "Admin"))
        {
            var displayRole = string.IsNullOrWhiteSpace(role) ? "Khách (Chưa đăng nhập)" : role;
            return StatusCode(403, new
            {
                message = $"MÃ 403 FORBIDDEN: Tài khoản vai trò [{displayRole}] không có quyền xác nhận thu hồi sách kho Thư viện! Chỉ Thủ thư (Librarian) hoặc Admin mới được phép."
            });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-lib-01";
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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelRequest([FromBody] CancelLibraryLoanRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
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
