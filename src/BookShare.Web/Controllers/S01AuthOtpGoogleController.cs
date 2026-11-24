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
    private readonly IQrAuthService _qrAuthService;
    private readonly IResendEmailService _resendOtpService;
    private readonly IRecaptchaService _recaptchaService;
    private readonly IConfiguration _config;

    public S01AuthOtpGoogleController(
        IS01AuthOtpService authService,
        IQrAuthService qrAuthService,
        IResendEmailService resendOtpService,
        IRecaptchaService recaptchaService,
        IConfiguration config)
    {
        _authService = authService;
        _qrAuthService = qrAuthService;
        _resendOtpService = resendOtpService;
        _recaptchaService = recaptchaService;
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? returnUrl = null, string? oauthError = null)
    {
        if (!string.IsNullOrWhiteSpace(oauthError))
        {
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Xác thực Google không thành công";
            TempData["StatusDetail"] = oauthError.Trim();
        }
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
        if (!AuthConfigHelper.IsValidEmailFormat(request?.Email))
        {
            return BadRequest(new
            {
                title = "Chưa có địa chỉ Email hợp lệ",
                message = "Vui lòng điền địa chỉ Email nhận thư hợp lệ trong Cài Đặt Tài Khoản trước khi gửi mã OTP.",
                requiresEmailSetup = true
            });
        }

        // [BOT-GUARD] Xác thực Google reCAPTCHA v3 chống spam gửi Email OTP qua Resend
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (captchaOk, _, captchaMsg) = await _recaptchaService.VerifyTokenAsync(request!.RecaptchaToken, "send_otp", remoteIp);
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

        // Đồng bộ lại claims cookie ngay sau khi xác minh Email (tránh sai lệch Email giữa S01 và S18)
        if (User.Identity?.IsAuthenticated == true)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(currentUserId))
            {
                var freshClaims = await _authService.GetUserClaimsByIdAsync(currentUserId);
                if (freshClaims != null)
                {
                    await SignInCookieAsync(freshClaims, isPersistent: true);
                }
            }
        }

        return Ok(new
        {
            title = "Xác thực OTP thành công",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendPasswordResetOtp([FromBody] ForgotPasswordSendOtpRequestDto request)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (captchaOk, _, captchaMsg) = await _recaptchaService.VerifyTokenAsync(request?.RecaptchaToken, "forgot_password", remoteIp);
        if (!captchaOk)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                title = "Chặn yêu cầu bất thường (reCAPTCHA v3)",
                message = captchaMsg
            });
        }

        var (success, message, devOtpCode) = await _authService.RequestPasswordResetOtpAsync(request?.Email ?? string.Empty);
        if (!success)
        {
            return BadRequest(new { title = "Không thể gửi mã OTP khôi phục", message });
        }

        return Ok(new
        {
            title = "Đã gửi mã OTP đặt lại mật khẩu",
            message,
            devOtpCode
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyPasswordResetOtp([FromBody] ForgotPasswordVerifyOtpRequestDto request)
    {
        var (success, message) = await _authService.VerifyPasswordResetOtpAsync(
            request?.Email ?? string.Empty,
            request?.OtpCode ?? string.Empty);

        if (!success)
        {
            return BadRequest(new { title = "Mã OTP chưa chính xác", message });
        }

        return Ok(new { title = "Xác thực OTP thành công", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPasswordWithOtp([FromBody] ForgotPasswordResetRequestDto request)
    {
        var (success, message) = await _authService.ResetPasswordWithOtpAsync(request ?? new ForgotPasswordResetRequestDto());
        if (!success)
        {
            return BadRequest(new { title = "Đặt lại mật khẩu thất bại", message });
        }

        return Ok(new { title = "Đặt lại mật khẩu thành công", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> UploadAvatar(IFormFile file)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, avatarUrl, updatedClaims) = await _authService.UploadUserAvatarAsync(currentUserId, file);
        if (!success)
        {
            return BadRequest(new { message });
        }

        if (updatedClaims != null)
        {
            await SignInCookieAsync(updatedClaims, isPersistent: true);
        }

        return Ok(new { title = "Cập nhật ảnh đại diện thành công", message, avatarUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ResetAvatar()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, updatedClaims) = await _authService.ResetUserAvatarAsync(currentUserId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        if (updatedClaims != null)
        {
            await SignInCookieAsync(updatedClaims, isPersistent: true);
        }

        return Ok(new { title = "Đã đặt lại ảnh đại diện", message, avatarUrl = "/images/avatar-default.svg" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GoogleLogin(string? returnUrl = null)
    {
        return await StartGoogleChallengeAsync(returnUrl, mobileApp: false);
    }

    /// <summary>
    /// Luồng đăng nhập Google dành riêng cho Android Custom Tabs (tránh lỗi 405 GET và đồng bộ phiên về WebView qua mã 1 lần).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GoogleLoginMobile(string? returnUrl = null)
    {
        return await StartGoogleChallengeAsync(returnUrl, mobileApp: true);
    }

    private async Task<IActionResult> StartGoogleChallengeAsync(string? returnUrl, bool mobileApp)
    {
        if (AuthConfigHelper.IsGoogleOAuthConfigured(_config))
        {
            var redirectUrl = Url.Action(nameof(GoogleCallback), "S01AuthOtpGoogle", new { returnUrl, mobileApp });
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
    public async Task<IActionResult> GoogleCallback(string? returnUrl = null, bool mobileApp = false)
    {
        // Đọc thông tin xác thực từ External Cookie riêng (không bị OnValidatePrincipal chặn vì Google sub chưa phải AspNetUsers.Id)
        var result = await HttpContext.AuthenticateAsync(AuthConfigHelper.ExternalCookieScheme);
        await HttpContext.SignOutAsync(AuthConfigHelper.ExternalCookieScheme);

        if (!result.Succeeded || result.Principal == null)
        {
            const string errDetail = "Không nhận được thông tin định danh hợp lệ từ Google. Vui lòng thử lại.";
            if (mobileApp)
            {
                return RenderMobileOAuthBridgeHtml(
                    $"/S01AuthOtpGoogle/Index?oauthError={Uri.EscapeDataString(errDetail)}",
                    "Xác thực Google không thành công",
                    errDetail,
                    isError: true);
            }
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Xác thực Google không thành công";
            TempData["StatusDetail"] = errDetail;
            return RedirectToAction(nameof(Index));
        }

        var providerKey = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = result.Principal.FindFirstValue(ClaimTypes.Email);
        var fullName = result.Principal.FindFirstValue(ClaimTypes.Name) ?? "Thành viên BookShare";

        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(email))
        {
            const string errDetail = "Không thể xác minh địa chỉ Email từ tài khoản Google của bạn.";
            if (mobileApp)
            {
                return RenderMobileOAuthBridgeHtml(
                    $"/S01AuthOtpGoogle/Index?oauthError={Uri.EscapeDataString(errDetail)}",
                    "Thiếu thông tin tài khoản Google",
                    errDetail,
                    isError: true);
            }
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Thiếu thông tin tài khoản Google";
            TempData["StatusDetail"] = errDetail;
            return RedirectToAction(nameof(Index));
        }

        var upsert = await _authService.UpsertGoogleLoginAsync(providerKey, email, fullName);
        if (!upsert.Success || upsert.UserClaims == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (mobileApp)
            {
                return RenderMobileOAuthBridgeHtml(
                    $"/S01AuthOtpGoogle/Index?oauthError={Uri.EscapeDataString(upsert.Message)}",
                    "Từ chối đăng nhập",
                    upsert.Message,
                    isError: true);
            }
            TempData["StatusCode"] = 403;
            TempData["StatusTitle"] = "Từ chối đăng nhập";
            TempData["StatusDetail"] = upsert.Message;
            return RedirectToAction(nameof(Index));
        }

        if (mobileApp)
        {
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            var link = await _qrAuthService.CreateMobileLinkSessionAsync(
                upsert.UserClaims.UserId,
                "Ứng dụng BookShare Android (Google OAuth)",
                remoteIp);

            if (link.Success && link.Data != null && !string.IsNullOrWhiteSpace(link.Data.QrPayload))
            {
                var handoffPath = $"/S01AuthOtpGoogle/CompleteMobileOAuth?code={Uri.EscapeDataString(link.Data.QrPayload)}";
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    handoffPath += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
                }
                return RenderMobileOAuthBridgeHtml(
                    handoffPath,
                    $"Xin chào {upsert.UserClaims.FullName}!",
                    "Xác thực Google thành công. Đang tự động đưa bạn quay lại ứng dụng BookShare...",
                    isError: false);
            }
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

    /// <summary>
    /// Hoàn tất đăng nhập Google bên trong Android WebView bằng mã liên kết 1 lần (hiệu lực 60 giây, đã băm SHA-256).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CompleteMobileOAuth(string? code = null, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Mã xác nhận Google không hợp lệ";
            TempData["StatusDetail"] = "Không tìm thấy mã đồng bộ phiên đăng nhập cho ứng dụng di động.";
            return RedirectToAction(nameof(Index));
        }

        var (success, statusCode, message, userClaims) = await _qrAuthService.ClaimMobileLinkSessionAsync(
            new MobileClaimQrLoginRequestDto { QrPayload = code.Trim() });

        if (!success || userClaims == null)
        {
            TempData["StatusCode"] = statusCode;
            TempData["StatusTitle"] = "Đồng bộ đăng nhập Google thất bại";
            TempData["StatusDetail"] = message;
            return RedirectToAction(nameof(Index));
        }

        await SignInCookieAsync(userClaims, isPersistent: true);
        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = $"Xin chào {userClaims.FullName}";
        TempData["StatusDetail"] = "Đăng nhập Google trên ứng dụng BookShare thành công!";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "S02PersonalShelf");
    }

    private ContentResult RenderMobileOAuthBridgeHtml(string deepLinkPath, string heading, string detail, bool isError)
    {
        string deepLinkUrl = $"bookshare://open?path={Uri.EscapeDataString(deepLinkPath)}";
        string safeHeading = System.Net.WebUtility.HtmlEncode(heading);
        string safeDetail = System.Net.WebUtility.HtmlEncode(detail);
        string safeHref = System.Net.WebUtility.HtmlEncode(deepLinkUrl);
        string accent = isError ? "#dc2626" : "#2563eb";
        string badgeBg = isError ? "#fee2e2" : "#eff6ff";
        string iconText = isError ? "!" : "✓";

        string html = $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>{safeHeading} - BookShare</title>
    <style>
        * {{ box-sizing: border-box; }}
        body {{ margin:0; min-height:100vh; display:flex; align-items:center; justify-content:center; padding:24px; background:#f8fafc; color:#0f172a; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif; }}
        .card {{ max-width:390px; width:100%; background:#fff; border:1px solid #e2e8f0; border-radius:20px; padding:28px 24px; text-align:center; box-shadow:0 12px 32px rgba(15,23,42,.08); }}
        .icon {{ width:64px; height:64px; margin:0 auto 16px; border-radius:18px; background:{badgeBg}; color:{accent}; display:flex; align-items:center; justify-content:center; font-size:30px; font-weight:800; }}
        h1 {{ font-size:1.15rem; margin:0 0 8px; }}
        p {{ font-size:.9rem; color:#475569; line-height:1.5; margin:0 0 20px; }}
        a.btn {{ display:block; width:100%; text-decoration:none; border-radius:12px; background:{accent}; color:#fff; font-weight:700; font-size:.95rem; padding:12px 16px; }}
    </style>
</head>
<body>
    <main class=""card"">
        <div class=""icon"">{iconText}</div>
        <h1>{safeHeading}</h1>
        <p>{safeDetail}</p>
        <a id=""btnBackApp"" class=""btn"" href=""{safeHref}"">Quay lại ứng dụng BookShare</a>
    </main>
    <script>
        (function () {{
            var target = document.getElementById('btnBackApp').getAttribute('href');
            setTimeout(function () {{ window.location.href = target; }}, 250);
        }})();
    </script>
</body>
</html>";

        return Content(html, "text/html; charset=utf-8");
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

    [HttpGet]
    public async Task<IActionResult> GetUiPreferences()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Ok(new { authenticated = false, preferences = new UiPreferencesDto() });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var prefs = await _authService.GetUiPreferencesAsync(currentUserId);
        return Ok(new { authenticated = true, preferences = prefs });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUiPreferences([FromBody] UiPreferencesDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, saved) = await _authService.SaveUiPreferencesAsync(currentUserId, request ?? new UiPreferencesDto());
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { title = "Đã lưu cài đặt giao diện", message, preferences = saved });
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
