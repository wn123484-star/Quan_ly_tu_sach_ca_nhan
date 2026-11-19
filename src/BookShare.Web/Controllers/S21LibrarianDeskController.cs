using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S21LibrarianDeskController : Controller
{
    private readonly IS21LibrarianDeskService _deskService;

    public S21LibrarianDeskController(IS21LibrarianDeskService deskService)
    {
        _deskService = deskService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-librarian-01";
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";

        if (User.Identity?.IsAuthenticated != true || (currentUserRole != "Librarian" && currentUserRole != "Admin"))
        {
            var displayRole = User.Identity?.IsAuthenticated == true ? currentUserRole : "Khách (Chưa đăng nhập)";
            TempData["StatusCode"] = 403;
            TempData["StatusTitle"] = "MÃ 403 FORBIDDEN: Phân khu Quầy Nghiệp Vụ Thủ Thư (Librarian & Admin)";
            TempData["StatusDetail"] = $"Tài khoản hiện tại có vai trò [{displayRole}]. Mọi thao tác nghiệp vụ tại Quầy Thủ thư đều được khóa chặt bằng [Authorize(Roles = \"Librarian,Admin\")]. Vui lòng đăng nhập tài khoản thuthu@cmc.edu.vn tại Cổng Xác Thực.";
        }

        var vm = await _deskService.GetLibrarianDeskPageAsync(currentUserId, currentUserRole);
        return View("~/Views/Modules/S21_LibrarianDesk/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Librarian,Admin")]
    public async Task<IActionResult> QuickScanBarcode([FromBody] S21QuickScanBarcodeDto request)
    {
        var librarianUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-librarian-01";
        var (success, message, actionPerformed) = await _deskService.ProcessQuickBarcodeScanAsync(librarianUserId, request);
        if (!success)
        {
            var code = message.Contains("MÃ 404") ? 404 : 400;
            return StatusCode(code, new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Thủ thư đã quét Barcode xử lý tại quầy";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã quét Barcode xử lý nhanh tại quầy",
            message,
            actionPerformed
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Librarian,Admin")]
    public async Task<IActionResult> BatchImportCopies([FromBody] S21BatchImportCopiesDto request)
    {
        var librarianUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-librarian-01";
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
        var librarianUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-librarian-01";
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
        var librarianUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-librarian-01";
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
