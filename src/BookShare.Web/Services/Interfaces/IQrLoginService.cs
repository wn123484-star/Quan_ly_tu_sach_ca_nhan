using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

// [BS-QRLOGIN-BRIDGE] Dịch vụ điều phối phiên đăng nhập chéo bằng Camera QR (Mobile -> Web PC)
public interface IQrLoginService
{
    Task<QrLoginInitResponseDto> InitSessionAsync();
    Task<QrLoginStatusResponseDto> GetStatusAsync(string token);
    Task<(bool Success, string Message)> ApproveSessionAsync(string token, string approverUserId);
    Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> ClaimSessionAsync(string token);
}
