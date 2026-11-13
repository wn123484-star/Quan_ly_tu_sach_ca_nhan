using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S14AiRecommendationController : Controller
{
    private readonly IS14AiRecommendationService _recService;
    private readonly IS03ReadingTrackerService _trackerService;

    public S14AiRecommendationController(
        IS14AiRecommendationService recService,
        IS03ReadingTrackerService trackerService)
    {
        _recService = recService;
        _trackerService = trackerService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string mood = "all", string major = "all")
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _recService.GetRecommendationsPageAsync(currentUserId, mood, major);
        return View("~/Views/Modules/S14_AiRecommendation/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateCustomAdvice([FromBody] S14GenerateCustomRecRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, aiAdvice, suggestedBooks) = await _recService.GenerateAiCustomAdviceAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message = "Không thể tạo lộ trình đọc cá nhân hóa." });
        }

        return Ok(new
        {
            title = "Đã sinh lộ trình gợi ý sách cá nhân hóa (S14 AI)",
            message = "Gemini AI đã phân tích lịch sử đọc S03 và đề xuất lộ trình phù hợp.",
            aiAdvice,
            suggestedBooks
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickAddToTracker([FromBody] AddTrackingBookRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _trackerService.AddBookToTrackerAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Đã thêm vào mục tiêu đọc (S03)",
            message
        });
    }
}
