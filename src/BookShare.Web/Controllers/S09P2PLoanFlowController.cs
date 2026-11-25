using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

/// <summary>
/// [S09] Controller điều phối Luồng Mượn – Trả sách Cộng đồng ngang hàng P2P qua Điểm trạm CLB, thưởng suất mượn và điểm uy tín.
/// </summary>
/// <remarks>RBAC: Độc giả gửi/hủy yêu cầu; Chủ sách duyệt/từ chối; Chủ sách, Người mượn, Trưởng/Phó CLB quản lý trạm, Thủ thư và Admin chuyển bước giao nhận.</remarks>
public class S09P2PLoanFlowController : Controller
{
    private readonly IS09P2PLoanService _p2pLoanService;

    public S09P2PLoanFlowController(IS09P2PLoanService p2pLoanService)
    {
        _p2pLoanService = p2pLoanService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var vm = await _p2pLoanService.GetP2PLoanFlowAsync(currentUserId);
        return View("~/Views/Modules/S09_P2PLoanFlow/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRequest([FromBody] CreateP2PLoanRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _p2pLoanService.CreateP2PLoanRequestAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã gửi yêu cầu mượn sách Cộng đồng";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã gửi yêu cầu mượn sách", message });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveOrReject([FromBody] ApproveP2PLoanDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var (success, message) = await _p2pLoanService.ApproveOrRejectP2PLoanAsync(currentUserId, currentUserRole, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Chủ sách đã phản hồi yêu cầu";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã cập nhật trạng thái mượn sách", message });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvanceStage([FromBody] P2PStageTransitionDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var (success, message) = await _p2pLoanService.AdvanceP2PStageAsync(currentUserId, currentUserRole, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Cập nhật tiến trình giao nhận thành công";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã chuyển bước giao nhận", message });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelRequest([FromBody] CancelP2PLoanRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var (success, message) = await _p2pLoanService.CancelP2PLoanRequestAsync(currentUserId, currentUserRole, request.LoanRequestId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã hủy yêu cầu mượn sách cộng đồng";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã hủy yêu cầu mượn", message });
    }
}
