using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S05IsbnAutoCatalogController : Controller
{
    private readonly IS05IsbnCatalogService _isbnService;

    public S05IsbnAutoCatalogController(IS05IsbnCatalogService isbnService)
    {
        _isbnService = isbnService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUserRole = User.Identity?.IsAuthenticated == true
            ? (User.FindFirstValue(ClaimTypes.Role) ?? "User")
            : "Guest";
        var vm = await _isbnService.GetIsbnCatalogPageAsync(currentUserId, currentUserRole);
        return View("~/Views/Modules/S05_IsbnAutoCatalog/Index.cshtml", vm);
    }

    public class IsbnLookupQueryDto
    {
        public string Isbn { get; set; } = string.Empty;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LookupIsbn([FromBody] IsbnLookupQueryDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Isbn))
        {
            return BadRequest(new { message = "Vui lòng nhập hoặc chọn mã vạch ISBN cần tra cứu." });
        }

        var result = await _isbnService.LookupByIsbnAsync(request.Isbn);
        if (!result.Found)
        {
            return BadRequest(new { message = "Mã ISBN không hợp lệ. Vui lòng thử lại với mã ISBN-10 hoặc ISBN-13." });
        }

        return Ok(new
        {
            title = $"Tra cứu ISBN thành công ({result.Source})",
            message = $"Đã tự động điền thông tin sách: {result.Title} — {result.Author}",
            data = result
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBookAndCopy([FromBody] SaveIsbnBookAndCopyRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message, barcode, bookId) = await _isbnService.SaveBookAndCreateCopyAsync(
                currentUserId,
                currentUserRole,
                request);

            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã lưu thông tin sách & tạo bản sao vật lý";
            TempData["StatusDetail"] = message;

            return Ok(new
            {
                title = "Đã lưu thông tin sách & tạo bản sao vật lý",
                message,
                barcode,
                bookId
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize(Roles = "Librarian,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCatalogBook([FromBody] UpdateCatalogBookRequestDto request)
    {
        var (success, message) = await _isbnService.UpdateCatalogBookAsync(request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã cập nhật đầu sách trong Danh mục";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã cập nhật đầu sách", message });
    }

    [HttpPost]
    [Authorize(Roles = "Librarian,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCatalogBook([FromBody] DeleteCatalogBookRequestDto request)
    {
        var (success, message) = await _isbnService.DeleteCatalogBookAsync(request.BookId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã xóa đầu sách khỏi Danh mục";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã xóa đầu sách", message });
    }
}
