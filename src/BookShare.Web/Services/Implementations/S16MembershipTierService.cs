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
        var userMemberships = !string.IsNullOrWhiteSpace(user.Id)
            ? await _db.Memberships
                .AsNoTracking()
                .Where(m => m.UserId == user.Id && m.IsActive)
                .OrderByDescending(m => m.StartDate)
                .ToListAsync()
            : new List<Membership>();

        var activePolicy = MembershipTierCatalog.GetEffectivePolicy(userMemberships, now);
        var activeRecord = userMemberships
            .FirstOrDefault(m =>
                m.Tier.Equals(activePolicy.Tier, StringComparison.OrdinalIgnoreCase) &&
                (!m.ExpiryDate.HasValue || m.ExpiryDate.Value > now));

        decimal walletBalance = !string.IsNullOrWhiteSpace(user.Id)
            ? await _paymentService.GetWalletBalanceAsync(user.Id)
            : 0m;

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

        int baseMaxLoans = activeRecord != null
            ? Math.Max(activePolicy.MaxConcurrentLoans, activeRecord.MaxConcurrentLoans)
            : activePolicy.MaxConcurrentLoans;
        DateTime startDate = activeRecord?.StartDate ?? user.CreatedAt;
        DateTime? expiryDate = activePolicy.Tier == "Free" ? null : activeRecord?.ExpiryDate;

        int daysRemaining = expiryDate.HasValue
            ? Math.Max(0, (int)Math.Ceiling((expiryDate.Value - now).TotalDays))
            : 3650;

        return new S16MembershipTierPageViewModel
        {
            CurrentUserId = user.Id,
            CurrentUserName = user.FullName,
            CurrentUserEmail = user.Email ?? string.Empty,
            CurrentTrustScore = user.TrustScore,
            WalletBalance = walletBalance,
            ActiveTier = activePolicy.Tier,
            ActiveTierDisplayName = activePolicy.DisplayName,
            BaseMaxConcurrentLoans = baseMaxLoans,
            MaxBorrowDays = activePolicy.MaxBorrowDays,
            MaxRenewals = activePolicy.MaxRenewals,
            AllowedBooksSummary = activePolicy.AllowedBooksSummary,
            LendQuotaBonus = user.LendQuotaBonus,
            CurrentActiveLoansCount = activeLoansCount,
            StartDate = startDate,
            ExpiryDate = expiryDate,
            DaysRemaining = daysRemaining,
            TierPlans = MembershipTierCatalog.ToPlanDtos(),
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

        string targetTier = MembershipTierCatalog.NormalizeTier(request.TargetTier);
        var policy = MembershipTierCatalog.GetPolicy(targetTier);
        int durationMonths = MembershipTierCatalog.NormalizeDurationMonths(request.DurationMonths);
        string paymentMode = string.Equals(request.PaymentMode, "InstantWallet", StringComparison.OrdinalIgnoreCase)
            ? "InstantWallet"
            : "QrInvoice";
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
                existingMembership.MaxConcurrentLoans = MembershipTierCatalog.Free.MaxConcurrentLoans;
                existingMembership.ExpiryDate = null;
            }
            else
            {
                _db.Memberships.Add(new Membership
                {
                    UserId = currentUserId,
                    Tier = "Free",
                    MaxConcurrentLoans = MembershipTierCatalog.Free.MaxConcurrentLoans,
                    StartDate = now,
                    ExpiryDate = null,
                    IsActive = true
                });
            }

            await _db.SaveChangesAsync();
            return (true, $"Đã chuyển hạng thành viên của {user.FullName} về gói Free (Tối đa 2 cuốn + {user.LendQuotaBonus} cuốn thưởng chia sẻ).", null, null, null);
        }

        decimal feeAmount = MembershipTierCatalog.GetPrice(targetTier, durationMonths);
        string invoiceDesc = $"Đăng ký / Gia hạn gói {policy.DisplayName} ({policy.Tier} — {durationMonths} tháng)";

        if (paymentMode == "InstantWallet")
        {
            var wallet = await _db.UserWallets.FirstOrDefaultAsync(w => w.UserId == currentUserId);
            decimal currentBal = wallet?.Balance ?? 0m;
            if (wallet == null || currentBal < feeAmount)
            {
                decimal shortfall = feeAmount - currentBal;
                return (false,
                    $"Số dư Ví Tiền Ảo hiện có {currentBal:N0}đ (cần {feeAmount:N0}đ, còn thiếu {shortfall:N0}đ). Hãy vào Ví Tiền Ảo nạp thêm miễn phí ngay!",
                    null, null, null);
            }

            wallet.Balance -= feeAmount;
            wallet.TotalSpent += feeAmount;
            wallet.UpdatedAt = now;

            var tx = new Transaction
            {
                UserId = currentUserId,
                LoanId = null,
                TransactionType = "Membership",
                Amount = feeAmount,
                PaymentGateway = "VirtualWallet",
                PaymentStatus = "Completed",
                TargetTier = policy.Tier,
                DurationMonths = durationMonths,
                Description = invoiceDesc,
                CreatedAt = now
            };
            _db.Transactions.Add(tx);
            await _db.SaveChangesAsync();

            string canonical = _paymentService.BuildCanonicalPayload(tx.Id, currentUserId, tx.TransactionType, feeAmount, tx.PaymentGateway, null);
            tx.HmacSignature = _paymentService.ComputeHmacSha512(canonical);

            if (existingMembership != null)
            {
                bool sameTierStillValid = existingMembership.Tier.Equals(policy.Tier, StringComparison.OrdinalIgnoreCase)
                    && existingMembership.ExpiryDate.HasValue
                    && existingMembership.ExpiryDate.Value > now;
                existingMembership.Tier = policy.Tier;
                existingMembership.MaxConcurrentLoans = policy.MaxConcurrentLoans;
                var baseDate = sameTierStillValid ? existingMembership.ExpiryDate!.Value : now;
                existingMembership.ExpiryDate = baseDate.AddMonths(durationMonths);
            }
            else
            {
                _db.Memberships.Add(new Membership
                {
                    UserId = currentUserId,
                    Tier = policy.Tier,
                    MaxConcurrentLoans = policy.MaxConcurrentLoans,
                    StartDate = now,
                    ExpiryDate = now.AddMonths(durationMonths),
                    IsActive = true
                });
            }

            _db.Notifications.Add(new Notification
            {
                UserId = currentUserId,
                Title = $"Kích hoạt thành công gói {policy.DisplayName}",
                Content = $"Đã trừ {feeAmount:N0}đ từ Ví Tiền Ảo. Hạn mức mượn mới: {policy.MaxConcurrentLoans} cuốn (+{user.LendQuotaBonus} thưởng P2P), thời hạn mượn tối đa {policy.MaxBorrowDays} ngày/lượt. Số dư còn lại: {wallet.Balance:N0}đ.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            return (true,
                $"Nâng cấp thành công gói {policy.DisplayName} ({durationMonths} tháng) bằng Ví Tiền Ảo! Đã trừ {feeAmount:N0}đ, số dư còn lại: {wallet.Balance:N0}đ.",
                null, null, null);
        }

        // Chế độ tạo hóa đơn QR Thanh Toán (Quét bằng camera trên Web / Android App hoặc xác nhận tại Ví Tiền Ảo)
        var (orderOk, _, txId, rawPayload, expectedHmac) = await _paymentService.CreateSandboxOrderAsync(
            currentUserId,
            new S17CreateSandboxOrderDto
            {
                TransactionType = "Membership",
                Amount = feeAmount,
                PaymentGateway = "QrPay",
                LoanId = null,
                TargetTier = policy.Tier,
                DurationMonths = durationMonths,
                Description = invoiceDesc
            });

        if (!orderOk)
        {
            return (false, $"Không thể khởi tạo hóa đơn thanh toán cho gói {policy.DisplayName}.", null, null, null);
        }

        return (true,
            $"Đã tạo hóa đơn #TX-{txId} ({feeAmount:N0}đ ảo — {invoiceDesc}). Quét mã QR hoặc mở Ví Tiền Ảo để xác nhận thanh toán!",
            txId,
            rawPayload,
            expectedHmac);
    }
}
