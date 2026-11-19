using System.Security.Cryptography;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [BS-QRLOGIN-BRIDGE] Xử lý vòng đời QR Cross-Login: Pending (180s) -> Approved (Mobile quét duyệt)
/// -> Claimed (PC nhận Cookie 1 lần duy nhất, tuyệt đối chặn sử dụng lại token lần 2).
/// </summary>
public class QrLoginService : IQrLoginService
{
    private const int SessionLifetimeSeconds = 180;
    private readonly BookShareDbContext _db;

    public QrLoginService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<QrLoginInitResponseDto> InitSessionAsync()
    {
        var now = DateTime.UtcNow;
        string randomHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        string token = $"BSQR-{randomHex}";
        var expiresAt = now.AddSeconds(SessionLifetimeSeconds);

        var session = new QrLoginSession
        {
            Token = token,
            Status = "Pending",
            ApprovedByUserId = null,
            ExpiresAt = expiresAt,
            CreatedAt = now
        };

        _db.QrLoginSessions.Add(session);
        await _db.SaveChangesAsync();

        return new QrLoginInitResponseDto
        {
            Token = token,
            QrPayload = token,
            ExpiresAt = expiresAt,
            ExpiresInSeconds = SessionLifetimeSeconds
        };
    }

    public async Task<QrLoginStatusResponseDto> GetStatusAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new QrLoginStatusResponseDto { Token = string.Empty, Status = "Expired", ExpiresInSeconds = 0 };
        }

        var session = await _db.QrLoginSessions
            .Include(q => q.ApprovedByUser)
            .FirstOrDefaultAsync(q => q.Token == token.Trim());

        if (session == null)
        {
            return new QrLoginStatusResponseDto { Token = token.Trim(), Status = "Expired", ExpiresInSeconds = 0 };
        }

        var now = DateTime.UtcNow;
        if (session.Status == "Pending" && session.ExpiresAt <= now)
        {
            session.Status = "Expired";
            await _db.SaveChangesAsync();
        }

        int remaining = Math.Max(0, (int)Math.Ceiling((session.ExpiresAt - now).TotalSeconds));
        return new QrLoginStatusResponseDto
        {
            Token = session.Token,
            Status = session.Status,
            ApprovedUserName = session.ApprovedByUser?.FullName,
            ExpiresInSeconds = remaining
        };
    }

    public async Task<(bool Success, string Message)> ApproveSessionAsync(string token, string approverUserId)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "Mã QR đăng nhập không hợp lệ.");
        }

        var session = await _db.QrLoginSessions.FirstOrDefaultAsync(q => q.Token == token.Trim());
        if (session == null)
        {
            return (false, "Không tìm thấy phiên đăng nhập QR tương ứng.");
        }

        var now = DateTime.UtcNow;
        if (session.ExpiresAt <= now || session.Status == "Expired")
        {
            session.Status = "Expired";
            await _db.SaveChangesAsync();
            return (false, "Mã QR đăng nhập đã hết hiệu lực (180 giây). Vui lòng làm mới mã QR trên trình duyệt PC.");
        }

        if (session.Status != "Pending")
        {
            return (false, $"Phiên QR này đã ở trạng thái '{session.Status}', không thể duyệt lại.");
        }

        var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == approverUserId);
        if (user == null)
        {
            return (false, "Tài khoản duyệt QR không tồn tại.");
        }

        session.Status = "Approved";
        session.ApprovedByUserId = user.Id;
        await _db.SaveChangesAsync();

        return (true, $"Đã xác nhận đăng nhập Web PC bằng tài khoản {user.FullName}!");
    }

    public async Task<(bool Success, string Message, AuthUserClaimsDto? UserClaims)> ClaimSessionAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "Thiếu mã phiên QR để xác nhận đăng nhập.", null);
        }

        var session = await _db.QrLoginSessions
            .Include(q => q.ApprovedByUser)
                .ThenInclude(u => u!.Roles)
            .FirstOrDefaultAsync(q => q.Token == token.Trim());

        if (session == null)
        {
            return (false, "Phiên đăng nhập QR không tồn tại.", null);
        }

        // [BS-QRLOGIN-SINGLEUSE] Chặn dùng lại token lần 2
        if (session.Status == "Claimed")
        {
            return (false, "Chặn tái sử dụng: Mã QR này đã được sử dụng đăng nhập trước đó!", null);
        }

        if (session.ExpiresAt <= DateTime.UtcNow || session.Status == "Expired")
        {
            session.Status = "Expired";
            await _db.SaveChangesAsync();
            return (false, "Phiên đăng nhập QR đã hết hạn.", null);
        }

        if (session.Status != "Approved" || session.ApprovedByUser == null)
        {
            return (false, "Phiên QR chưa được thiết bị Mobile xác nhận duyệt.", null);
        }

        // Chuyển ngay sang Claimed trước khi cấp Cookie (Atomic single-use)
        session.Status = "Claimed";
        await _db.SaveChangesAsync();

        var u = session.ApprovedByUser;
        string primaryRole = u.Roles.FirstOrDefault()?.Name ?? "User";

        var claims = new AuthUserClaimsDto
        {
            UserId = u.Id,
            Email = u.Email ?? string.Empty,
            FullName = u.FullName,
            PrimaryRole = primaryRole,
            TrustScore = u.TrustScore
        };

        return (true, $"Đăng nhập chéo thành công bằng QR Mobile ({u.FullName} - Vai trò: {primaryRole})!", claims);
    }
}
