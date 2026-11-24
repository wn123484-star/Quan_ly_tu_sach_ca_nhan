using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Implementations;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S01AuthOtpGoogleController : Controller
{
    private readonly IS01AuthOtpService _authService;
    private readonly IResendEmailService _resendOtpService;
    private readonly IRecaptchaService _recaptchaService;
    private readonly IConfiguration _config;

    public S01AuthOtpGoogleController(
        IS01AuthOtpService authService,
        IResendEmailService resendOtpService,
        IRecaptchaService recaptchaService,
        IConfiguration config)
    {
        _authService = authService;
        _resendOtpService = resendOtpService;
        _recaptchaService = recaptchaService;
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? returnUrl = null)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var vm = await _authService.GetAuthPageViewModelAsync(currentUserId, returnUrl);
        return View("~/Views/Modules/S01_AuthOtpGoogle/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInputModel input)
    {
        // [BOT-GUARD] Xác thực Google reCAPTCHA v3 cho luồng Đăng nhập (Smart Bypass nếu chưa điền key)
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (captchaOk, _, captchaMsg) = await _recaptchaService.VerifyTokenAsync(input.RecaptchaToken, "login", remoteIp);
        if (!captchaOk)
        {
            TempData["StatusCode"] = 403;
            TempData["StatusTitle"] = "Chặn truy cập bất thường (reCAPTCHA v3)";
            TempData["StatusDetail"] = captchaMsg;
            return RedirectToAction(nameof(Index), new { returnUrl = input.ReturnUrl });
        }

        var result = await _authService.AuthenticateAsync(input.Email, input.Password);
        if (!result.Success || result.UserClaims == null)
        {
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Đăng nhập không thành công";
            TempData["StatusDetail"] = result.Message;
            return RedirectToAction(nameof(Index), new { returnUrl = input.ReturnUrl });
        }

        await SignInCookieAsync(result.UserClaims, input.RememberMe);

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = $"Xin chào {result.UserClaims.FullName}";
        TempData["StatusDetail"] = result.Message;

        if (!string.IsNullOrWhiteSpace(input.ReturnUrl) && Url.IsLocalUrl(input.ReturnUrl))
        {
            return Redirect(input.ReturnUrl);
        }

        return RedirectToAction("Index", "S02PersonalShelf");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterInputModel input)
    {
        // [BOT-GUARD] Xác thực Google reCAPTCHA v3 cho luồng Đăng ký tài khoản
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (captchaOk, _, captchaMsg) = await _recaptchaService.VerifyTokenAsync(input.RecaptchaToken, "register", remoteIp);
        if (!captchaOk)
        {
            TempData["StatusCode"] = 403;
            TempData["StatusTitle"] = "Chặn đăng ký tự động";
            TempData["StatusDetail"] = captchaMsg;
            return RedirectToAction(nameof(Index));
        }

        var result = await _authService.RegisterUserAsync(input);
        if (!result.Success || result.UserClaims == null)
        {
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Đăng ký tài khoản thất bại";
            TempData["StatusDetail"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        await SignInCookieAsync(result.UserClaims, isPersistent: true);

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đăng ký & Đăng nhập thành công";
        TempData["StatusDetail"] = result.Message;
        return RedirectToAction("Index", "S02PersonalShelf");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã đăng xuất an toàn";
        TempData["StatusDetail"] = "Phiên làm việc của bạn đã được đóng an toàn.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpRequestDto request)
    {
        // [BOT-GUARD] Xác thực Google reCAPTCHA v3 chống spam gửi Email OTP qua Resend
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (captchaOk, _, captchaMsg) = await _recaptchaService.VerifyTokenAsync(request.RecaptchaToken, "send_otp", remoteIp);
        if (!captchaOk)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                title = "Chặn spam gửi OTP (reCAPTCHA v3)",
                message = captchaMsg
            });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var (success, message, devOtpCode) = await _resendOtpService.GenerateAndSendOtpAsync(
            request.Email,
            request.Purpose,
            currentUserId);

        if (!success)
        {
            return BadRequest(new { title = "Không thể gửi OTP", message });
        }

        return Ok(new
        {
            title = "Đã gửi mã xác thực OTP 6 chữ số",
            message,
            devOtpCode
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequestDto request)
    {
        var (success, message) = await _resendOtpService.VerifyOtpAsync(
            request.Email,
            request.OtpCode,
            request.Purpose);

        if (!success)
        {
            return BadRequest(new { title = "Xác thực OTP thất bại", message });
        }

        return Ok(new
        {
            title = "Xác thực OTP thành công",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GoogleLogin(string? returnUrl = null)
    {
        if (AuthConfigHelper.IsGoogleOAuthConfigured(_config))
        {
            var redirectUrl = Url.Action(nameof(GoogleCallback), "S01AuthOtpGoogle", new { returnUrl });
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        const string errMsg = "Đăng nhập Google hiện chưa được kích hoạt do quản trị viên chưa cấu hình khóa Google OAuth 2.0 hợp lệ.";
        if (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { title = "Đăng nhập Google chưa được kích hoạt", message = errMsg });
        }

        TempData["StatusCode"] = 400;
        TempData["StatusTitle"] = "Đăng nhập Google chưa được kích hoạt";
        TempData["StatusDetail"] = errMsg;
        Response.StatusCode = StatusCodes.Status400BadRequest;

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var vm = await _authService.GetAuthPageViewModelAsync(currentUserId, returnUrl);
        return View("~/Views/Modules/S01_AuthOtpGoogle/Index.cshtml", vm);
    }

    [HttpGet]
    public async Task<IActionResult> GoogleCallback(string? returnUrl = null)
    {
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Xác thực Google không thành công";
            TempData["StatusDetail"] = "Không nhận được thông tin định danh hợp lệ từ Google.";
            return RedirectToAction(nameof(Index));
        }

        var providerKey = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = result.Principal.FindFirstValue(ClaimTypes.Email);
        var fullName = result.Principal.FindFirstValue(ClaimTypes.Name) ?? "Thành viên BookShare";

        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(email))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Thiếu thông tin tài khoản Google";
            TempData["StatusDetail"] = "Không thể xác minh địa chỉ Email từ tài khoản Google của bạn.";
            return RedirectToAction(nameof(Index));
        }

        var upsert = await _authService.UpsertGoogleLoginAsync(providerKey, email, fullName);
        if (!upsert.Success || upsert.UserClaims == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["StatusCode"] = 403;
            TempData["StatusTitle"] = "Từ chối đăng nhập";
            TempData["StatusDetail"] = upsert.Message;
            return RedirectToAction(nameof(Index));
        }

        await SignInCookieAsync(upsert.UserClaims, isPersistent: true);
        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = $"Xin chào {upsert.UserClaims.FullName}";
        TempData["StatusDetail"] = upsert.Message;

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "S02PersonalShelf");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Vui lòng đăng nhập tài khoản để cập nhật hồ sơ." });
        }

        var (success, message, updatedClaims) = await _authService.UpdateUserProfileAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        if (User.Identity?.IsAuthenticated == true && updatedClaims != null)
        {
            await SignInCookieAsync(updatedClaims, isPersistent: true);
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã cập nhật hồ sơ cá nhân";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Cập nhật hồ sơ cá nhân thành công", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized(new { message = "Vui lòng đăng nhập tài khoản để đổi mật khẩu." });
        }

        var (success, message) = await _authService.ChangeUserPasswordAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { title = "Đổi mật khẩu thành công", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> SubmitCuratorRequest([FromBody] SubmitCuratorRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _authService.SubmitCuratorRequestAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã gửi đề nghị nâng quyền Chủ nhiệm CLB";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã gửi đề nghị nâng quyền Chủ nhiệm CLB", message });
    }

    private async Task SignInCookieAsync(AuthUserClaimsDto userClaims, bool isPersistent)
    {
        var principal = Services.Implementations.AuthConfigHelper.BuildClaimsPrincipal(userClaims);
        var props = Services.Implementations.AuthConfigHelper.CreateCookieProperties(isPersistent);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            props);
    }
}
