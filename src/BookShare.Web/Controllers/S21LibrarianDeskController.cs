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

        if (User.Identity?.IsAuthenticated == true && currentUserRole != "Librarian" && currentUserRole != "Admin")
        {
            TempData["StatusCode"] = 403;
            TempData["StatusTitle"] = "MÃ 403 FORBIDDEN: Phân khu Bàn làm việc Thủ thư (Librarian & Admin)";
            TempData["StatusDetail"] = $"Tài khoản hiện tại có vai trò [{currentUserRole}]. Mọi thao tác nghiệp vụ tại S21 đều được khóa chặt bằng [Authorize(Roles = \"Librarian,Admin\")]. Vui lòng chuyển sang tài khoản thuthu@cmc.edu.vn tại S01.";
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
        TempData["StatusTitle"] = "Thủ thư đã quét Barcode xử lý tại quầy (S21)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã quét Barcode xử lý nhanh tại quầy (S21)",
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
        TempData["StatusTitle"] = "Thủ thư đã nhập lô bản sao mới vào kho chung (S21)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Nhập lô bản sao mới thành công (S21)",
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
        TempData["StatusTitle"] = "Đã cập nhật trạng thái bản sao kho chung (S21)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã cập nhật trạng thái bản sao (S21)",
            message
        });
    }
}
