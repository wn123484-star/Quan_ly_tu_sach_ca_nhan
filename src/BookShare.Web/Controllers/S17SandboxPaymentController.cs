using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S17SandboxPaymentController : Controller
{
    private readonly IS17SandboxPaymentService _paymentService;

    public S17SandboxPaymentController(IS17SandboxPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? txId = null)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUserRole = User.Identity?.IsAuthenticated == true
            ? (User.FindFirstValue(ClaimTypes.Role) ?? "User")
            : "Guest";
        var vm = await _paymentService.GetSandboxPaymentPageAsync(currentUserId, currentUserRole, txId);
        return View("~/Views/Modules/S17_SandboxPayment/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder([FromBody] S17CreateSandboxOrderDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, txId, rawPayload, expectedHmac) = await _paymentService.CreateSandboxOrderAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Đã khởi tạo giao dịch thanh toán",
            message,
            transactionId = txId,
            rawPayload,
            expectedHmacSignature = expectedHmac
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyCallback([FromBody] S17SandboxCallbackDto callback)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var (success, message) = await _paymentService.VerifyCallbackAndCompleteTransactionAsync(
            currentUserId,
            currentUserRole,
            callback);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Xác nhận thanh toán thành công";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Xác nhận thanh toán thành công",
            message
        });
    }

    [HttpPost]
    [Authorize(Roles = "Librarian,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundDeposit([FromBody] S17RefundDepositDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "Librarian";
        var (success, message) = await _paymentService.RefundSimulatedDepositAsync(
            currentUserId,
            currentUserRole,
            request.TransactionId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã hoàn trả tiền cọc bảo lãnh";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Hoàn trả tiền cọc bảo lãnh thành công",
            message
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelTransaction([FromBody] S17CancelTransactionDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _paymentService.CancelPendingTransactionAsync(currentUserId, currentUserRole, request.TransactionId);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã xóa lệnh giao dịch chưa hoàn tất";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã xóa lệnh giao dịch", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
