using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var vm = await _shelfService.GetPersonalShelfAsync(currentUserId, shelfId);
        return View("~/Views/Modules/S02_PersonalShelf/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLendable([FromBody] ToggleLendableRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
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
                title = "Đã cập nhật trạng thái cho mượn cộng đồng",
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
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateShelf([FromBody] CreateShelfRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _shelfService.CreateShelfAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Tạo kệ sách mới thành công";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Tạo kệ sách mới thành công", message });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBookToShelf([FromBody] AddBookToShelfRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _shelfService.AddBookToShelfAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã thêm sách vào tủ cá nhân";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã thêm sách vào tủ cá nhân", message });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCopy([FromBody] UpdateBookCopyRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            var (success, message) = await _shelfService.UpdateBookCopyAsync(currentUserId, request);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã cập nhật bản sao sách";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã cập nhật bản sao sách", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCopy([FromBody] DeleteBookCopyRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            var (success, message) = await _shelfService.DeleteBookCopyAsync(currentUserId, request.BookCopyId);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã xóa bản sao sách khỏi tủ";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã xóa bản sao sách", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteShelf([FromBody] DeleteShelfRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            var (success, message) = await _shelfService.DeleteShelfAsync(currentUserId, request.ShelfId);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã xóa kệ sách tùy chỉnh";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã xóa kệ sách", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateShelf([FromBody] UpdateShelfRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            var (success, message) = await _shelfService.UpdateShelfAsync(currentUserId, request);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã cập nhật kệ sách";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã cập nhật kệ sách", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
