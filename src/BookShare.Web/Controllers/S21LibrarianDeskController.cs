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
    [Authorize(Roles = "Librarian,Admin")]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrWhiteSpace(currentUserId) || string.IsNullOrWhiteSpace(currentUserRole))
        {
            return Forbid();
        }

        var vm = await _deskService.GetLibrarianDeskPageAsync(currentUserId, currentUserRole);
        return View("~/Views/Modules/S21_LibrarianDesk/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Librarian,Admin")]
    public async Task<IActionResult> QuickScanBarcode([FromBody] S21QuickScanBarcodeDto request)
    {
        var librarianUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(librarianUserId)) return Forbid();
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
        var librarianUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(librarianUserId)) return Forbid();
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
        var librarianUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(librarianUserId)) return Forbid();
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
