using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S19DisputeResolutionController : Controller
{
    private readonly IS19DisputeResolutionService _disputeService;

    public S19DisputeResolutionController(IS19DisputeResolutionService disputeService)
    {
        _disputeService = disputeService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var vm = await _disputeService.GetDisputeResolutionPageAsync(currentUserId, currentUserRole);
        return View("~/Views/Modules/S19_DisputeResolution/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDispute([FromBody] S19CreateDisputeRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, disputeId) = await _disputeService.CreateDisputeAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã gửi hồ sơ khiếu nại tranh chấp sách";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã tạo hồ sơ tranh chấp sách",
            message,
            disputeId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ResolveDispute([FromBody] S19AdminResolveDisputeDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-admin-01";
        var (success, message) = await _disputeService.ResolveDisputeByAdminAsync(adminUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Trọng tài Admin đã ban hành phán quyết tranh chấp";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã ban hành phán quyết tranh chấp thành công",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDispute([FromBody] S19UpdateDisputeRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _disputeService.UpdateDisputeReasonAsync(currentUserId, currentUserRole, request);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã cập nhật hồ sơ khiếu nại";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã cập nhật hồ sơ khiếu nại", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDispute([FromBody] S19DeleteDisputeRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _disputeService.DeleteDisputeAsync(currentUserId, currentUserRole, request.DisputeId);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã rút/xóa hồ sơ tranh chấp";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã rút/xóa hồ sơ tranh chấp", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
