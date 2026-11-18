using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _environment;

    public S01AuthOtpGoogleController(
        IS01AuthOtpService authService,
        IResendEmailService resendOtpService,
        IConfiguration config,
        IHostEnvironment environment)
    {
        _authService = authService;
        _resendOtpService = resendOtpService;
        _config = config;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? returnUrl = null)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var vm = await _authService.GetAuthPageViewModelAsync(currentUserId, returnUrl);
        if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Testing"))
        {
            vm.DemoAccounts.Clear();
        }
        return View("~/Views/Modules/S01_AuthOtpGoogle/Index.cshtml", vm);
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        TempData["StatusCode"] = 403;
        TempData["StatusTitle"] = "Bạn không có quyền truy cập chức năng này (Mã 403)";
        TempData["StatusDetail"] = "Hệ thống đã từ chối yêu cầu theo vai trò tài khoản. Hãy đăng nhập bằng tài khoản có quyền phù hợp.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInputModel input)
    {
        if (!ModelState.IsValid)
        {
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Dữ liệu đăng nhập không hợp lệ (Mã 400)";
            TempData["StatusDetail"] = GetModelErrorMessage();
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
        if (!ModelState.IsValid)
        {
            TempData["StatusCode"] = 400;
            TempData["StatusTitle"] = "Dữ liệu đăng ký không hợp lệ (Mã 400)";
            TempData["StatusDetail"] = GetModelErrorMessage();
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
        if (!ModelState.IsValid)
        {
            return BadRequest(new { title = "Dữ liệu OTP không hợp lệ", message = GetModelErrorMessage() });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? otpUserId = currentUserId;
        if (request.Purpose == "Login")
        {
            var targetUser = await _authService.GetUserClaimsForOtpLoginAsync(request.Email);
            if (targetUser == null)
            {
                return BadRequest(new { title = "Không tìm thấy tài khoản", message = "Email chưa được đăng ký trong BookShare. Vui lòng kiểm tra lại." });
            }
            otpUserId = targetUser.UserId;
        }

        var (success, message, devOtpCode) = await _resendOtpService.GenerateAndSendOtpAsync(
            request.Email,
            request.Purpose,
            otpUserId);

        if (!success)
        {
            return BadRequest(new { title = "Không thể gửi OTP", message });
        }

        return Ok(new
        {
            title = "Đã gửi mã OTP 6 chữ số (S01 Resend)",
            message,
            devOtpCode
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { title = "Dữ liệu OTP không hợp lệ", message = GetModelErrorMessage() });
        }

        var (success, message) = await _resendOtpService.VerifyOtpAsync(
            request.Email,
            request.OtpCode,
            request.Purpose);

        if (!success)
        {
            return BadRequest(new { title = "Xác thực OTP thất bại", message });
        }

        if (request.Purpose == "Login")
        {
            var userClaims = await _authService.GetUserClaimsForOtpLoginAsync(request.Email);
            if (userClaims == null)
            {
                return BadRequest(new { title = "Không thể đăng nhập", message = "Tài khoản không còn tồn tại hoặc đã bị vô hiệu hóa." });
            }

            await SignInCookieAsync(userClaims, isPersistent: true);
            return Ok(new
            {
                title = "Đăng nhập bằng OTP thành công (Mã 200)",
                message = $"Xin chào {userClaims.FullName}. Phiên đăng nhập đã được tạo sau khi xác thực OTP.",
                redirectUrl = Url.Action("Index", "S02PersonalShelf")
            });
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
        var clientId = _config["Authentication:Google:ClientId"] ?? string.Empty;
        // Nếu người dùng đã điền Google ClientId thật trong .env thì chuyển hướng OAuth 2.0 sang Google
        if (!string.IsNullOrWhiteSpace(clientId) && !clientId.StartsWith("demo-google-client-id") && !clientId.StartsWith("xxxx"))
        {
            var redirectUrl = Url.Action(nameof(GoogleCallback), "S01AuthOtpGoogle", new { returnUrl });
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        // Sandbox Google chỉ được bật trong Development; production không được đăng nhập bằng danh tính demo.
        if (!_environment.IsDevelopment())
        {
            TempData["StatusCode"] = 503;
            TempData["StatusTitle"] = "Google OAuth chưa được cấu hình (Mã 503)";
            TempData["StatusDetail"] = "Quản trị viên cần cấu hình Google Client ID và Client Secret trước khi bật đăng nhập Google.";
            return RedirectToAction(nameof(Index));
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

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { title = "Thông tin hồ sơ chưa hợp lệ", message = GetModelErrorMessage() });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(currentUserId)) return Forbid();

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
        TempData["StatusTitle"] = "Đã cập nhật Hồ sơ Cá nhân (S01)";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Cập nhật Hồ sơ Cá nhân thành công (S01)", message });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { title = "Dữ liệu đổi mật khẩu chưa hợp lệ", message = GetModelErrorMessage() });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(currentUserId)) return Forbid();

        var (success, message) = await _authService.ChangeUserPasswordAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new { title = "Đổi mật khẩu thành công (S01)", message });
    }

    private string GetModelErrorMessage()
    {
        var errors = ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => error.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct();
        return string.Join(" ", errors);
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
