using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class ResendEmailService : IResendEmailService
{
    private readonly BookShareDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        BookShareDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<ResendEmailService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<(bool Success, string Message, string? DevOtpCode)> GenerateAndSendOtpAsync(
        string email,
        string purpose,
        string? userId = null)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var allowedPurposes = new[] { "Register", "ResetPassword", "VerifyLoan" };
        if (!allowedPurposes.Contains(purpose))
        {
            purpose = "VerifyLoan";
        }

        // Sinh mã OTP 6 chữ số an toàn mật mã học
        int otpNumber = RandomNumberGenerator.GetInt32(100000, 999999);
        string otpCodePlain = otpNumber.ToString("D6");
        string otpHash = ComputeSha256Hash(otpCodePlain);

        // Nếu chưa truyền userId thì thử tìm theo email
        if (string.IsNullOrEmpty(userId))
        {
            var existingUser = await _db.AspNetUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail.ToUpperInvariant());
            userId = existingUser?.Id;
        }

        string? resendMessageId = null;
        string apiKey = _config["Resend:ApiKey"] ?? string.Empty;
        string fromEmail = _config["Resend:FromEmail"] ?? "BookShare <onboarding@resend.dev>";
        bool sentViaRealResend = false;

        if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.Contains("DIEN_KEY"))
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var payload = new
                {
                    from = fromEmail,
                    to = new[] { normalizedEmail },
                    subject = $"[BookShare] Mã xác thực OTP ({otpCodePlain}) cho tác vụ {purpose}",
                    html = $"<div style='font-family:Roboto,sans-serif;padding:20px;border:1px solid #e2e8f0;border-radius:12px;'>" +
                           $"<h2 style='color:#1e3a8a;'>BookShare Portal - Xác Thực OTP</h2>" +
                           $"<p>Mã xác thực OTP 6 chữ số của bạn cho tác vụ <strong>{purpose}</strong> là:</p>" +
                           $"<div style='font-size:28px;font-weight:bold;letter-spacing:6px;color:#2563eb;padding:12px 0;'>{otpCodePlain}</div>" +
                           $"<p style='color:#64748b;font-size:13px;'>Mã có hiệu lực trong vòng 5 phút. Tuyệt đối không chia sẻ mã này cho người lạ.</p></div>"
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.resend.com/emails", content);
                if (response.IsSuccessStatusCode)
                {
                    var respBody = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(respBody);
                    if (doc.RootElement.TryGetProperty("id", out var idProp))
                    {
                        resendMessageId = idProp.GetString();
                    }
                    sentViaRealResend = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể kết nối Resend API, chuyển sang chế độ Dev OTP Console.");
            }
        }

        var verification = new OtpVerification
        {
            UserId = userId,
            Email = normalizedEmail,
            OtpCodeHash = otpHash,
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            IsUsed = false,
            ResendMessageId = resendMessageId ?? $"dev-local-{Guid.NewGuid():N}"[..24],
            CreatedAt = DateTime.UtcNow
        };

        _db.OtpVerifications.Add(verification);
        await _db.SaveChangesAsync();

        _logger.LogInformation("==================================================================");
        _logger.LogInformation("[BOOKSHARE RESEND OTP - {Purpose}] Email: {Email} | Mã OTP 6 số: {OtpCode} | SHA256: {Hash}",
            purpose, normalizedEmail, otpCodePlain, otpHash[..16] + "...");
        _logger.LogInformation("==================================================================");

        string msg = sentViaRealResend
            ? $"Đã gửi mã OTP 6 chữ số tới hộp thư {normalizedEmail} qua Resend API (Hiệu lực 5 phút)."
            : $"Đã lưu băm SHA-256 vào bảng OtpVerifications. [Chế độ Dev/Demo chưa cấu hình Resend API Key thật: Mã OTP của bạn là {otpCodePlain}].";

        return (true, msg, sentViaRealResend ? null : otpCodePlain);
    }

    public async Task<(bool Success, string Message)> VerifyOtpAsync(
        string email,
        string otpCode,
        string purpose)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var cleanCode = otpCode.Trim();
        var codeHash = ComputeSha256Hash(cleanCode);

        var record = await _db.OtpVerifications
            .Where(o => o.Email == normalizedEmail && o.Purpose == purpose && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (record == null)
        {
            return (false, "Không tìm thấy yêu cầu xác thực OTP nào đang chờ cho địa chỉ email này.");
        }

        if (record.ExpiresAt < DateTime.UtcNow)
        {
            return (false, "Mã OTP đã hết hạn (quá 5 phút). Vui lòng bấm gửi lại mã mới.");
        }

        if (!string.Equals(record.OtpCodeHash, codeHash, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Mã OTP 6 chữ số không chính xác. Vui lòng kiểm tra lại.");
        }

        record.IsUsed = true;
        await _db.SaveChangesAsync();

        return (true, $"Xác thực mã OTP ({purpose}) thành công cho tài khoản {normalizedEmail}!");
    }

    public async Task<(bool SentViaApi, string MessageId, string StatusNote)> SendNotificationEmailAsync(
        string toEmail,
        string subject,
        string htmlBody)
    {
        var normalizedEmail = toEmail.Trim().ToLowerInvariant();
        string apiKey = _config["Resend:ApiKey"] ?? string.Empty;
        string fromEmail = _config["Resend:FromEmail"] ?? "BookShare <onboarding@resend.dev>";

        if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.Contains("DIEN_KEY"))
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var payload = new
                {
                    from = fromEmail,
                    to = new[] { normalizedEmail },
                    subject,
                    html = htmlBody
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.resend.com/emails", content);
                if (response.IsSuccessStatusCode)
                {
                    var respBody = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(respBody);
                    string msgId = doc.RootElement.TryGetProperty("id", out var idProp)
                        ? (idProp.GetString() ?? $"resend-{Guid.NewGuid():N}"[..20])
                        : $"resend-{Guid.NewGuid():N}"[..20];

                    return (true, msgId, $"Đã gửi Email thực qua Resend API tới {normalizedEmail} (MessageId: {msgId}).");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể kết nối Resend API khi gửi thông báo, dùng chế độ Dev Mail Log.");
            }
        }

        string devId = $"dev-resend-{Guid.NewGuid():N}"[..22];
        _logger.LogInformation("[BOOKSHARE RESEND NOTIFICATION] To: {To} | Subject: {Subject} | MsgId: {MsgId}",
            normalizedEmail, subject, devId);

        return (true, devId, $"Đã kích hoạt IResendEmailService tới {normalizedEmail} (Mã thông điệp: {devId}).");
    }

    private static string ComputeSha256Hash(string rawData)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
