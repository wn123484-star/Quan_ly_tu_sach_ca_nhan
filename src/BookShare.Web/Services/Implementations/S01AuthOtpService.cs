using System.Text.RegularExpressions;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [S01] Dịch vụ triển khai toàn bộ nghiệp vụ Tài khoản & Xác thực: đăng nhập/đăng ký, hash mật khẩu ASP.NET Identity, Google OAuth 2.0, OTP quên mật khẩu, cập nhật hồ sơ/avatar và lưu cấu hình giao diện.
/// </summary>
/// <remarks>Bảo mật: Khóa tạm khi sai OTP 5 lần, kiểm tra định dạng Email/SĐT Việt Nam/URL ảnh an toàn, tách bạch tên hiển thị với Email.</remarks>
public class S01AuthOtpService : IS01AuthOtpService
{
    private const long MaxAvatarSizeBytes = 2 * 1024 * 1024; // 2 MB
    private static readonly string[] AllowedAvatarExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

    private readonly BookShareDbContext _db;
    private readonly IConfiguration _config;
    private readonly IDatabaseBootstrapService _bootstrap;
    private readonly IResendEmailService _resendEmailService;
    private readonly IWebHostEnvironment _env;
    private readonly IAntiSpamRateLimiterService _rateLimiter;
    private readonly PasswordHasher<AspNetUser> _passwordHasher = new();

    public S01AuthOtpService(
        BookShareDbContext db,
        IConfiguration config,
        IDatabaseBootstrapService bootstrap,
        IResendEmailService resendEmailService,
        IWebHostEnvironment env,
        IAntiSpamRateLimiterService rateLimiter)
    {
        _db = db;
        _config = config;
        _bootstrap = bootstrap;
        _resendEmailService = resendEmailService;
        _env = env;
        _rateLimiter = rateLimiter;
    }

    private Task EnsureCoreAdminBootstrapAsync() => _bootstrap.EnsureInitializedAsync();

    public async Task<S01AuthPageViewModel> GetAuthPageViewModelAsync(string? currentUserId, string? returnUrl = null)
    {
        await EnsureCoreAdminBootstrapAsync();

        var vm = new S01AuthPageViewModel
        {
            ReturnUrl = returnUrl,
            IsGoogleOAuthConfigured = AuthConfigHelper.IsGoogleOAuthConfigured(_config)
        };

        if (!string.IsNullOrWhiteSpace(currentUserId))
        {
            var user = await _db.AspNetUsers
                .Include(u => u.Roles)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == currentUserId);

            if (user != null)
            {
                vm.IsAuthenticated = true;
                vm.CurrentUserId = user.Id;
                vm.CurrentUserName = user.UserName ?? user.Email ?? user.Id;
                vm.CurrentUserFullName = user.FullName;
                vm.CurrentUserEmail = user.Email ?? string.Empty;
                vm.EmailConfirmed = user.EmailConfirmed;
                vm.HasValidEmail = AuthConfigHelper.IsValidEmailFormat(user.Email);
                vm.UiPreferencesJson = user.UiPreferencesJson;
                vm.CurrentUserPhoneNumber = user.PhoneNumber ?? string.Empty;
                vm.CurrentUserAddress = user.Address ?? string.Empty;
                vm.CurrentUserAvatarUrl = string.IsNullOrWhiteSpace(user.AvatarUrl)
                    ? "/images/avatar-default.svg"
                    : user.AvatarUrl;
                vm.CurrentUserRole = user.Roles.FirstOrDefault()?.Name ?? "User";
                vm.CurrentTrustScore = user.TrustScore;
                vm.TwoFactorEnabled = user.TwoFactorEnabled;

                var latestCuratorReq = await _db.RoleUpgradeRequests
                    .AsNoTracking()
                    .Where(r => r.UserId == user.Id && r.RequestedRole == "Curator")
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync();
                vm.PendingCuratorStatus = latestCuratorReq?.Status;
                vm.LatestCuratorNote = latestCuratorReq?.ReviewNote;
            }
        }

