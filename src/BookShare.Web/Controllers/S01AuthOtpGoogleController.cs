using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S01AuthOtpGoogleController : Controller
{
    private readonly IS01AuthOtpService _authService;
    private readonly IResendEmailService _resendOtpService;
    private readonly IRecaptchaService _recaptchaService;
    private readonly IQrLoginService _qrLoginService;
    private readonly IConfiguration _config;

    public S01AuthOtpGoogleController(
        IS01AuthOtpService authService,
        IResendEmailService resendOtpService,
        IRecaptchaService recaptchaService,
        IQrLoginService qrLoginService,
        IConfiguration config)
    {
        _authService = authService;
        _resendOtpService = resendOtpService;
        _recaptchaService = recaptchaService;
        _qrLoginService = qrLoginService;
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
            TempData["StatusTitle"] = "Đăng nhập không thành công (Mã 400)";
            TempData["StatusDetail"] = result.Message;
            return RedirectToAction(nameof(Index), new { returnUrl = input.ReturnUrl });
        }

        await SignInCookieAsync(result.UserClaims, input.RememberMe);

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = $"Xin chào {result.UserClaims.FullName} (Vai trò: {result.UserClaims.PrimaryRole})";
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
            TempData["StatusTitle"] = "Chặn đăng ký tự động (reCAPTCHA v3)";
            TempData["StatusDetail"] = captchaMsg;
            return RedirectToAction(nameof(Index));
        }

        var result = await _authService.RegisterUserAsync(input);
        if (!result.Success || result.UserClaims == null)
        {
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Đăng ký tài khoản thất bại (Mã 400)";
            TempData["StatusDetail"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        await SignInCookieAsync(result.UserClaims, isPersistent: true);

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đăng ký & Đăng nhập thành công (Mã 200)";
        TempData["StatusDetail"] = result.Message;
        return RedirectToAction("Index", "S02PersonalShelf");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã đăng xuất an toàn (Mã 200)";
        TempData["StatusDetail"] = "Phiên làm việc Cookie Authentication đã được đóng.";
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
        var clientId = _config["Authentication:Google:ClientId"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")
            ?? string.Empty;
        var clientSecret = _config["Authentication:Google:ClientSecret"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET")
            ?? string.Empty;
        // Nếu người dùng đã điền Google ClientId thật trong .env thì chuyển hướng OAuth 2.0 sang Google
        if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret) && !clientId.StartsWith("demo-google-client-id") && !clientId.StartsWith("xxxx"))
        {
            var redirectUrl = Url.Action(nameof(GoogleCallback), "S01AuthOtpGoogle", new { returnUrl });
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        // Chế độ Sandbox Dev khi chưa điền ClientId thật: nối trực tiếp vào bảng AspNetUserLogins để kiểm thử luồng
        var demoResult = await _authService.UpsertGoogleLoginAsync(
            providerKey: "google-oauth2-sub-1029384756",
            email: "nguyenhung@cmc.edu.vn",
            fullName: "Nguyễn Hùng (Google OAuth)");

        if (demoResult.Success && demoResult.UserClaims != null)
        {
            await SignInCookieAsync(demoResult.UserClaims, isPersistent: true);
            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đăng nhập Google OAuth thành công (Chế độ Dev Sandbox)";
            TempData["StatusDetail"] = "Đã ghi nhận liên kết vào bảng AspNetUserLogins. Điền Authentication__Google__ClientId thật vào .env để bật màn hình Google Consent.";
        }

        return RedirectToAction("Index", "S02PersonalShelf");
    }

    [HttpGet]
    public async Task<IActionResult> GoogleCallback(string? returnUrl = null)
    {
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal == null)
        {
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Xác thực Google OAuth không thành công";
            TempData["StatusDetail"] = "Không nhận được thông tin định danh từ Google.";
            return RedirectToAction(nameof(Index));
        }

        var providerKey = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.NewGuid().ToString("N");
        var email = result.Principal.FindFirstValue(ClaimTypes.Email) ?? "googleuser@bookshare.vn";
        var fullName = result.Principal.FindFirstValue(ClaimTypes.Name) ?? "Google User";

        var upsert = await _authService.UpsertGoogleLoginAsync(providerKey, email, fullName);
        if (upsert.Success && upsert.UserClaims != null)
        {
            await SignInCookieAsync(upsert.UserClaims, isPersistent: true);
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "S02PersonalShelf");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
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
        TempData["StatusTitle"] = "Đã cập nhật Hồ sơ Cá nhân";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Cập nhật Hồ sơ Cá nhân thành công", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-admin-01";
        var (success, message) = await _authService.ChangeUserPasswordAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { title = "Đổi mật khẩu thành công", message });
    }

    // [BS-QRLOGIN-BRIDGE] Cấp Cookie Auth 1 lần duy nhất cho trình duyệt PC sau khi Mobile App quét duyệt QR
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ClaimQrLogin([FromBody] QrLoginTokenRequestDto? request, [FromQuery] string? token = null)
    {
        string effectiveToken = !string.IsNullOrWhiteSpace(request?.Token) ? request.Token : (token ?? string.Empty);
        var (success, message, userClaims) = await _qrLoginService.ClaimSessionAsync(effectiveToken);
        if (!success || userClaims == null)
        {
            return BadRequest(new { message });
        }

        await SignInCookieAsync(userClaims, isPersistent: true);

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = $"Đăng nhập QR thành công: {userClaims.FullName} ({userClaims.PrimaryRole})";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đăng nhập chéo QR thành công",
            message,
            fullName = userClaims.FullName,
            role = userClaims.PrimaryRole,
            redirectUrl = Url.Action("Index", "S02PersonalShelf") ?? "/S02PersonalShelf"
        });
    }

    private async Task SignInCookieAsync(AuthUserClaimsDto userClaims, bool isPersistent)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userClaims.UserId),
            new(ClaimTypes.Email, userClaims.Email),
            new(ClaimTypes.Name, userClaims.FullName),
            new(ClaimTypes.Role, userClaims.PrimaryRole),
            new("TrustScore", userClaims.TrustScore.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });
    }
}
