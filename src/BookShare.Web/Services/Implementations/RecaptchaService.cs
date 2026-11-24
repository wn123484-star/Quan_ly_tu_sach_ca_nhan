using System.Text.Json;
using System.Text.Json.Serialization;
using BookShare.Web.Services.Interfaces;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [BOT-GUARD] Triển khai Google reCAPTCHA v3 với cơ chế Smart Bypass an toàn khi chưa điền khóa trong .env.
/// Khi có ReCaptcha__SiteKey &amp; ReCaptcha__SecretKey thật -> Gọi POST https://www.google.com/recaptcha/api/siteverify
/// và yêu cầu success == true, score &gt;= 0.5.
/// </summary>
public class RecaptchaService : IRecaptchaService
{
    private const string SiteVerifyUrl = "https://www.google.com/recaptcha/api/siteverify";
    private const double MinHumanScore = 0.5;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _siteKey;
    private readonly string _secretKey;

    public RecaptchaService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _siteKey = SanitizeKey(configuration["ReCaptcha:SiteKey"]);
        _secretKey = SanitizeKey(configuration["ReCaptcha:SecretKey"]);
    }

    public string SiteKey => _siteKey;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_siteKey) && !string.IsNullOrWhiteSpace(_secretKey);

    public async Task<(bool Success, double Score, string Message)> VerifyTokenAsync(
        string? token,
        string expectedAction,
        string? remoteIp = null)
    {
        // [BOT-GUARD] Smart Bypass: Nếu chưa cấu hình Key thật trong .env -> Tự động bỏ qua để không cản trở môi trường Local/Test
        if (!IsEnabled)
        {
            return (true, 1.0, "reCAPTCHA Smart Bypass: Chưa cấu hình ReCaptcha__SiteKey / ReCaptcha__SecretKey trong .env.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, 0.0, "Không nhận được mã xác thực chống Bot (Google reCAPTCHA v3). Vui lòng tải lại trang và thao tác lại.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(6);

            var formFields = new List<KeyValuePair<string, string>>
            {
                new("secret", _secretKey),
                new("response", token.Trim())
            };

            if (!string.IsNullOrWhiteSpace(remoteIp))
            {
                formFields.Add(new("remoteip", remoteIp));
            }

            using var content = new FormUrlEncodedContent(formFields);
            using var response = await client.PostAsync(SiteVerifyUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                return (false, 0.0, $"Không thể xác minh Google reCAPTCHA v3 (HTTP {(int)response.StatusCode}). Vui lòng thử lại sau.");
            }

            var json = await response.Content.ReadAsStringAsync();
            var verification = JsonSerializer.Deserialize<RecaptchaSiteVerifyResponse>(json);

            if (verification == null || !verification.Success)
            {
                var errCodes = verification?.ErrorCodes is { Length: > 0 }
                    ? string.Join(", ", verification.ErrorCodes)
                    : "invalid-token";
                return (false, 0.0, $"Xác thực chống Bot Google reCAPTCHA v3 thất bại ({errCodes}). Vui lòng tải lại trang và thử lại.");
            }

            if (!string.IsNullOrWhiteSpace(expectedAction) &&
                !string.IsNullOrWhiteSpace(verification.Action) &&
                !string.Equals(verification.Action, expectedAction, StringComparison.OrdinalIgnoreCase))
            {
                return (false, verification.Score, "Mã xác thực reCAPTCHA không khớp hành động yêu cầu (Action Mismatch).");
            }

            if (verification.Score < MinHumanScore)
            {
                return (false, verification.Score,
                    $"Hệ thống chống Bot phát hiện hoạt động bất thường (reCAPTCHA v3 score = {verification.Score:0.00} < {MinHumanScore:0.00}). Yêu cầu đã bị chặn để bảo vệ tài nguyên.");
            }

            return (true, verification.Score, $"Xác thực người thật thành công (reCAPTCHA v3 score = {verification.Score:0.00}).");
        }
        catch (Exception ex)
        {
            return (false, 0.0, $"Lỗi kết nối dịch vụ xác thực chống Bot Google reCAPTCHA v3: {ex.Message}");
        }
    }

    private static string SanitizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = AuthConfigHelper.SanitizeEnvValue(raw);
        if (trimmed.Contains("xxxx", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("demo", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return trimmed;
    }

    private sealed class RecaptchaSiteVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; }

        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; set; }
    }
}