        return vm;
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UpdatedClaims)> UpdateUserProfileAsync(
        string userId,
        UpdateProfileRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return (false, "Tên hiển thị không được để trống.", null);
        }

        string cleanFullName = request.FullName.Trim();
        if (cleanFullName.Length < 2 || cleanFullName.Length > 150)
        {
            return (false, "Tên hiển thị phải từ 2 đến 150 ký tự.", null);
        }

        if (AuthConfigHelper.IsValidEmailFormat(cleanFullName))
        {
            return (false, "Tên hiển thị cần tách biệt với địa chỉ Email. Vui lòng nhập tên gọi hiển thị của bạn và điền Email ở ô Email riêng.", null);
        }

        if (!AuthConfigHelper.IsValidVietnamPhone(request.PhoneNumber))
        {
            return (false, "Số điện thoại chưa đúng định dạng (cần 9–11 chữ số, bắt đầu bằng 0 hoặc +84).", null);
        }

        if (!string.IsNullOrWhiteSpace(request.Address) && request.Address.Trim().Length > 250)
        {
            return (false, "Địa chỉ liên hệ không được vượt quá 250 ký tự.", null);
        }

        if (!AuthConfigHelper.IsSafeImageUrl(request.AvatarUrl))
        {
            return (false, "Đường dẫn ảnh đại diện không hợp lệ hoặc không an toàn.", null);
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return (false, "Không tìm thấy thông tin hồ sơ người dùng.", null);
        }

        bool emailChanged = false;
        if (request.Email != null)
        {
            string trimmedEmail = request.Email.Trim();
            if (string.IsNullOrEmpty(trimmedEmail))
            {
                user.Email = null;
                user.NormalizedEmail = null;
                user.EmailConfirmed = false;
                emailChanged = true;
            }
            else
            {
                if (!AuthConfigHelper.IsValidEmailFormat(trimmedEmail))
                {
                    return (false, "Địa chỉ Email nhận thông báo chưa đúng định dạng hợp lệ.", null);
                }

                string normalizedNewEmail = trimmedEmail.ToUpperInvariant();
                bool duplicateEmail = await _db.AspNetUsers
                    .AsNoTracking()
                    .AnyAsync(u => u.Id != userId && u.NormalizedEmail == normalizedNewEmail);
                if (duplicateEmail)
                {
                    return (false, "Địa chỉ Email này đã được gắn cho một tài khoản khác trên hệ thống.", null);
                }

                if (!string.Equals(user.NormalizedEmail, normalizedNewEmail, StringComparison.OrdinalIgnoreCase))
                {
                    user.Email = trimmedEmail;
                    user.NormalizedEmail = normalizedNewEmail;
                    user.EmailConfirmed = false;
                    emailChanged = true;
                }
            }
        }

        user.FullName = cleanFullName;
        user.PhoneNumber = request.PhoneNumber?.Trim();
        user.Address = request.Address?.Trim();
        if (!string.IsNullOrWhiteSpace(request.AvatarUrl))
        {
            user.AvatarUrl = request.AvatarUrl.Trim();
        }

        await _db.SaveChangesAsync();

        var roleName = user.Roles.FirstOrDefault()?.Name ?? "User";
        var claims = new AuthUserClaimsDto
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            PrimaryRole = roleName,
            TrustScore = user.TrustScore,
            AvatarUrl = user.AvatarUrl
        };

        string note = emailChanged
            ? (string.IsNullOrWhiteSpace(user.Email)
                ? $"Đã cập nhật tên hiển thị \"{user.FullName}\" (chưa gắn Email nhận thư)."
                : $"Đã cập nhật tên hiển thị \"{user.FullName}\" và Email nhận thư mới ({user.Email}). Bạn có thể xác minh OTP Email trong Trung tâm Thông báo!")
            : $"Đã cập nhật hồ sơ cá nhân của {user.FullName} thành công.";

        return (true, note, claims);
    }

    public async Task<UiPreferencesDto> GetUiPreferencesAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new UiPreferencesDto();
        }

        var raw = await _db.AspNetUsers
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.UiPreferencesJson)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new UiPreferencesDto();
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<UiPreferencesDto>(
                raw,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return SanitizeUiPreferences(parsed);
        }
        catch
        {
            return new UiPreferencesDto();
        }
    }

    public async Task<(bool Success, string Message, UiPreferencesDto Saved)> SaveUiPreferencesAsync(
        string userId,
        UiPreferencesDto request)
    {
        var clean = SanitizeUiPreferences(request);
        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản để lưu cài đặt giao diện.", clean);
        }

        user.UiPreferencesJson = System.Text.Json.JsonSerializer.Serialize(clean);
        await _db.SaveChangesAsync();
        return (true, "Đã lưu cài đặt giao diện, phong cách, phông chữ và cỡ chữ vào tài khoản của bạn!", clean);
    }

    private static UiPreferencesDto SanitizeUiPreferences(UiPreferencesDto? raw)
    {
        var allowedStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "standard", "sketchy", "flatly", "morph", "cyborg", "paper", "neobrutal", "pastel", "terminal"
        };
        var allowedPalettes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "blue", "ocean", "emerald", "gold", "rose", "lavender", "sepia", "coral", "royal", "midnight", "mint", "sakura", "obsidian"
        };
        var allowedBackgrounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "solid", "paper-grain", "dot-grid", "graph-grid", "notebook-lines",
            "soft-aurora", "sunset-glow", "botanical", "starry-night",
            "warm-linen", "pastel-clouds", "geometric-prism"
        };
        var allowedFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "roboto", "bevietnam", "lora", "patrick", "jetbrains" };
        var allowedSizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "xs", "sm", "md", "lg", "xl", "2xl", "3xl" };

        var t = raw?.Theme?.Trim().ToLowerInvariant() == "dark" ? "dark" : "light";
        var st = raw?.Style?.Trim().ToLowerInvariant() ?? "standard";
        var pl = raw?.Palette?.Trim().ToLowerInvariant() ?? "blue";
        var bg = raw?.Background?.Trim().ToLowerInvariant() ?? "solid";
        var fn = raw?.Font?.Trim().ToLowerInvariant() ?? "roboto";
        var sz = raw?.FontSize?.Trim().ToLowerInvariant() ?? "md";

        return new UiPreferencesDto
        {
            Theme = t,
            Style = allowedStyles.Contains(st) ? st : "standard",
            Palette = allowedPalettes.Contains(pl) ? pl : "blue",
            Background = allowedBackgrounds.Contains(bg) ? bg : "solid",
            Font = allowedFonts.Contains(fn) ? fn : "roboto",
            FontSize = allowedSizes.Contains(sz) ? sz : "md"
        };
    }

    public async Task<(bool Success, string Message, string? DevOtpCode)> RequestPasswordResetOtpAsync(string email)
    {
        if (!AuthConfigHelper.IsValidEmailFormat(email))
        {
            return (false, "Vui lòng nhập địa chỉ Email đã đăng ký hợp lệ.", null);
        }

        await EnsureCoreAdminBootstrapAsync();

        string normalizedUpper = email.Trim().ToUpperInvariant();
        var user = await _db.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedUpper);

        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản nào được đăng ký với địa chỉ Email này.", null);
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, "Tài khoản này hiện đang bị tạm khóa. Vui lòng liên hệ Quản trị viên.", null);
        }

        return await _resendEmailService.GenerateAndSendOtpAsync(user.Email ?? email.Trim(), "ResetPassword", user.Id);
    }

    public async Task<(bool Success, string Message)> VerifyPasswordResetOtpAsync(string email, string otpCode)
    {
        if (!AuthConfigHelper.IsValidEmailFormat(email) || string.IsNullOrWhiteSpace(otpCode))
        {
            return (false, "Vui lòng nhập đầy đủ Email và mã OTP 6 chữ số.");
        }

        string normalizedLower = email.Trim().ToLowerInvariant();
        if (_rateLimiter.IsLockedOut("reset-otp-verify", normalizedLower, maxFailures: 5, TimeSpan.FromMinutes(10), out int retrySec))
        {
            throw new RateLimitExceededException(
                $"Bạn đã nhập sai mã OTP khôi phục quá 5 lần liên tiếp. Vui lòng thử lại sau {retrySec} giây hoặc gửi mã OTP mới.",
                retrySec);
        }

        string cleanCode = otpCode.Trim();
        if (cleanCode.Length != 6 || !cleanCode.All(char.IsDigit))
        {
            return (false, "Mã OTP chưa chính xác. Vui lòng nhập đủ 6 chữ số.");
        }

        string codeHash = AuthConfigHelper.ComputeSha256Hex(cleanCode);
        var record = await _db.OtpVerifications
            .Where(o => o.Email == normalizedLower && o.Purpose == "ResetPassword" && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (record == null)
        {
            return (false, "Chưa có yêu cầu đặt lại mật khẩu nào đang chờ cho Email này. Vui lòng bấm gửi mã OTP trước.");
        }

        if (record.ExpiresAt < DateTime.UtcNow)
        {
            return (false, "Mã OTP đã hết hạn (quá 5 phút). Vui lòng bấm gửi lại mã mới.");
        }

        if (!string.Equals(record.OtpCodeHash, codeHash, StringComparison.OrdinalIgnoreCase))
        {
            var (fails, lockedOut, _) = _rateLimiter.RecordFailure("reset-otp-verify", normalizedLower, maxFailures: 5, TimeSpan.FromMinutes(10));
            if (lockedOut)
            {
                record.IsUsed = true;
                await _db.SaveChangesAsync();
                return (false, "Bạn đã nhập sai mã OTP quá 5 lần liên tiếp. Mã OTP cũ đã bị hủy để bảo mật — vui lòng gửi mã mới.");
            }
            return (false, $"Mã OTP nhập chưa chính xác ({fails}/5 lần thử). Vui lòng kiểm tra lại hộp thư.");
        }

        _rateLimiter.ClearFailures("reset-otp-verify", normalizedLower);
        return (true, "Xác thực OTP thành công! Mời bạn nhập mật khẩu mới bên dưới.");
    }

    public async Task<(bool Success, string Message)> ResetPasswordWithOtpAsync(ForgotPasswordResetRequestDto request)
    {
        if (request == null || !AuthConfigHelper.IsValidEmailFormat(request.Email))
        {
            return (false, "Địa chỉ Email không hợp lệ.");
        }

        string normalizedLower = request.Email.Trim().ToLowerInvariant();
        if (_rateLimiter.IsLockedOut("reset-otp-verify", normalizedLower, maxFailures: 5, TimeSpan.FromMinutes(10), out int retrySec))
        {
            throw new RateLimitExceededException(
                $"Tài khoản đang tạm khóa xác thực OTP do nhập sai quá nhiều lần. Vui lòng thử lại sau {retrySec} giây.",
                retrySec);
        }

        if (string.IsNullOrWhiteSpace(request.OtpCode) || request.OtpCode.Trim().Length != 6)
        {
            return (false, "Mã OTP 6 chữ số không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            return (false, "Mật khẩu mới phải có tối thiểu 6 ký tự.");
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return (false, "Mật khẩu nhập lại không khớp với mật khẩu mới. Vui lòng kiểm tra lại.");
        }

        string normalizedUpper = normalizedLower.ToUpperInvariant();
        string codeHash = AuthConfigHelper.ComputeSha256Hex(request.OtpCode.Trim());

        var record = await _db.OtpVerifications
            .Where(o => o.Email == normalizedLower && o.Purpose == "ResetPassword" && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (record == null || record.ExpiresAt < DateTime.UtcNow ||
            !string.Equals(record.OtpCodeHash, codeHash, StringComparison.OrdinalIgnoreCase))
        {
            var (_, lockedOut, _) = _rateLimiter.RecordFailure("reset-otp-verify", normalizedLower, maxFailures: 5, TimeSpan.FromMinutes(10));
            if (lockedOut && record != null)
            {
                record.IsUsed = true;
                await _db.SaveChangesAsync();
            }
            return (false, "Mã OTP chưa chính xác hoặc đã hết hạn. Vui lòng gửi lại mã mới.");
        }

        _rateLimiter.ClearFailures("reset-otp-verify", normalizedLower);

        var user = !string.IsNullOrWhiteSpace(record.UserId)
            ? await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == record.UserId)
            : await _db.AspNetUsers.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedUpper);

        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản tương ứng với Email này.");
        }

        record.IsUsed = true;
        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.EmailConfirmed = true;

        _db.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Title = "Đặt lại mật khẩu thành công qua OTP",
            Content = $"Mật khẩu tài khoản BookShare của bạn vừa được đặt lại lúc {DateTime.UtcNow.AddHours(7):HH:mm dd/MM/yyyy}. Nếu không phải bạn thực hiện, hãy đổi mật khẩu ngay.",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return (true, "Đặt lại mật khẩu mới thành công! Bạn có thể đăng nhập ngay bằng mật khẩu mới.");
    }

    public async Task<(bool Success, string Message, string? AvatarUrl, AuthUserClaimsDto? UpdatedClaims)> UploadUserAvatarAsync(
        string userId,
        IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return (false, "Vui lòng chọn một tệp ảnh hợp lệ để làm ảnh đại diện.", null, null);
        }

        if (file.Length > MaxAvatarSizeBytes)
        {
            return (false, "Ảnh đại diện không được vượt quá 2MB.", null, null);
        }

        string ext = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        if (!AllowedAvatarExtensions.Contains(ext))
        {
            return (false, "Chỉ chấp nhận tệp ảnh định dạng .png, .jpg, .jpeg hoặc .webp.", null, null);
        }

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        if (!IsValidAvatarBytes(ext, bytes))
        {
            return (false, "Nội dung tệp tải lên không đúng định dạng ảnh hợp lệ.", null, null);
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản người dùng.", null, null);
        }

        string webRoot = string.IsNullOrWhiteSpace(_env.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : _env.WebRootPath;
        string avatarsDir = Path.Combine(webRoot, "uploads", "avatars");
        Directory.CreateDirectory(avatarsDir);

        string safeUid = Regex.Replace(userId.ToLowerInvariant(), @"[^a-z0-9_-]", "");
        if (string.IsNullOrWhiteSpace(safeUid)) safeUid = "member";
        string fileName = $"avt_{safeUid}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
        string fullPath = Path.Combine(avatarsDir, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes);

        string? oldAvatar = user.AvatarUrl;
        string publicUrl = $"/uploads/avatars/{fileName}";
        user.AvatarUrl = publicUrl;
        await _db.SaveChangesAsync();

        TryDeleteLocalAvatar(webRoot, oldAvatar);

        var claims = BuildClaimsDto(user);
        return (true, "Đã cập nhật ảnh đại diện mới thành công!", publicUrl, claims);
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UpdatedClaims)> ResetUserAvatarAsync(string userId)
    {
        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản người dùng.", null);
        }

        string webRoot = string.IsNullOrWhiteSpace(_env.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : _env.WebRootPath;
        TryDeleteLocalAvatar(webRoot, user.AvatarUrl);

        user.AvatarUrl = null;
        await _db.SaveChangesAsync();

        return (true, "Đã đặt lại ảnh đại diện về mặc định hệ thống.", BuildClaimsDto(user));
    }

    public async Task<AuthUserClaimsDto?> GetUserClaimsByIdAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);
        return user == null ? null : BuildClaimsDto(user);
    }

    private static AuthUserClaimsDto BuildClaimsDto(AspNetUser user) => new()
    {
        UserId = user.Id,
        Email = user.Email ?? string.Empty,
        FullName = user.FullName,
        PrimaryRole = user.Roles.FirstOrDefault()?.Name ?? "User",
        TrustScore = user.TrustScore,
        AvatarUrl = user.AvatarUrl
    };

    private static bool IsValidAvatarBytes(string ext, byte[] bytes)
    {
        if (bytes.Length < 12) return false;
        if (ext == ".png")
        {
            return bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
        }
        if (ext is ".jpg" or ".jpeg")
        {
            return bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        }
        if (ext == ".webp")
        {
            return bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
                && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
        }
        return false;
    }

    private static void TryDeleteLocalAvatar(string webRoot, string? oldUrl)
    {
        if (string.IsNullOrWhiteSpace(oldUrl) || !oldUrl.StartsWith("/uploads/avatars/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        try
        {
            string fileName = Path.GetFileName(oldUrl);
            if (string.IsNullOrWhiteSpace(fileName)) return;
            string fullPath = Path.Combine(webRoot, "uploads", "avatars", fileName);
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
        catch { /* bỏ qua */ }
    }

    public async Task<(bool Success, string Message)> ChangeUserPasswordAsync(
        string userId,
        ChangePasswordRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return (false, "Vui lòng nhập đầy đủ mật khẩu hiện tại và mật khẩu mới.");
        }

        if (request.NewPassword.Length < 6)
        {
            return (false, "Mật khẩu mới phải có tối thiểu 6 ký tự.");
        }

        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản người dùng.");
        }

        var currentValid = VerifyUserPassword(user, request.CurrentPassword, out _);
        if (!currentValid)
        {
            return (false, "Mật khẩu hiện tại không chính xác.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync();

        return (true, $"Đã đổi mật khẩu thành công cho tài khoản {user.Email}.");
    }

    public async Task<(bool Success, string Message)> SubmitCuratorRequestAsync(
        string userId,
        SubmitCuratorRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationName) || string.IsNullOrWhiteSpace(request.Reason))
        {
            return (false, "Vui lòng nhập đầy đủ Tên Câu lạc bộ / Điểm trạm và Lý do đăng ký điều hành.");
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return (false, "Không tìm thấy hồ sơ thành viên.");
        }

        var currentRole = user.Roles.FirstOrDefault()?.Name ?? "User";
        if (currentRole != "User")
        {
            return (false, $"Tài khoản của bạn hiện đang giữ vai trò {currentRole}, không cần đăng ký nâng quyền thêm.");
        }

        bool hasPending = await _db.RoleUpgradeRequests.AnyAsync(r =>
            r.UserId == userId && r.RequestedRole == "Curator" && r.Status == "Pending");
        if (hasPending)
        {
            return (false, "Bạn đã có một đề nghị trở thành Chủ nhiệm CLB đang chờ Quản trị viên xét duyệt.");
        }

        var upgradeReq = new RoleUpgradeRequest
        {
            UserId = userId,
            RequestedRole = "Curator",
            OrganizationName = request.OrganizationName.Trim(),
            Reason = request.Reason.Trim(),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        _db.RoleUpgradeRequests.Add(upgradeReq);

        var adminUserIds = await _db.AspNetUsers
            .Where(u => u.Roles.Any(r => r.Name == "Admin"))
            .Select(u => u.Id)
            .Take(5)
            .ToListAsync();

        foreach (var adminId in adminUserIds)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = adminId,
                Title = $"Đề nghị nâng quyền Chủ nhiệm CLB mới từ {user.FullName}",
                Content = $"Thành viên {user.FullName} ({user.Email}) đề nghị phụ trách điểm trạm \"{upgradeReq.OrganizationName}\". Mời Quản trị viên xem xét phê duyệt tại trang Quản trị.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return (true, $"Đã gửi đề nghị trở thành Chủ nhiệm CLB cho đơn vị \"{upgradeReq.OrganizationName}\". Quản trị viên sẽ xem xét và phản hồi qua thông báo.");
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> AuthenticateAsync(
        string email,
        string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "Vui lòng nhập đầy đủ Email và Mật khẩu.", null);
        }

        await EnsureCoreAdminBootstrapAsync();

        var normalizedEmail = email.Trim().ToUpperInvariant();
        if (normalizedEmail == "ADMIN")
        {
            normalizedEmail = "ADMIN@BOOKSHARE.VN";
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail || u.NormalizedUserName == normalizedEmail);

        if (user == null)
        {
            return (false, "Email hoặc mật khẩu không chính xác.", null);
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, "Tài khoản của bạn hiện đang bị tạm khóa. Vui lòng liên hệ Quản trị viên để được hỗ trợ.", null);
        }

        bool passwordValid = VerifyUserPassword(user, password, out bool needsRehash);
        if (!passwordValid)
        {
            return (false, "Email hoặc mật khẩu không chính xác.", null);
        }

        if (needsRehash)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
            await _db.SaveChangesAsync();
        }

        var primaryRole = user.Roles.FirstOrDefault()?.Name ?? "User";
        var claimsDto = new AuthUserClaimsDto
        {
            UserId = user.Id,
            Email = user.Email ?? email.Trim(),
            FullName = user.FullName,
            PrimaryRole = primaryRole,
            TrustScore = user.TrustScore,
            AvatarUrl = user.AvatarUrl
        };

        return (true, $"Đăng nhập thành công! Chào mừng {user.FullName}.", claimsDto);
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> RegisterUserAsync(
        RegisterInputModel input)
    {
        if (string.IsNullOrWhiteSpace(input.FullName) || string.IsNullOrWhiteSpace(input.Email) || string.IsNullOrWhiteSpace(input.Password))
        {
            return (false, "Vui lòng điền đầy đủ Họ tên, Email và Mật khẩu.", null);
        }

        var cleanRegName = input.FullName.Trim();
        if (cleanRegName.Length < 2 || cleanRegName.Length > 150)
        {
            return (false, "Họ và tên phải có độ dài từ 2 đến 150 ký tự.", null);
        }

        if (AuthConfigHelper.IsValidEmailFormat(cleanRegName))
        {
            return (false, "Họ và tên hiển thị không được dùng địa chỉ Email.", null);
        }

        if (!AuthConfigHelper.IsValidEmailFormat(input.Email))
        {
            return (false, "Địa chỉ Email đăng ký không đúng định dạng hợp lệ.", null);
        }

        if (input.Password.Length < 6 || input.Password.Length > 128)
        {
            return (false, "Mật khẩu đăng ký phải có độ dài từ 6 đến 128 ký tự.", null);
        }

        if (!string.IsNullOrWhiteSpace(input.Address) && input.Address.Trim().Length > 250)
        {
            return (false, "Địa chỉ khu vực không được vượt quá 250 ký tự.", null);
        }

        await EnsureCoreAdminBootstrapAsync();

        var normalizedEmail = input.Email.Trim().ToUpperInvariant();
        bool exists = await _db.AspNetUsers.AnyAsync(u => u.NormalizedEmail == normalizedEmail || u.NormalizedUserName == normalizedEmail);
        if (exists)
        {
            return (false, "Địa chỉ Email này đã được đăng ký trên hệ thống.", null);
        }

        // Đăng ký công khai luôn luôn gán vai trò thành viên tiêu chuẩn (User)
        const string roleName = "User";
        var roleEntity = await _db.AspNetRoles.FirstOrDefaultAsync(r => r.Name == roleName);

        var newUser = new AspNetUser
        {
            Id = $"u-{Guid.NewGuid():N}"[..16],
            UserName = input.Email.Trim(),
            NormalizedUserName = normalizedEmail,
            Email = input.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            LockoutEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            FullName = input.FullName.Trim(),
            Address = string.IsNullOrWhiteSpace(input.Address) ? "Hà Nội" : input.Address.Trim(),
            TrustScore = 100,
            LendQuotaBonus = 0,
            CreatedAt = DateTime.UtcNow
        };
        newUser.PasswordHash = _passwordHasher.HashPassword(newUser, input.Password);

        if (roleEntity != null)
        {
            newUser.Roles.Add(roleEntity);
        }

        _db.AspNetUsers.Add(newUser);

        _db.Shelves.Add(new Shelf
        {
            UserId = newUser.Id,
            Name = "Kệ sách cá nhân của tôi",
            Description = "Kệ sách mặc định dành cho thành viên mới",
            IsPublic = true,
            DisplayOrder = 1
        });

        _db.Memberships.Add(new Membership
        {
            UserId = newUser.Id,
            Tier = "Free",
            MaxConcurrentLoans = 2,
            StartDate = DateTime.UtcNow,
            IsActive = true
        });

        await _db.SaveChangesAsync();

        var claims = new AuthUserClaimsDto
        {
            UserId = newUser.Id,
            Email = newUser.Email,
            FullName = newUser.FullName,
            PrimaryRole = roleName,
            TrustScore = newUser.TrustScore
        };

        return (true, "Đăng ký tài khoản mới thành công với 100 điểm uy tín khởi đầu!", claims);
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> UpsertGoogleLoginAsync(
        string providerKey,
        string email,
        string fullName)
    {
        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(email))
        {
            return (false, "Thiếu thông tin định danh từ Google.", null);
        }

        await EnsureCoreAdminBootstrapAsync();

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var userLogin = await _db.AspNetUserLogins
            .Include(l => l.User)
            .ThenInclude(u => u.Roles)
            .FirstOrDefaultAsync(l => l.LoginProvider == "Google" && l.ProviderKey == providerKey);

        AspNetUser? user = userLogin?.User;

        if (user == null)
        {
            user = await _db.AspNetUsers
                .Include(u => u.Roles)
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

            if (user == null)
            {
                var userRole = await _db.AspNetRoles.FirstOrDefaultAsync(r => r.Name == "User");
                user = new AspNetUser
                {
                    Id = $"u-gg-{Guid.NewGuid():N}"[..16],
                    UserName = email.Trim(),
                    NormalizedUserName = normalizedEmail,
                    Email = email.Trim(),
                    NormalizedEmail = normalizedEmail,
                    EmailConfirmed = true,
                    LockoutEnabled = true,
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    FullName = string.IsNullOrWhiteSpace(fullName) ? email.Split('@')[0] : fullName.Trim(),
                    Address = "Hà Nội",
                    TrustScore = 100,
                    LendQuotaBonus = 0,
                    CreatedAt = DateTime.UtcNow
                };
                if (userRole != null)
                {
                    user.Roles.Add(userRole);
                }
                _db.AspNetUsers.Add(user);

                _db.Shelves.Add(new Shelf
                {
                    UserId = user.Id,
                    Name = "Kệ sách cá nhân của tôi",
                    Description = "Kệ sách mặc định dành cho thành viên mới",
                    IsPublic = true,
                    DisplayOrder = 1
                });

                _db.Memberships.Add(new Membership
                {
                    UserId = user.Id,
                    Tier = "Free",
                    MaxConcurrentLoans = 2,
                    StartDate = DateTime.UtcNow,
                    IsActive = true
                });
            }

            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
            {
                return (false, "Tài khoản của bạn hiện đang bị tạm khóa. Không thể đăng nhập bằng Google.", null);
            }

            _db.AspNetUserLogins.Add(new AspNetUserLogin
            {
                LoginProvider = "Google",
                ProviderKey = providerKey,
                ProviderDisplayName = "Google OAuth 2.0",
                UserId = user.Id
            });

            await _db.SaveChangesAsync();
        }
        else if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, "Tài khoản của bạn hiện đang bị tạm khóa. Không thể đăng nhập bằng Google.", null);
        }

        var roleName = user.Roles.FirstOrDefault()?.Name ?? "User";
        return (true, "Đã liên kết và đăng nhập bằng Google thành công!", new AuthUserClaimsDto
        {
            UserId = user.Id,
            Email = user.Email ?? email,
            FullName = user.FullName,
            PrimaryRole = roleName,
            TrustScore = user.TrustScore,
            AvatarUrl = user.AvatarUrl
        });
    }

    private bool VerifyUserPassword(AspNetUser user, string password, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrWhiteSpace(user.PasswordHash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        // Hỗ trợ nâng cấp mật khẩu khởi tạo từ dữ liệu SQL mẫu cũ sang băm chuẩn Identity trong lần đăng nhập đầu tiên
        var legacySeedSecret = user.PasswordHash switch
        {
            "AQAAAAIAAYagAAAAEBookShareDemoHashAdmin123!" => "Admin@123",
            "AQAAAAIAAYagAAAAEBookShareDemoHashLib123!" => "Librarian@123",
            "AQAAAAIAAYagAAAAEBookShareDemoHashCurator123!" => "Curator@123",
            "AQAAAAIAAYagAAAAEBookShareDemoHashUser123!" => "User@123",
            _ => null
        };

        if (legacySeedSecret != null)
        {
            if (string.Equals(password, legacySeedSecret, StringComparison.Ordinal))
            {
                needsRehash = true;
                return true;
            }
            return false;
        }

        try
        {
            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
            {
                needsRehash = true;
                return true;
            }
            return verifyResult == PasswordVerificationResult.Success;
        }
        catch
        {
            return false;
        }
    }
}
