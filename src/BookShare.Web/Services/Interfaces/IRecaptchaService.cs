namespace BookShare.Web.Services.Interfaces;

/// <summary>
/// [BOT-GUARD] Dịch vụ xác thực Google reCAPTCHA v3 chạy ngầm (Score-based) chống Bot &amp; Spam.
/// Cấu hình qua biến môi trường .env: ReCaptcha__SiteKey và ReCaptcha__SecretKey.
/// </summary>
public interface IRecaptchaService
{
    string SiteKey { get; }
    bool IsEnabled { get; }
    Task<(bool Success, double Score, string Message)> VerifyTokenAsync(
        string? token,
        string expectedAction,
        string? remoteIp = null);
}
