using System.Collections.Concurrent;
using System.Security.Claims;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class QrAuthService : IQrAuthService
{
    private const string WebLoginFlow = "WebLoginByMobile";
    private const string MobileLinkFlow = "MobileLoginByWeb";
    private const string WebLoginPrefix = "BOOKSHARE-QRLOGIN:v1:";
    private const string MobileLinkPrefix = "BOOKSHARE-MOBILELINK:v1:";
    private const int WebLoginTtlSeconds = 120;
    private const int MobileLinkTtlSeconds = 60;
    private const int MaxCreatesPerIpPerMinute = 20;

    private static readonly ConcurrentDictionary<string, List<DateTime>> IpCreateBuckets = new(StringComparer.OrdinalIgnoreCase);

    private readonly BookShareDbContext _db;

    public QrAuthService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<(bool Success, string Message, CreateQrLoginResultDto? Data)> CreateWebLoginSessionAsync(
        string? deviceInfo,
        string? ipAddress)
    {
        string normalizedIp = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress.Trim();
        if (normalizedIp.Length > 64)
        {
            normalizedIp = normalizedIp[..64];
        }

        if (!TryConsumeIpRateQuota(normalizedIp))
        {
            return (false, "Bạn thao tác tạo mã xác nhận quá nhanh. Vui lòng thử lại sau ít phút.", null);
        }

        DateTime utcNow = DateTime.UtcNow;
        DateTime expiresAt = utcNow.AddSeconds(WebLoginTtlSeconds);
        string rawToken = AuthConfigHelper.GenerateBase64UrlToken(32);
        string tokenHash = AuthConfigHelper.ComputeSha256Hex(rawToken);

        var session = new QrLoginSession
        {
            TokenHash = tokenHash,
            FlowType = WebLoginFlow,
            Status = "Pending",
            DeviceInfo = SanitizeDeviceInfo(deviceInfo, "Trình duyệt máy tính"),
            IpAddress = normalizedIp,
            CreatedAt = utcNow,
            ExpiresAt = expiresAt
        };

        _db.QrLoginSessions.Add(session);
        await _db.SaveChangesAsync();

        string payload = $"{WebLoginPrefix}{session.Id}:{rawToken}";
        return (true, "Đã khởi tạo mã xác nhận đăng nhập nhanh (hiệu lực 120 giây).", new CreateQrLoginResultDto
        {
            SessionId = session.Id,
            FlowType = WebLoginFlow,
            Token = rawToken,
            QrPayload = payload,
            ExpiresInSeconds = WebLoginTtlSeconds,
            ExpiresAt = expiresAt
        });
    }

    public async Task<QrLoginStatusDto> GetSessionStatusAsync(int sessionId)
    {
        if (sessionId <= 0)
        {
            return new QrLoginStatusDto { SessionId = sessionId, Status = "Expired", RemainingSeconds = 0 };
        }

        var session = await _db.QrLoginSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null)
        {
            return new QrLoginStatusDto { SessionId = sessionId, Status = "Expired", RemainingSeconds = 0 };
        }

        DateTime utcNow = DateTime.UtcNow;
        if ((session.Status == "Pending" || session.Status == "Approved") && session.ExpiresAt <= utcNow)
        {
            session.Status = "Expired";
            await _db.SaveChangesAsync();
            return new QrLoginStatusDto { SessionId = session.Id, Status = "Expired", RemainingSeconds = 0 };
        }

        int remaining = session.Status is "Pending" or "Approved"
            ? Math.Max(0, (int)Math.Ceiling((session.ExpiresAt - utcNow).TotalSeconds))
            : 0;

        return new QrLoginStatusDto
        {
            SessionId = session.Id,
            Status = session.Status,
            RemainingSeconds = remaining
        };
    }

    public async Task<(bool Success, string Message, PreviewQrLoginResultDto? Data)> PreviewWebLoginSessionAsync(
        PreviewQrLoginRequestDto request)
    {
        if (!TryParseQrInput(request.QrPayload, request.Token, WebLoginPrefix, out int? parsedSessionId, out string rawToken))
        {
            return (false, "Mã xác nhận không đúng định dạng đăng nhập nhanh.", null);
        }

        string tokenHash = AuthConfigHelper.ComputeSha256Hex(rawToken);
        var query = _db.QrLoginSessions.AsNoTracking()
            .Where(s => s.FlowType == WebLoginFlow && s.TokenHash == tokenHash);

        if (parsedSessionId.HasValue)
        {
            query = query.Where(s => s.Id == parsedSessionId.Value);
        }

        var session = await query.FirstOrDefaultAsync();
        DateTime utcNow = DateTime.UtcNow;
        if (session == null || session.Status != "Pending" || session.ExpiresAt <= utcNow)
        {
            return (false, "Mã xác nhận đăng nhập không hợp lệ, đã hết hạn hoặc đã được xử lý.", null);
        }

        int remaining = Math.Max(1, (int)Math.Ceiling((session.ExpiresAt - utcNow).TotalSeconds));
        return (true, "Đã đọc thông tin thiết bị yêu cầu đăng nhập.", new PreviewQrLoginResultDto
        {
            SessionId = session.Id,
            DeviceInfo = session.DeviceInfo ?? "Thiết bị chưa xác định",
            IpAddress = session.IpAddress ?? "Không rõ",
            RemainingSeconds = remaining
        });
    }

    public async Task<(bool Success, string Message)> ApproveWebLoginSessionAsync(
        string currentUserId,
        ApproveQrLoginRequestDto request)
    {
        if (request.SessionId <= 0 || string.IsNullOrWhiteSpace(currentUserId))
        {
            return (false, "Thông tin phê duyệt đăng nhập không hợp lệ.");
        }

        var approver = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (approver == null)
        {
            return (false, "Không tìm thấy tài khoản thực hiện phê duyệt.");
        }

        if (approver.LockoutEnd.HasValue && approver.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, "Tài khoản đang bị tạm khóa, không thể phê duyệt đăng nhập thiết bị khác.");
        }

        var session = await _db.QrLoginSessions
            .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.FlowType == WebLoginFlow);

        DateTime utcNow = DateTime.UtcNow;
        if (session == null || session.Status != "Pending" || session.ExpiresAt <= utcNow)
        {
            return (false, "Mã đăng nhập đã hết hạn hoặc đã được xác nhận trước đó.");
        }

        if (!request.Approve)
        {
            session.Status = "Rejected";
            await _db.SaveChangesAsync();
            return (true, "Đã từ chối yêu cầu đăng nhập từ thiết bị vừa quét.");
        }

        session.Status = "Approved";
        session.ApprovedUserId = currentUserId;
        await _db.SaveChangesAsync();
        return (true, "Đã phê duyệt đăng nhập thành công! Thiết bị kia sẽ tự động vào hệ thống.");
    }

    public async Task<(bool Success, int StatusCode, string Message, AuthUserClaimsDto? UserClaims)> ClaimWebLoginSessionAsync(
        ClaimQrLoginRequestDto request)
    {
        if (request.SessionId <= 0 || string.IsNullOrWhiteSpace(request.Token))
        {
            return (false, 400, "Thông tin xác nhận đăng nhập không hợp lệ.", null);
        }

        string tokenHash = AuthConfigHelper.ComputeSha256Hex(request.Token.Trim());
        DateTime utcNow = DateTime.UtcNow;

        var session = await _db.QrLoginSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.Id == request.SessionId &&
                s.FlowType == WebLoginFlow &&
                s.TokenHash == tokenHash);

        if (session == null || session.Status != "Approved" || session.ExpiresAt <= utcNow || string.IsNullOrWhiteSpace(session.ApprovedUserId))
        {
            return (false, 400, "Phiên đăng nhập không hợp lệ, đã hết hạn hoặc đã được sử dụng.", null);
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == session.ApprovedUserId);

        if (user == null)
        {
            return (false, 400, "Không tìm thấy tài khoản gắn với mã xác nhận này.", null);
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, 403, "Tài khoản đang bị tạm khóa, không thể hoàn tất đăng nhập.", null);
        }

        int affected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE [dbo].[QrLoginSessions]
               SET [Status] = N'Consumed', [ConsumedAt] = {utcNow}
               WHERE [Id] = {session.Id}
                 AND [FlowType] = N'WebLoginByMobile'
                 AND [Status] = N'Approved'
                 AND [TokenHash] = {tokenHash}
                 AND [ExpiresAt] > {utcNow}");

        if (affected != 1)
        {
            return (false, 400, "Mã đăng nhập đã được sử dụng hoặc vừa hết hạn.", null);
        }

        _db.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Title = "Đăng nhập thiết bị mới qua mã vuông",
            Content = $"Tài khoản vừa đăng nhập nhanh trên {session.DeviceInfo ?? "trình duyệt mới"} (IP: {session.IpAddress ?? "không rõ"}) lúc {utcNow.AddHours(7):HH:mm dd/MM/yyyy}. Nếu không phải bạn, hãy đổi mật khẩu ngay.",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = utcNow
        });
        await _db.SaveChangesAsync();

        var claims = BuildClaimsDto(user);
        return (true, 200, $"Chào mừng {user.FullName} quay trở lại BookShare!", claims);
    }

    public async Task<(bool Success, string Message, CreateQrLoginResultDto? Data)> CreateMobileLinkSessionAsync(
        string currentUserId,
        string? deviceInfo,
        string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return (false, "Bạn cần đăng nhập trước khi tạo mã liên kết thiết bị di động.", null);
        }

        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (user == null)
        {
            return (false, "Không tìm thấy hồ sơ tài khoản hiện tại.", null);
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, "Tài khoản đang bị tạm khóa, không thể tạo mã liên kết thiết bị.", null);
        }

        DateTime utcNow = DateTime.UtcNow;
        DateTime expiresAt = utcNow.AddSeconds(MobileLinkTtlSeconds);
        string rawToken = AuthConfigHelper.GenerateBase64UrlToken(32);
        string tokenHash = AuthConfigHelper.ComputeSha256Hex(rawToken);

        string normalizedIp = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress.Trim();
        if (normalizedIp.Length > 64)
        {
            normalizedIp = normalizedIp[..64];
        }

        var session = new QrLoginSession
        {
            TokenHash = tokenHash,
            FlowType = MobileLinkFlow,
            Status = "Approved",
            InitiatorUserId = currentUserId,
            ApprovedUserId = currentUserId,
            DeviceInfo = SanitizeDeviceInfo(deviceInfo, "Thiết bị khởi tạo liên kết"),
            IpAddress = normalizedIp,
            CreatedAt = utcNow,
            ExpiresAt = expiresAt
        };

        _db.QrLoginSessions.Add(session);
        await _db.SaveChangesAsync();

        string payload = $"{MobileLinkPrefix}{session.Id}:{rawToken}";
        return (true, "Đã tạo mã đăng nhập nhanh 1 lần cho thiết bị khác (hiệu lực 60 giây).", new CreateQrLoginResultDto
        {
            SessionId = session.Id,
            FlowType = MobileLinkFlow,
            Token = rawToken,
            QrPayload = payload,
            ExpiresInSeconds = MobileLinkTtlSeconds,
            ExpiresAt = expiresAt
        });
    }

    public async Task<(bool Success, int StatusCode, string Message, AuthUserClaimsDto? UserClaims)> ClaimMobileLinkSessionAsync(
        MobileClaimQrLoginRequestDto request)
    {
        if (!TryParseQrInput(request.QrPayload, request.Token, MobileLinkPrefix, out int? parsedSessionId, out string rawToken))
        {
            return (false, 400, "Mã liên kết di động không đúng định dạng hợp lệ.", null);
        }

        string tokenHash = AuthConfigHelper.ComputeSha256Hex(rawToken);
        DateTime utcNow = DateTime.UtcNow;

        var query = _db.QrLoginSessions
            .AsNoTracking()
            .Where(s => s.FlowType == MobileLinkFlow && s.TokenHash == tokenHash);

        if (parsedSessionId.HasValue)
        {
            query = query.Where(s => s.Id == parsedSessionId.Value);
        }

        var session = await query.FirstOrDefaultAsync();
        string? targetUserId = session?.ApprovedUserId ?? session?.InitiatorUserId;
        if (session == null || session.Status != "Approved" || session.ExpiresAt <= utcNow || string.IsNullOrWhiteSpace(targetUserId))
        {
            return (false, 400, "Mã liên kết không hợp lệ, đã hết hạn hoặc đã được sử dụng.", null);
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == targetUserId);

        if (user == null)
        {
            return (false, 400, "Không tìm thấy tài khoản liên kết tương ứng.", null);
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, 403, "Tài khoản đang bị tạm khóa, không thể đồng bộ phiên đăng nhập.", null);
        }

        int affected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE [dbo].[QrLoginSessions]
               SET [Status] = N'Consumed', [ConsumedAt] = {utcNow}
               WHERE [Id] = {session.Id}
                 AND [FlowType] = N'MobileLoginByWeb'
                 AND [Status] = N'Approved'
                 AND [TokenHash] = {tokenHash}
                 AND [ExpiresAt] > {utcNow}");

        if (affected != 1)
        {
            return (false, 400, "Mã liên kết đã được sử dụng hoặc đã hết hạn.", null);
        }

        _db.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Title = "Đồng bộ đăng nhập thiết bị mới",
            Content = $"Một thiết bị vừa đăng nhập bằng mã liên kết nhanh lúc {utcNow.AddHours(7):HH:mm dd/MM/yyyy}.",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = utcNow
        });
        await _db.SaveChangesAsync();

        var claims = BuildClaimsDto(user);
        return (true, 200, $"Đồng bộ đăng nhập thành công cho {user.FullName}!", claims);
    }

    private static AuthUserClaimsDto BuildClaimsDto(AspNetUser user)
    {
        var roles = user.Roles;
        string primaryRole =
            roles.Any(r => r.Name == "Admin") ? "Admin" :
            roles.Any(r => r.Name == "Librarian") ? "Librarian" :
            roles.Any(r => r.Name == "Curator") ? "Curator" :
            "User";

        return new AuthUserClaimsDto
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            PrimaryRole = primaryRole,
            TrustScore = user.TrustScore,
            AvatarUrl = user.AvatarUrl
        };
    }

    private static bool TryParseQrInput(
        string? qrPayload,
        string? fallbackToken,
        string expectedPrefix,
        out int? sessionId,
        out string rawToken)
    {
        sessionId = null;
        rawToken = string.Empty;

        string candidatePayload = qrPayload?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(candidatePayload))
        {
            if (candidatePayload.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string remainder = candidatePayload[expectedPrefix.Length..];
                int sep = remainder.IndexOf(':');
                if (sep > 0 && int.TryParse(remainder[..sep], out int parsedId))
                {
                    string tokenPart = remainder[(sep + 1)..].Trim();
                    if (!string.IsNullOrEmpty(tokenPart))
                    {
                        sessionId = parsedId;
                        rawToken = tokenPart;
                        return true;
                    }
                }
                return false;
            }

            if (string.IsNullOrWhiteSpace(fallbackToken) && !candidatePayload.StartsWith("BOOKSHARE-", StringComparison.OrdinalIgnoreCase))
            {
                rawToken = candidatePayload;
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackToken))
        {
            rawToken = fallbackToken.Trim();
            return true;
        }

        return false;
    }

    private static bool TryConsumeIpRateQuota(string ip)
    {
        DateTime cutoff = DateTime.UtcNow.AddMinutes(-1);
        var list = IpCreateBuckets.GetOrAdd(ip, _ => new List<DateTime>());
        lock (list)
        {
            list.RemoveAll(t => t < cutoff);
            if (list.Count >= MaxCreatesPerIpPerMinute)
            {
                return false;
            }
            list.Add(DateTime.UtcNow);
            return true;
        }
    }

    private static string SanitizeDeviceInfo(string? userAgent, string fallback)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return fallback;
        }

        string ua = userAgent.Trim();
        if (ua.Contains("BookShareAndroidApp", StringComparison.OrdinalIgnoreCase))
        {
            return "Ứng dụng BookShare Android";
        }
        if (ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
        {
            return ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ? "Microsoft Edge (Di động)" : "Microsoft Edge (Máy tính)";
        }
        if (ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
        {
            return ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ? "Google Chrome (Di động)" : "Google Chrome (Máy tính)";
        }
        if (ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
        {
            return "Mozilla Firefox";
        }
        if (ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase))
        {
            return ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase)
                ? "Safari (iOS)"
                : "Safari (macOS)";
        }

        return ua.Length > 120 ? ua[..120] : ua;
    }
}
