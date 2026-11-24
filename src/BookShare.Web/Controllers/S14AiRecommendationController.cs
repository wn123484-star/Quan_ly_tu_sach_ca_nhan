using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var vm = await _recService.GetRecommendationsPageAsync(currentUserId, mood, major);
        return View("~/Views/Modules/S14_AiRecommendation/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateCustomAdvice([FromBody] S14GenerateCustomRecRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, aiAdvice, suggestedBooks) = await _recService.GenerateAiCustomAdviceAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message = "Không thể tạo lộ trình đọc cá nhân hóa." });
        }

        return Ok(new
        {
            title = "Đã sinh lộ trình gợi ý sách cá nhân hóa",
            message = "Trợ lý đọc sách đã phân tích nhu cầu của bạn và đề xuất lộ trình phù hợp.",
            aiAdvice,
            suggestedBooks
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickAddToTracker([FromBody] AddTrackingBookRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _trackerService.AddBookToTrackerAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Đã thêm sách vào danh sách theo dõi tiến độ đọc",
            message
        });
    }
}
