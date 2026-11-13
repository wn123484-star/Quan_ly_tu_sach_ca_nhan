using System.Security.Claims;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S10ConditionReportController : Controller
{
    private readonly IS10ConditionReportService _conditionService;

    public S10ConditionReportController(IS10ConditionReportService conditionService)
    {
        _conditionService = conditionService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? loanId = null)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _conditionService.GetConditionReportsPageAsync(currentUserId, loanId);
        return View("~/Views/Modules/S10_ConditionReport/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadReport(
        [FromForm] int loanId,
        [FromForm] string reportType,
        [FromForm] string conditionGrade,
        [FromForm] string? note,
        [FromForm] IFormFile? imageFile,
        [FromForm] string? presetImageUrl)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, imageUrl) = await _conditionService.SaveConditionReportAsync(
            currentUserId,
            loanId,
            reportType,
            conditionGrade,
            note,
            imageFile,
            presetImageUrl);

        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã lưu biên bản ảnh tình trạng sách (S10)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã lưu biên bản ảnh tình trạng sách (S10)",
            message,
            imageUrl
        });
    }
}
