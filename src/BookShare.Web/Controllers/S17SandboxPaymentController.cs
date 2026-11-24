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
    public async Task<IActionResult> TopUpVirtualWallet([FromBody] S17WalletTopUpDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, newBalance, txId, rateLimited) = await _paymentService.TopUpVirtualWalletAsync(
            currentUserId,
            request?.Amount ?? 0m,
            request?.Note,
            request?.IdempotencyKey);

        if (!success)
        {
            if (rateLimited)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new { message, balance = newBalance });
            }
            return BadRequest(new { message, balance = newBalance, transactionId = txId });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Nạp tiền ảo miễn phí thành công";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Nạp tiền ảo miễn phí thành công",
            message,
            balance = newBalance,
            transactionId = txId
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewQrInvoice([FromBody] S17PreviewQrInvoiceDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message, invoice) = await _paymentService.PreviewQrInvoiceAsync(
                currentUserId,
                currentUserRole,
                request?.QrPayload ?? string.Empty);

            if (!success)
            {
                return BadRequest(new { message, invoice });
            }

            return Ok(new { title = "Đã đọc hóa đơn thanh toán", message, invoice });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmQrInvoice([FromBody] S17ConfirmQrInvoiceDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message, newBalance) = await _paymentService.ConfirmQrInvoicePaymentAsync(
                currentUserId,
                currentUserRole,
                request?.TransactionId ?? 0);

            if (!success)
            {
                return BadRequest(new { message, balance = newBalance });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Thanh toán QR thành công";
            TempData["StatusDetail"] = message;

            return Ok(new
            {
                title = "Thanh toán hóa đơn QR thành công",
                message,
                balance = newBalance
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> InvoiceStatus(int txId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (found, status, amount, description) = await _paymentService.GetInvoiceStatusAsync(
                currentUserId,
                currentUserRole,
                txId);

            if (!found)
            {
                return NotFound(new { message = $"Không tìm thấy hóa đơn #TX-{txId}." });
            }

            return Ok(new { transactionId = txId, status, amount, description });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
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
            title = "Đã khởi tạo hóa đơn & mã QR thanh toán",
            message,
            transactionId = txId,
            qrPayload = $"BOOKSHARE-PAY:{txId}",
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
            TempData["StatusTitle"] = "Đã xóa hóa đơn chưa hoàn tất";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã xóa hóa đơn", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
