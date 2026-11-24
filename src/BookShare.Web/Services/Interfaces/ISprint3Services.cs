using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

public interface IBookHandoverQrService
{
    Task<(bool Success, string Message, CreateHandoverQrResultDto? Data)> CreateHandoverQrAsync(
        string currentUserId,
        string currentUserRole,
        CreateHandoverQrRequestDto request);

    Task<(bool Success, string Message, string ActionPerformed)> ConfirmHandoverQrAsync(
        string currentUserId,
        string currentUserRole,
        ConfirmHandoverQrRequestDto request);
}

public interface IQrAuthService
{
    Task<(bool Success, string Message, CreateQrLoginResultDto? Data)> CreateWebLoginSessionAsync(
        string? deviceInfo,
        string? ipAddress);

    Task<QrLoginStatusDto> GetSessionStatusAsync(int sessionId);

    Task<(bool Success, string Message, PreviewQrLoginResultDto? Data)> PreviewWebLoginSessionAsync(
        PreviewQrLoginRequestDto request);

    Task<(bool Success, string Message)> ApproveWebLoginSessionAsync(
        string currentUserId,
        ApproveQrLoginRequestDto request);

    Task<(bool Success, int StatusCode, string Message, AuthUserClaimsDto? UserClaims)> ClaimWebLoginSessionAsync(
        ClaimQrLoginRequestDto request);

    Task<(bool Success, string Message, CreateQrLoginResultDto? Data)> CreateMobileLinkSessionAsync(
        string currentUserId,
        string? deviceInfo,
        string? ipAddress);

    Task<(bool Success, int StatusCode, string Message, AuthUserClaimsDto? UserClaims)> ClaimMobileLinkSessionAsync(
        MobileClaimQrLoginRequestDto request);
}

public interface IMobileCompanionService
{
    Task<MobileUnreadNotificationsResponseDto> GetUnreadNotificationsAsync(
        string currentUserId,
        int sinceId = 0);

    Task<MobileSessionStateDto> GetSessionStateAsync(
        string? currentUserId,
        string? currentUserRole,
        string? fullName);
}
