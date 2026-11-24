using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S16MembershipTierController : Controller
{
    private readonly IS16MembershipTierService _membershipService;
    private readonly IS17SandboxPaymentService _paymentService;

    public S16MembershipTierController(
        IS16MembershipTierService membershipService,
        IS17SandboxPaymentService paymentService)
    {
        _membershipService = membershipService;
        _paymentService = paymentService;
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
    public async Task<IActionResult> PreviewTierSwitch([FromBody] S16PreviewTierSwitchDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, preview) = await _membershipService.PreviewTierSwitchAsync(
            currentUserId,
            request?.TargetTier ?? "Premium",
            request?.DurationMonths ?? 12);

        if (!success)
        {
            return BadRequest(new { message, preview });
        }

        return Ok(new { title = "Báo giá minh bạch gói hội viên", message, preview });
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

        decimal walletBalance = await _paymentService.GetWalletBalanceAsync(currentUserId);
        string? redirectUrl = createdTransactionId.HasValue
            ? Url.Action("Index", "S17SandboxPayment", new { txId = createdTransactionId.Value })
            : null;
        string? qrPayload = createdTransactionId.HasValue
            ? $"BOOKSHARE-PAY:{createdTransactionId.Value}"
            : null;

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = createdTransactionId.HasValue
            ? "Đã tạo hóa đơn & mã QR thanh toán gói hội viên"
            : "Đã cập nhật Hạng thẻ Hội viên thành công";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = createdTransactionId.HasValue
                ? "Quét mã QR hoặc mở Ví Tiền Ảo để hoàn tất"
                : "Đã kích hoạt Gói Hội Viên thành công",
            message,
            createdTransactionId,
            qrPayload,
            rawPayload,
            expectedHmac,
            walletBalance,
            redirectUrl
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelMembership([FromBody] S16CancelMembershipRequestDto? request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _membershipService.CancelMembershipAsync(currentUserId, request?.Reason);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã hủy gói hội viên thành công";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã hủy gói hội viên về gói Free",
            message
        });
    }
}
