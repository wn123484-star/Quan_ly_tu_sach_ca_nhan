using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

/// <summary>
/// [S21] Controller Quầy Nghiệp vụ Thủ thư hợp nhất: Quét mã vạch tự động (Mượn <-> Trả), Cho mượn trực tiếp tại quầy, Xử lý mọi phiếu mượn Kho chung & P2P, Nhập kho hàng loạt.
/// </summary>
/// <remarks>RBAC: Chỉ Thủ thư (Librarian) và Quản trị viên (Admin) được truy cập vận hành và gọi các thao tác kho/cho mượn.</remarks>
public class S21LibrarianDeskController : Controller
{
    private readonly IS21LibrarianDeskService _deskService;

    public S21LibrarianDeskController(IS21LibrarianDeskService deskService)
    {
        _deskService = deskService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? loanKeyword = null,
        string loanChannel = "all",
        string loanStatus = "all",
        int loanPage = 1,
        string? copyKeyword = null,
        string copyStatus = "all",
        int? copyLibraryId = null,
        int copyPage = 1)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUserRole = User.Identity?.IsAuthenticated == true
            ? (User.FindFirstValue(ClaimTypes.Role) ?? "User")
            : "Guest";

        if (User.Identity?.IsAuthenticated != true || (currentUserRole != "Librarian" && currentUserRole != "Admin"))
        {
            TempData["StatusCode"] = 403;
            TempData["StatusTitle"] = "Khu vực dành riêng cho Thủ thư & Quản trị viên";
            TempData["StatusDetail"] = "Tài khoản hiện tại không có quyền thao tác tại Quầy nghiệp vụ Thủ thư. Vui lòng đăng nhập bằng tài khoản Thủ thư hoặc Quản trị viên.";
        }

        var vm = await _deskService.GetLibrarianDeskPageAsync(
            currentUserId,
            currentUserRole,
            loanKeyword,
            loanChannel,
            loanStatus,
            loanPage,
            copyKeyword,
            copyStatus,
            copyLibraryId,
            copyPage);
        return View("~/Views/Modules/S21_LibrarianDesk/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Librarian,Admin")]
    public async Task<IActionResult> ProcessLoanAction([FromBody] S21ProcessLoanActionDto request)
    {
        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var staffUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "Librarian";
        try
        {
            var (success, message) = await _deskService.ProcessUnifiedLoanActionAsync(staffUserId, staffUserRole, request);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã điều phối phiếu mượn tại Quầy Thủ thư";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Xử lý phiếu mượn thành công", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Librarian,Admin")]
    public async Task<IActionResult> DirectLendBook([FromBody] S21DirectLendBookDto request)
    {
        var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var staffUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "Librarian";
        try
        {
            var (success, message) = await _deskService.DirectLendBookAtDeskAsync(staffUserId, staffUserRole, request);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã cho mượn sách trực tiếp tại Quầy Thủ thư";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Cho mượn sách thành công", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Librarian,Admin")]
    public async Task<IActionResult> QuickScanBarcode([FromBody] S21QuickScanBarcodeDto request)
    {
        var librarianUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, actionPerformed) = await _deskService.ProcessQuickBarcodeScanAsync(librarianUserId, request);
        if (!success)
        {
            var code = message.Contains("MÃ 404") ? 404 : 400;
            return StatusCode(code, new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Thủ thư đã quét mã vạch xử lý tại quầy";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã xử lý mã vạch tại quầy",
            message,
            actionPerformed
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Librarian,Admin")]
    public async Task<IActionResult> BatchImportCopies([FromBody] S21BatchImportCopiesDto request)
    {
        var librarianUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, barcodes) = await _deskService.BatchImportSystemCopiesAsync(librarianUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Thủ thư đã nhập lô bản sao mới vào kho chung";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Nhập lô bản sao mới thành công",
            message,
            barcodes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Librarian,Admin")]
    public async Task<IActionResult> UpdateCopyState([FromBody] S21UpdateCopyStateDto request)
    {
        var librarianUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _deskService.UpdateCopyDamageOrLostAsync(librarianUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã cập nhật trạng thái bản sao kho chung";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã cập nhật trạng thái bản sao",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Librarian,Admin")]
    public async Task<IActionResult> DeleteSystemCopy([FromBody] S21DeleteSystemCopyDto request)
    {
        var librarianUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _deskService.DeleteSystemCopyAsync(librarianUserId, request.BookCopyId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã thanh lý/xóa bản sao sách kho chung";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã xóa bản sao kho chung", message });
    }
}
