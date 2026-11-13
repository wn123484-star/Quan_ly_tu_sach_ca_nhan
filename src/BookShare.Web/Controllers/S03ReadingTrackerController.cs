using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S03ReadingTrackerController : Controller
{
    private readonly IS03ReadingTrackerService _trackerService;

    public S03ReadingTrackerController(IS03ReadingTrackerService trackerService)
    {
        _trackerService = trackerService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _trackerService.GetReadingTrackerAsync(currentUserId);
        return View("~/Views/Modules/S03_ReadingTracker/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProgress([FromBody] UpdateReadingProgressRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        try
        {
            var (success, message, statusLabel, percent) = await _trackerService.UpdateReadingProgressAsync(
                currentUserId,
                request);

            if (!success)
            {
                return BadRequest(new { message });
            }

            return Ok(new
            {
                title = "Đã lưu tiến độ đọc sách (S03)",
                message,
                statusLabel,
                percent
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBookToTracker(AddTrackingBookRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _trackerService.AddBookToTrackerAsync(currentUserId, request);

        TempData["StatusCode"] = success ? 200 : 400;
        TempData["StatusTitle"] = success ? "Đã thêm sách theo dõi (S03)" : "Không thể thêm sách theo dõi";
        TempData["StatusDetail"] = message;

        return RedirectToAction(nameof(Index));
    }
}
