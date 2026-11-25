using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

/// <summary>
/// [CORE-QR-HANDOVER] Giao diện dịch vụ tạo và xác nhận mã QR bàn giao / hoàn trả sách trực tiếp.
/// </summary>
/// <remarks>Triển khai bởi: BookHandoverQrService.</remarks>
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

/// <summary>
/// [CORE-QR-AUTH] Giao diện dịch vụ Đăng nhập nhanh bằng mã QR không cần nhập mật khẩu.
/// </summary>
/// <remarks>Triển khai bởi: QrAuthService.</remarks>
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

/// <summary>
/// [CORE-MOBILE] Giao diện dịch vụ hỗ trợ Ứng dụng di động đồng hành (Mobile Companion / PWA).
/// </summary>
/// <remarks>Triển khai bởi: MobileCompanionService.</remarks>
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
