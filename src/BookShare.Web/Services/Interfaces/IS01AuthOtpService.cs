using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

/// <summary>
/// [CORE-EMAIL] Giao diện dịch vụ gửi Email thực (Resend API / SMTP) và phát hành/xác thực mã OTP 6 chữ số.
/// </summary>
/// <remarks>Triển khai bởi: ResendEmailService.</remarks>
public interface IResendEmailService
{
    Task<(bool Success, string Message, string? DevOtpCode)> GenerateAndSendOtpAsync(
        string email,
        string purpose,
        string? userId = null);

    Task<(bool Success, string Message)> VerifyOtpAsync(
        string email,
        string otpCode,
        string purpose);

    Task<(bool SentViaApi, string MessageId, string StatusNote)> SendNotificationEmailAsync(
        string toEmail,
        string subject,
        string htmlBody);
}

/// <summary>
/// [S01] Giao diện dịch vụ Xác thực, Đăng ký, Google OAuth, Khôi phục mật khẩu OTP, Hồ sơ cá nhân, Tải ảnh đại diện và Cá nhân hóa giao diện.
/// </summary>
/// <remarks>Triển khai bởi: S01AuthOtpService.</remarks>
public interface IS01AuthOtpService
{
    Task<S01AuthPageViewModel> GetAuthPageViewModelAsync(string? currentUserId, string? returnUrl = null);

    Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> AuthenticateAsync(
        string email,
        string password);

    Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> RegisterUserAsync(
        RegisterInputModel input);

    Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> UpsertGoogleLoginAsync(
        string providerKey,
        string email,
        string fullName);

    Task<(bool Success, string Message, AuthUserClaimsDto? UpdatedClaims)> UpdateUserProfileAsync(
        string userId,
        UpdateProfileRequestDto request);

    Task<(bool Success, string Message)> ChangeUserPasswordAsync(
        string userId,
        ChangePasswordRequestDto request);

    Task<(bool Success, string Message, string? DevOtpCode)> RequestPasswordResetOtpAsync(string email);

    Task<(bool Success, string Message)> VerifyPasswordResetOtpAsync(string email, string otpCode);

    Task<(bool Success, string Message)> ResetPasswordWithOtpAsync(ForgotPasswordResetRequestDto request);

    Task<(bool Success, string Message, string? AvatarUrl, AuthUserClaimsDto? UpdatedClaims)> UploadUserAvatarAsync(
        string userId,
        IFormFile file);

    Task<(bool Success, string Message, AuthUserClaimsDto? UpdatedClaims)> ResetUserAvatarAsync(string userId);

    Task<AuthUserClaimsDto?> GetUserClaimsByIdAsync(string userId);

    Task<(bool Success, string Message)> SubmitCuratorRequestAsync(
        string userId,
        SubmitCuratorRequestDto request);

    Task<UiPreferencesDto> GetUiPreferencesAsync(string userId);

    Task<(bool Success, string Message, UiPreferencesDto Saved)> SaveUiPreferencesAsync(
        string userId,
        UiPreferencesDto request);
}

public class AuthUserClaimsDto
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PrimaryRole { get; set; } = "User";
    public int TrustScore { get; set; } = 100;
    public string? AvatarUrl { get; set; }
}
