using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [CORE-AUTH] Lớp tiện ích tĩnh hỗ trợ Xác thực & Kiểm tra hợp lệ đầu vào: đọc khóa Google OAuth, dựng ClaimsPrincipal/Cookie, kiểm tra định dạng Email, SĐT Việt Nam, URL ảnh an toàn và băm SHA-256.
/// </summary>
/// <remarks>Dùng chung trên toàn hệ thống Web và Services.</remarks>
public static class AuthConfigHelper
{
    public const string ExternalCookieScheme = "BookShare.External";

    private static readonly string[] PlaceholderTokens =
    [
        "demo",
        "xxxx",
        "your-",
        "dien_key",
        "placeholder",
        "changeme"
    ];

    public static string SanitizeEnvValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return raw.Trim().Trim('"', '\'').Trim();
    }

    public static bool IsValidEmailFormat(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        string trimmed = email.Trim();
        if (trimmed.Length < 5 || trimmed.Length > 254 || trimmed.Contains(' '))
        {
            return false;
        }

        try
        {
            var addr = new System.Net.Mail.MailAddress(trimmed);
            return string.Equals(addr.Address, trimmed, StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains('.')
                && !trimmed.EndsWith('.');
        }
        catch
        {
            return false;
        }
    }

    public static (string ClientId, string ClientSecret) GetGoogleOAuthCredentials(IConfiguration config)
    {
        var clientId = SanitizeEnvValue(config["Authentication:Google:ClientId"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID"));
        var clientSecret = SanitizeEnvValue(config["Authentication:Google:ClientSecret"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET"));
        return (clientId, clientSecret);
    }

    public static bool IsGoogleOAuthConfigured(IConfiguration config)
    {
        var (clientId, clientSecret) = GetGoogleOAuthCredentials(config);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return false;
        }

        foreach (var token in PlaceholderTokens)
        {
            if (clientId.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                clientSecret.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static ClaimsPrincipal BuildClaimsPrincipal(AuthUserClaimsDto userClaims)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userClaims.UserId),
            new(ClaimTypes.Email, userClaims.Email),
            new(ClaimTypes.Name, userClaims.FullName),
            new(ClaimTypes.Role, userClaims.PrimaryRole),
            new("TrustScore", userClaims.TrustScore.ToString())
        };
        if (!string.IsNullOrWhiteSpace(userClaims.AvatarUrl))
        {
            claims.Add(new Claim("AvatarUrl", userClaims.AvatarUrl));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public static AuthenticationProperties CreateCookieProperties(bool isPersistent = true)
    {
        return new AuthenticationProperties
        {
            IsPersistent = isPersistent,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        };
    }

    public static string GenerateBase64UrlToken(int byteLength = 32)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string GenerateShortCode(int length = 6)
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
        return new string(chars);
    }

    /// <summary>
    /// Kiểm tra định dạng số điện thoại hợp lệ (hỗ trợ chuẩn Việt Nam 9-11 chữ số, cho phép dấu cách/gạch ngang).
    /// </summary>
    public static bool IsValidVietnamPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return true;
        }

        string compact = Regex.Replace(phone.Trim(), @"[\s\-\.\(\)]", string.Empty);
        return Regex.IsMatch(compact, @"^(?:\+84|84|0)[0-9]{8,10}$");
    }

    /// <summary>
    /// Kiểm tra đường dẫn ảnh có an toàn không (chỉ cho phép đường dẫn tương đối bắt đầu bằng '/' hoặc http/https,
    /// chặn tuyệt đối giao thức nguy hiểm như javascript:, vbscript:, data:).
    /// </summary>
    public static bool IsSafeImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        string trimmed = url.Trim();
        if (trimmed.Length > 1000)
        {
            return false;
        }

        if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return !trimmed.Contains('\r') && !trimmed.Contains('\n');
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            return parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || parsed.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static string ComputeSha256Hex(string rawInput)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawInput));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
