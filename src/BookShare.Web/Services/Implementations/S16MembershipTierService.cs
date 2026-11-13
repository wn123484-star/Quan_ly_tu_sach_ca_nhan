using System.Security.Cryptography;
using System.Text;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S16MembershipTierService : IS16MembershipTierService
{
    private readonly BookShareDbContext _db;
    private readonly IConfiguration _config;

    public S16MembershipTierService(BookShareDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<S16MembershipTierPageViewModel> GetMembershipTierPageAsync(string currentUserId)
    {
        var user = await _db.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId)
            ?? await _db.AspNetUsers.AsNoTracking().FirstAsync(u => u.Id == "u-user-01");

        var myMembership = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.IsActive)
            .OrderByDescending(m => m.StartDate)
            .FirstOrDefaultAsync();

        var activeLoansCount = await _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest)
            .CountAsync(l => l.LoanRequest.BorrowerId == user.Id && (l.Status == "Active" || l.Status == "Overdue"));

        var allRecords = await _db.Memberships
            .AsNoTracking()
            .Include(m => m.User)
            .OrderByDescending(m => m.Tier == "Premium")
            .ThenByDescending(m => m.StartDate)
            .Select(m => new S16MembershipRecordDto
            {
                MembershipId = m.Id,
                UserId = m.UserId,
                UserFullName = m.User.FullName,
                UserEmail = m.User.Email ?? string.Empty,
                Tier = m.Tier,
                MaxConcurrentLoans = m.MaxConcurrentLoans,
                LendQuotaBonus = m.User.LendQuotaBonus,
                StartDate = m.StartDate,
                ExpiryDate = m.ExpiryDate,
                IsActive = m.IsActive
            })
            .ToListAsync();

        string activeTier = myMembership?.Tier ?? "Free";
        int baseMaxLoans = myMembership?.MaxConcurrentLoans ?? (activeTier == "Premium" ? 5 : 2);
        DateTime startDate = myMembership?.StartDate ?? user.CreatedAt;
        DateTime? expiryDate = myMembership?.ExpiryDate;

        int daysRemaining = expiryDate.HasValue
            ? Math.Max(0, (int)Math.Ceiling((expiryDate.Value - DateTime.UtcNow).TotalDays))
            : 3650;

        return new S16MembershipTierPageViewModel
        {
            CurrentUserId = user.Id,
            CurrentUserName = user.FullName,
            CurrentUserEmail = user.Email ?? string.Empty,
            CurrentTrustScore = user.TrustScore,
            ActiveTier = activeTier,
            BaseMaxConcurrentLoans = baseMaxLoans,
            LendQuotaBonus = user.LendQuotaBonus,
            CurrentActiveLoansCount = activeLoansCount,
            StartDate = startDate,
            ExpiryDate = expiryDate,
            DaysRemaining = daysRemaining,
            AllMemberships = allRecords
        };
    }

    public async Task<(bool Success, string Message, int? CreatedTransactionId)> SwitchOrExtendMembershipAsync(
        string currentUserId,
        S16SwitchMembershipRequestDto request)
    {
        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (user == null)
        {
            return (false, "Không tìm thấy thông tin tài khoản người dùng.", null);
        }

        string targetTier = request.TargetTier == "Free" ? "Free" : "Premium";
        int durationMonths = Math.Clamp(request.DurationMonths, 1, 24);
        string gateway = request.PaymentGateway == "MoMoSandbox" ? "MoMoSandbox" : "VNPaySandbox";
        var now = DateTime.UtcNow;

        var existingMembership = await _db.Memberships
            .Where(m => m.UserId == currentUserId && m.IsActive)
            .OrderByDescending(m => m.StartDate)
            .FirstOrDefaultAsync();

        if (targetTier == "Free")
        {
            if (existingMembership != null)
            {
                existingMembership.Tier = "Free";
                existingMembership.MaxConcurrentLoans = 2;
                existingMembership.ExpiryDate = null;
            }
            else
            {
                _db.Memberships.Add(new Membership
                {
                    UserId = currentUserId,
                    Tier = "Free",
                    MaxConcurrentLoans = 2,
                    StartDate = now,
                    ExpiryDate = null,
                    IsActive = true
                });
            }

            await _db.SaveChangesAsync();
            return (true, $"Đã chuyển gói thành viên của {user.FullName} về gói Free (Tối đa 2 cuốn + {user.LendQuotaBonus} cuốn thưởng P2P).", null);
        }

        // Gói Premium: 5 cuốn cơ bản + ưu tiên hàng chờ S11
        decimal feeAmount = durationMonths switch
        {
            1 => 19000m,
            6 => 59000m,
            _ => 99000m
        };

        if (existingMembership != null)
        {
            existingMembership.Tier = "Premium";
            existingMembership.MaxConcurrentLoans = 5;
            var baseExpiry = existingMembership.ExpiryDate.HasValue && existingMembership.ExpiryDate.Value > now
                ? existingMembership.ExpiryDate.Value
                : now;
            existingMembership.ExpiryDate = baseExpiry.AddMonths(durationMonths);
        }
        else
        {
            _db.Memberships.Add(new Membership
            {
                UserId = currentUserId,
                Tier = "Premium",
                MaxConcurrentLoans = 5,
                StartDate = now,
                ExpiryDate = now.AddMonths(durationMonths),
                IsActive = true
            });
        }

        // Tạo bản ghi giao dịch kèm chữ ký HMAC-SHA512 vào bảng Transactions
        string secret = _config["VNPay:HashSecret"] is { Length: > 0 } s ? s : "BOOKSHARE_VNPAY_SANDBOX_SECRET_2026";
        string rawPayload = $"MEMBERSHIP|USER:{currentUserId}|TIER:Premium|MONTHS:{durationMonths}|AMOUNT:{feeAmount:0}|GW:{gateway}|TS:{now:yyyyMMddHHmmss}";
        byte[] hmacBytes = HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawPayload));
        string hmacHex = Convert.ToHexString(hmacBytes).ToLowerInvariant();

        var tx = new Transaction
        {
            UserId = currentUserId,
            LoanId = null,
            TransactionType = "Membership",
            Amount = feeAmount,
            PaymentGateway = gateway,
            PaymentStatus = "Completed",
            HmacSignature = hmacHex[..64],
            CreatedAt = now
        };
        _db.Transactions.Add(tx);

        _db.Notifications.Add(new Notification
        {
            UserId = currentUserId,
            Title = $"Đã kích hoạt gói thành viên Premium ({durationMonths} tháng)",
            Content = $"Tài khoản {user.FullName} đã được nâng hạn mức lên 5 cuốn cơ bản (+{user.LendQuotaBonus} cuốn thưởng P2P) và ưu tiên đầu hàng chờ S11.",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        int totalQuota = 5 + user.LendQuotaBonus;
        return (true,
            $"Đã kích hoạt/gia hạn gói Premium ({durationMonths} tháng) cho {user.FullName}! Tổng hạn mức mượn hiện tại: {totalQuota} cuốn (5 cuốn Premium + {user.LendQuotaBonus} cuốn thưởng P2P). Đã ghi nhận giao dịch #{tx.Id}.",
            tx.Id);
    }
}
