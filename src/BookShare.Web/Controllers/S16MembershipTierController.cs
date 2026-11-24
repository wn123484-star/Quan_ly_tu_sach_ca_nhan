using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S16MembershipTierController : Controller
{
    private readonly IS16MembershipTierService _membershipService;

    public S16MembershipTierController(IS16MembershipTierService membershipService)
    {
        _membershipService = membershipService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var vm = await _membershipService.GetMembershipTierPageAsync(currentUserId);
        return View("~/Views/Modules/S16_MembershipTier/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchMembership([FromBody] S16SwitchMembershipRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, createdTransactionId, rawPayload, expectedHmac) =
            await _membershipService.SwitchOrExtendMembershipAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        string? redirectUrl = createdTransactionId.HasValue
            ? Url.Action("Index", "S17SandboxPayment", new { txId = createdTransactionId.Value })
            : null;

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = createdTransactionId.HasValue
            ? "Đã tạo yêu cầu thanh toán nâng cấp Premium"
            : "Đã cập nhật Hạng thẻ Thành viên";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = createdTransactionId.HasValue
                ? "Chuyển sang Cổng thanh toán để hoàn tất"
                : "Đã cập nhật Hạng thẻ Thành viên",
            message,
            createdTransactionId,
            rawPayload,
            expectedHmac,
            redirectUrl
        });
    }
}
