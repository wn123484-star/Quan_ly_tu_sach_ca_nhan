using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

/// <summary>
/// [BS-WALLET-PAY] &amp; [BS-QRLOGIN-BRIDGE] API dùng chung cho Web Portal &amp; BookShare Mobile Super-App (/MobileApp).
/// Tuân thủ kiến trúc 3 tầng: Controller -> IWalletService / IQrLoginService -> DbContext.
/// </summary>
[ApiController]
[Route("api/mobile")]
public class MobileApiController : ControllerBase
{
    private readonly IWalletService _walletService;
    private readonly IQrLoginService _qrLoginService;
    private readonly IS03ReadingTrackerService _readingService;
    private readonly IS12LoanRenewalFineService _renewalService;
    private readonly ISupportChatService _supportChatService;

    public MobileApiController(
        IWalletService walletService,
        IQrLoginService qrLoginService,
        IS03ReadingTrackerService readingService,
        IS12LoanRenewalFineService renewalService,
        ISupportChatService supportChatService)
    {
        _walletService = walletService;
        _qrLoginService = qrLoginService;
        _readingService = readingService;
        _renewalService = renewalService;
        _supportChatService = supportChatService;
    }

    // ==================== 1. VÍ BẢO LÃNH BOOKSHARE PAY ====================

    [HttpGet("wallet/summary")]
    public async Task<IActionResult> WalletSummary()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var summary = await _walletService.GetWalletSummaryAsync(userId);
        return Ok(summary);
    }

    [HttpPost("wallet/topup")]
    public async Task<IActionResult> WalletTopUp([FromBody] WalletTopUpRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, newBalance, txId, hmac) = await _walletService.TopUpDemoAsync(userId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Nạp tiền Ví BookShare Pay thành công",
            message,
            balance = newBalance,
            transactionId = txId,
            hmacSignature = hmac
        });
    }

    [HttpPost("wallet/pay-qr")]
    public async Task<IActionResult> WalletPayQr([FromBody] WalletPayQrRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, newBalance, txId, hmac) = await _walletService.PayQrWithPinAsync(userId, request);
        if (!success)
        {
            return BadRequest(new { message, balance = newBalance });
        }

        return Ok(new
        {
            title = "Thanh toán QR bằng PIN thành công",
            message,
            balance = newBalance,
            transactionId = txId,
            hmacSignature = hmac
        });
    }

    [HttpPost("wallet/change-pin")]
    public async Task<IActionResult> WalletChangePin([FromBody] WalletChangePinRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _walletService.ChangePinAsync(userId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { title = "Đổi mã PIN Ví Pay thành công", message });
    }

    // ==================== 2. QR CROSS-LOGIN (MOBILE -> WEB PC) ====================

    [HttpPost("qr-login/init")]
    public async Task<IActionResult> QrLoginInit()
    {
        var data = await _qrLoginService.InitSessionAsync();
        return Ok(data);
    }

    [HttpGet("qr-login/status")]
    public async Task<IActionResult> QrLoginStatus([FromQuery] string token)
    {
        var data = await _qrLoginService.GetStatusAsync(token);
        return Ok(data);
    }

    // [BS-RBAC-QRLOGIN] Bắt buộc thiết bị Mobile đã đăng nhập mới có quyền duyệt mã QR
    [HttpPost("qr-login/approve")]
    [Authorize]
    public async Task<IActionResult> QrLoginApprove([FromBody] QrLoginTokenRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Vui lòng đăng nhập trên Mobile App trước khi duyệt mã QR." });
        }

        var (success, message) = await _qrLoginService.ApproveSessionAsync(request.Token, userId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { title = "Đã duyệt đăng nhập Web PC bằng QR", message });
    }

    // ==================== 3. [BS-READING-STUDIO] READING STUDIO API (IM LẶNG 100%) ====================

    [HttpGet("reading/studio-summary")]
    public async Task<IActionResult> ReadingStudioSummary([FromQuery] int? bookId = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _readingService.GetReadingStudioAsync(userId, bookId);
        return Ok(vm);
    }

    [HttpPost("reading/log-session")]
    public async Task<IActionResult> LogReadingSession([FromBody] LogReadingSessionRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, newPage, newPct, pph, streak) = await _readingService.LogReadingSessionAsync(userId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Đã lưu phiên đọc Pomodoro",
            message,
            currentPage = newPage,
            percentCompleted = newPct,
            pagesPerHour = pph,
            readingStreakDays = streak
        });
    }

    [HttpPost("reading/voice-quote")]
    public async Task<IActionResult> SaveVoiceQuote([FromBody] SaveVoiceQuoteRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, noteId) = await _readingService.SaveVoiceQuoteAsync(userId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { title = "Đã lưu trích dẫn giọng nói", message, noteId });
    }

    [HttpPost("reading/quick-renew")]
    public async Task<IActionResult> QuickRenewLoan([FromBody] QuickRenewLoanRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, newDueDate, newRenewCount) = await _renewalService.RenewLoanAsync(userId, request.LoanId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { title = "Gia hạn thuê sách thành công (+7 ngày)", message, newDueDate, newRenewCount });
    }

    // ==================== 4. [BS-AGENT-AI-CHAT] CHAT TRỰC TIẾP THỦ THƯ / CHỦ SÁCH ====================

    [HttpGet("support-chat/messages")]
    public async Task<IActionResult> SupportChatMessages([FromQuery] string channel = "Librarian")
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var list = await _supportChatService.GetMessagesAsync(userId, role, channel);
        return Ok(list);
    }

    [HttpPost("support-chat/send")]
    public async Task<IActionResult> SupportChatSend([FromBody] SendSupportChatRequestDto request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var (success, message, saved) = await _supportChatService.SendMessageAsync(userId, role, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { title = "Đã gửi tin nhắn trực tiếp", message, data = saved });
    }
}
