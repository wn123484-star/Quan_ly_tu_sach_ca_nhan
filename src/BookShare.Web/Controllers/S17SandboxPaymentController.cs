using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
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
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _paymentService.GetSandboxPaymentPageAsync(currentUserId);
        return View("~/Views/Modules/S17_SandboxPayment/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder([FromBody] S17CreateSandboxOrderDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, txId, rawPayload, expectedHmac) = await _paymentService.CreateSandboxOrderAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Đã khởi tạo giao dịch & sinh chữ ký HMAC-SHA512 (S17)",
            message,
            transactionId = txId,
            rawPayload,
            expectedHmacSignature = expectedHmac
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyCallback([FromBody] S17SandboxCallbackDto callback)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _paymentService.VerifyCallbackAndCompleteTransactionAsync(currentUserId, callback);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Xác thực Callback HMAC-SHA512 thành công (S17)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Xác thực Callback HMAC-SHA512 thành công (S17)",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundDeposit([FromBody] S17RefundDepositDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _paymentService.RefundSimulatedDepositAsync(currentUserId, request.TransactionId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã hoàn trả tiền cọc mô phỏng (S17)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Hoàn trả tiền cọc thành công (S17)",
            message
        });
    }
}
