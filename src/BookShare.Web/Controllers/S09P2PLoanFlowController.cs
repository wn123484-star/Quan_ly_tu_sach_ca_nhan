using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

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
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _p2pLoanService.GetP2PLoanFlowAsync(currentUserId);
        return View("~/Views/Modules/S09_P2PLoanFlow/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRequest([FromBody] CreateP2PLoanRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _p2pLoanService.CreateP2PLoanRequestAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã gửi yêu cầu mượn sách P2P (S09)";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã gửi yêu cầu mượn P2P (S09)", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveOrReject([FromBody] ApproveP2PLoanDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-02";
        var (success, message) = await _p2pLoanService.ApproveOrRejectP2PLoanAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Chủ sách P2P đã phản hồi yêu cầu (S09)";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã cập nhật duyệt mượn P2P (S09)", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvanceStage([FromBody] P2PStageTransitionDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _p2pLoanService.AdvanceP2PStageAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Cập nhật tiến trình giao nhận P2P thành công (S09)";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã chuyển bước giao nhận P2P (S09)", message });
    }
}
