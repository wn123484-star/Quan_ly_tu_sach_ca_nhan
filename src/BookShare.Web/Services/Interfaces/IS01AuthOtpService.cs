using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

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
}

public class AuthUserClaimsDto
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PrimaryRole { get; set; } = "User";
    public int TrustScore { get; set; } = 100;
}
