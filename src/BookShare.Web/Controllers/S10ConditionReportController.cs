using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var vm = await _conditionService.GetConditionReportsPageAsync(currentUserId, loanId);
        return View("~/Views/Modules/S10_ConditionReport/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadReport(
        [FromForm] int loanId,
        [FromForm] string reportType,
        [FromForm] string conditionGrade,
        [FromForm] string? note,
        [FromForm] IFormFile? imageFile,
        [FromForm] string? presetImageUrl)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message, imageUrl) = await _conditionService.SaveConditionReportAsync(
                currentUserId,
                currentUserRole,
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
            TempData["StatusTitle"] = "Đã lưu biên bản kiểm định tình trạng sách";
            TempData["StatusDetail"] = message;

            return Ok(new
            {
                title = "Đã lưu biên bản kiểm định tình trạng sách",
                message,
                imageUrl
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
    public async Task<IActionResult> UpdateReport([FromBody] UpdateConditionReportRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _conditionService.UpdateConditionReportAsync(currentUserId, currentUserRole, request);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã cập nhật biên bản kiểm định";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã cập nhật biên bản kiểm định", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReport([FromBody] DeleteConditionReportRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _conditionService.DeleteConditionReportAsync(currentUserId, currentUserRole, request.ReportId);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã xóa biên bản kiểm định";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã xóa biên bản kiểm định", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
