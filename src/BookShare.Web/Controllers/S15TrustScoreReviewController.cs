using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S15TrustScoreReviewController : Controller
{
    private readonly IS15TrustScoreReviewService _reviewService;

    public S15TrustScoreReviewController(IS15TrustScoreReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _reviewService.GetTrustScoreReviewPageAsync(currentUserId);
        return View("~/Views/Modules/S15_TrustScoreReview/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReview([FromBody] SubmitReviewRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, newTargetTrustScore) = await _reviewService.SubmitTwoWayReviewAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã lưu đánh giá 2 chiều & cập nhật TrustScore (S15)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã lưu đánh giá & cập nhật TrustScore (S15)",
            message,
            newTargetTrustScore
        });
    }
}
