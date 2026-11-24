using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Implementations;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

[Route("api/qr-login")]
public class QrAuthController : Controller
{
    private readonly IQrAuthService _qrAuthService;

    public QrAuthController(IQrAuthService qrAuthService)
    {
        _qrAuthService = qrAuthService;
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create()
    {
        string? ua = Request.Headers.UserAgent.ToString();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var (success, message, data) = await _qrAuthService.CreateWebLoginSessionAsync(ua, ip);
        if (!success || data == null)
        {
            int statusCode = message.Contains("quá nhanh", StringComparison.OrdinalIgnoreCase) ? 429 : 400;
            return StatusCode(statusCode, new { success = false, message });
        }

        return Ok(new { success = true, message, data });
    }

    [HttpGet("status/{sessionId:int}")]
    public async Task<IActionResult> GetStatus(int sessionId)
    {
        var status = await _qrAuthService.GetSessionStatusAsync(sessionId);
        return Ok(status);
    }

    [HttpPost("preview")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview([FromBody] PreviewQrLoginRequestDto request)
    {
        var (success, message, data) = await _qrAuthService.PreviewWebLoginSessionAsync(request);
        if (!success || data == null)
        {
            return BadRequest(new { success = false, message });
        }

        return Ok(new { success = true, message, data });
    }

    [HttpPost("approve")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve([FromBody] ApproveQrLoginRequestDto request)
    {
        string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var (success, message) = await _qrAuthService.ApproveWebLoginSessionAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { success = false, message });
        }

        return Ok(new
        {
            success = true,
            title = request.Approve ? "Phê duyệt thành công" : "Đã từ chối đăng nhập",
            message
        });
    }

    [HttpPost("claim")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Claim([FromBody] ClaimQrLoginRequestDto request)
    {
        var (success, statusCode, message, userClaims) = await _qrAuthService.ClaimWebLoginSessionAsync(request);
        if (!success || userClaims == null)
        {
            return StatusCode(statusCode, new { success = false, message });
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AuthConfigHelper.BuildClaimsPrincipal(userClaims),
            AuthConfigHelper.CreateCookieProperties(isPersistent: true));

        return Ok(new
        {
            success = true,
            title = "Đăng nhập thành công",
            message,
            redirectUrl = "/"
        });
    }

    [HttpPost("create-mobile-link")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMobileLink()
    {
        string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        string? ua = Request.Headers.UserAgent.ToString();
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var (success, message, data) = await _qrAuthService.CreateMobileLinkSessionAsync(currentUserId, ua, ip);
        if (!success || data == null)
        {
            return BadRequest(new { success = false, message });
        }

        return Ok(new { success = true, message, data });
    }

    [HttpPost("mobile-claim")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MobileClaim([FromBody] MobileClaimQrLoginRequestDto request)
    {
        var (success, statusCode, message, userClaims) = await _qrAuthService.ClaimMobileLinkSessionAsync(request);
        if (!success || userClaims == null)
        {
            return StatusCode(statusCode, new { success = false, message });
        }

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AuthConfigHelper.BuildClaimsPrincipal(userClaims),
            AuthConfigHelper.CreateCookieProperties(isPersistent: true));

        return Ok(new
        {
            success = true,
            title = "Đồng bộ đăng nhập thành công",
            message,
            redirectUrl = "/"
        });
    }
}
