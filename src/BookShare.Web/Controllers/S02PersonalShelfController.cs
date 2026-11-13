using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S02PersonalShelfController : Controller
{
    private readonly IS02PersonalShelfService _shelfService;

    public S02PersonalShelfController(IS02PersonalShelfService shelfService)
    {
        _shelfService = shelfService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? shelfId = null)
    {
        // Mặc định hiển thị tài khoản mẫu u-user-01 (Nguyễn Hùng) nếu người dùng mở xem nhanh chưa đăng nhập
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _shelfService.GetPersonalShelfAsync(currentUserId, shelfId);
        return View("~/Views/Modules/S02_PersonalShelf/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLendable([FromBody] ToggleLendableRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        try
        {
            var (success, message, lendableCount) = await _shelfService.ToggleLendableAsync(
                currentUserId,
                request.BookCopyId,
                request.IsLendable);

            if (!success)
            {
                return BadRequest(new { message });
            }

            return Ok(new
            {
                title = "Đã cập nhật trạng thái cho mượn P2P (S02)",
                message,
                lendableCount
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateShelf([FromBody] CreateShelfRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _shelfService.CreateShelfAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Tạo kệ sách mới thành công (S02)";
        TempData["StatusDetail"] = message;
        return Ok(new { message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBookToShelf([FromBody] AddBookToShelfRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _shelfService.AddBookToShelfAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã thêm sách vào tủ cá nhân (S02)";
        TempData["StatusDetail"] = message;
        return Ok(new { message });
    }
}
