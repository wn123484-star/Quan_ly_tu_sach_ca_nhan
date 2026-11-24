using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BookShare.Web.Services.Implementations;

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

    public static string ComputeSha256Hex(string rawInput)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawInput));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
