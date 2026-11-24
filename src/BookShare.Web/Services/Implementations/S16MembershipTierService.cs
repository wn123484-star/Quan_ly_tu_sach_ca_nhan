using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S16MembershipTierService : IS16MembershipTierService
{
    private readonly BookShareDbContext _db;
    private readonly IS17SandboxPaymentService _paymentService;

    public S16MembershipTierService(BookShareDbContext db, IS17SandboxPaymentService paymentService)
    {
        _db = db;
        _paymentService = paymentService;
    }

    public async Task<S16MembershipTierPageViewModel> GetMembershipTierPageAsync(string currentUserId)
    {
        var user = !string.IsNullOrWhiteSpace(currentUserId)
            ? await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            : null;

        if (user == null)
        {
            user = new AspNetUser
            {
                Id = string.Empty,
                FullName = "Khách",
                Email = string.Empty,
                TrustScore = 100,
                LendQuotaBonus = 0,
                CreatedAt = DateTime.UtcNow
            };
        }

        var now = DateTime.UtcNow;
        var myMembership = !string.IsNullOrWhiteSpace(user.Id)
            ? await _db.Memberships
                .AsNoTracking()
                .Where(m => m.UserId == user.Id && m.IsActive)
                .OrderByDescending(m => m.StartDate)
                .FirstOrDefaultAsync()
            : null;

        bool isValidPremium = myMembership != null &&
                              myMembership.Tier == "Premium" &&
                              (!myMembership.ExpiryDate.HasValue || myMembership.ExpiryDate.Value > now);

        var activeLoansCount = !string.IsNullOrWhiteSpace(user.Id)
            ? await _db.Loans
                .AsNoTracking()
                .CountAsync(l => l.LoanRequest.BorrowerId == user.Id && (l.Status == "Active" || l.Status == "Overdue" || l.Status == "Disputed"))
            : 0;

        var allRecords = !string.IsNullOrWhiteSpace(user.Id)
            ? await _db.Memberships
                .AsNoTracking()
                .Include(m => m.User)
                .Where(m => m.UserId == user.Id)
                .OrderByDescending(m => m.StartDate)
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
                .ToListAsync()
            : new List<S16MembershipRecordDto>();

        string activeTier = isValidPremium ? "Premium" : "Free";
        int baseMaxLoans = isValidPremium ? (myMembership?.MaxConcurrentLoans ?? 5) : 2;
        DateTime startDate = myMembership?.StartDate ?? user.CreatedAt;
        DateTime? expiryDate = isValidPremium ? myMembership?.ExpiryDate : null;

        int daysRemaining = expiryDate.HasValue
            ? Math.Max(0, (int)Math.Ceiling((expiryDate.Value - now).TotalDays))
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

    public async Task<(bool Success, string Message, int? CreatedTransactionId, string? RawPayload, string? ExpectedHmacSignature)> SwitchOrExtendMembershipAsync(
        string currentUserId,
        S16SwitchMembershipRequestDto request)
    {
        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (user == null)
        {
            return (false, "Không tìm thấy thông tin tài khoản người dùng.", null, null, null);
        }

        string targetTier = request.TargetTier == "Free" ? "Free" : "Premium";
        int durationMonths = request.DurationMonths switch
        {
            1 => 1,
            6 => 6,
            _ => 12
        };
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
            return (true, $"Đã chuyển hạng thành viên của {user.FullName} về gói Free (Tối đa 2 cuốn + {user.LendQuotaBonus} cuốn thưởng chia sẻ).", null, null, null);
        }

        // Gói Premium: tạo lệnh thanh toán chờ xác thực chữ ký HMAC-SHA512 tại Ví Bảo Lãnh
        decimal feeAmount = durationMonths switch
        {
            1 => 19000m,
            6 => 59000m,
            _ => 99000m
        };

        var (orderOk, _, txId, rawPayload, expectedHmac) = await _paymentService.CreateSandboxOrderAsync(
            currentUserId,
            new S17CreateSandboxOrderDto
            {
                TransactionType = "Membership",
                Amount = feeAmount,
                PaymentGateway = gateway,
                LoanId = null
            });

        if (!orderOk)
        {
            return (false, "Không thể khởi tạo lệnh thanh toán nâng cấp gói Premium.", null, null, null);
        }

        return (true,
            $"Đã khởi tạo lệnh thanh toán #{txId} ({feeAmount:N0} VNĐ cho gói Premium {durationMonths} tháng). Vui lòng xác nhận thanh toán tại Ví Bảo Lãnh để kích hoạt quyền lợi.",
            txId,
            rawPayload,
            expectedHmac);
    }
}
