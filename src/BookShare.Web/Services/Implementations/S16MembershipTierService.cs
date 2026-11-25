using System.Data;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [S16] Dịch vụ quản lý đăng ký, nâng cấp/chuyển đổi giữa 4 hạng Hội viên (có tính phí chuyển đổi minh bạch) và hủy gói về Miễn phí.
/// </summary>
/// <remarks>Đồng bộ thanh toán qua Ví Bảo Lãnh S17 và chống trùng lặp gói đang hiệu lực.</remarks>
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

    public async Task<(bool Success, string Message, S16TierSwitchPreviewResultDto? Preview)> PreviewTierSwitchAsync(
        string currentUserId,
        string targetTier,
        int durationMonths)
    {
        var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (user == null)
        {
            return (false, "Không tìm thấy thông tin tài khoản thành viên.", null);
        }

        var now = DateTime.UtcNow;
        var activePaid = await FindActivePaidMembershipAsync(currentUserId, now);
        var quote = MembershipTierCatalog.CalculateSwitchQuote(activePaid, targetTier, durationMonths, now);
        var currentPolicy = MembershipTierCatalog.GetPolicy(quote.CurrentTier);
        var targetPolicy = MembershipTierCatalog.GetPolicy(quote.TargetTier);
        decimal walletBalance = await _paymentService.GetWalletBalanceAsync(currentUserId);

        string formula = quote.IsSameActiveTier
            ? $"Bạn đang sở hữu gói {currentPolicy.DisplayName} (còn {quote.RemainingDays} ngày). Không thể mua trùng gói đang hoạt động."
            : quote.IsSwitch
                ? $"Giá gói {targetPolicy.DisplayName} ({quote.DurationMonths}T): {quote.NewPlanPrice:N0}đ − Hoàn trừ ngày dư gói {currentPolicy.Tier}: {quote.ProrationCredit:N0}đ + Lệ phí chuyển gói: {quote.SwitchFee:N0}đ = {quote.TotalPayable:N0}đ ảo."
                : $"Đăng ký mới gói {targetPolicy.DisplayName} ({quote.DurationMonths} tháng): {quote.TotalPayable:N0}đ ảo (miễn phí chuyển đổi).";

        var preview = new S16TierSwitchPreviewResultDto
        {
            CurrentTier = quote.CurrentTier,
            CurrentTierDisplayName = currentPolicy.DisplayName,
            TargetTier = quote.TargetTier,
            TargetTierDisplayName = targetPolicy.DisplayName,
            DurationMonths = quote.DurationMonths,
            IsSwitch = quote.IsSwitch,
            IsSameActiveTier = quote.IsSameActiveTier,
            RemainingDays = quote.RemainingDays,
            NewPlanPrice = quote.NewPlanPrice,
            ProrationCredit = quote.ProrationCredit,
            SwitchFee = quote.SwitchFee,
            TotalPayable = quote.TotalPayable,
            WalletBalance = walletBalance,
            FormulaNote = formula
        };

        if (quote.IsSameActiveTier)
        {
            return (false, formula, preview);
        }

        return (true, formula, preview);
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
        if (targetTier == "Free")
        {
            var (cancelOk, cancelMsg) = await CancelMembershipAsync(currentUserId, "Người dùng chủ động chuyển về gói Free");
            return (cancelOk, cancelMsg, null, null, null);
        }

        string? cleanIdemKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim()[..Math.Min(100, request.IdempotencyKey.Trim().Length)];

        if (!string.IsNullOrWhiteSpace(cleanIdemKey))
        {
            bool dupIdem = await _db.Transactions.AsNoTracking().AnyAsync(t => t.IdempotencyKey == cleanIdemKey);
            if (dupIdem)
            {
                return (false, "Yêu cầu thanh toán này đã được tiếp nhận trước đó. Vui lòng không bấm lặp lại.", null, null, null);
            }
        }

        var now = DateTime.UtcNow;
        var activePaid = await FindActivePaidMembershipAsync(currentUserId, now);
        var policy = MembershipTierCatalog.GetPolicy(targetTier);
        int durationMonths = MembershipTierCatalog.NormalizeDurationMonths(request.DurationMonths);
        var quote = MembershipTierCatalog.CalculateSwitchQuote(activePaid, targetTier, durationMonths, now);

        // Chốt 1: Cấm mua trùng gói trả phí đang còn hiệu lực
        if (quote.IsSameActiveTier)
        {
            string expText = activePaid?.ExpiryDate?.ToString("dd/MM/yyyy") ?? "còn hiệu lực";
            return (false,
                $"Bạn đang sở hữu gói {policy.DisplayName} ({policy.Tier}) còn hiệu lực đến {expText} (còn {quote.RemainingDays} ngày), không thể mua trùng gói đang hoạt động. Bạn có thể chọn Chuyển sang gói khác (lệ phí chuyển đổi {MembershipTierCatalog.TierSwitchFee:N0}đ ảo) hoặc Hủy gói hiện tại.",
                null, null, null);
        }

        string paymentMode = string.Equals(request.PaymentMode, "InstantWallet", StringComparison.OrdinalIgnoreCase)
            ? "InstantWallet"
            : "QrInvoice";

        decimal feeAmount = quote.TotalPayable;
        string txType = quote.IsSwitch ? "MembershipSwitch" : "Membership";
        string actionType = quote.IsSwitch ? "Switch" : "Register";
        string invoiceDesc = quote.IsSwitch
            ? $"Chuyển gói {quote.CurrentTier} -> {policy.Tier} ({durationMonths}T) | Gói mới {quote.NewPlanPrice:N0}đ - Hoàn dư {quote.ProrationCredit:N0}đ + Phí chuyển {quote.SwitchFee:N0}đ"
            : $"Đăng ký gói {policy.DisplayName} ({policy.Tier} — {durationMonths} tháng)";

        if (paymentMode == "InstantWallet")
        {
            await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var wallet = await _db.UserWallets
                    .FromSqlRaw("SELECT * FROM [dbo].[UserWallets] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {0}", currentUserId)
                    .FirstOrDefaultAsync();

                decimal balBefore = wallet?.Balance ?? 0m;
                if (wallet == null || balBefore < feeAmount)
                {
                    decimal shortfall = feeAmount - balBefore;
                    await dbTx.RollbackAsync();
                    return (false,
                        $"Số dư Ví Tiền Ảo hiện có {balBefore:N0}đ (cần {feeAmount:N0}đ, còn thiếu {shortfall:N0}đ). Hãy vào Ví Tiền Ảo nạp thêm miễn phí ngay!",
                        null, null, null);
                }

                wallet.Balance -= feeAmount;
                wallet.TotalSpent += feeAmount;
                wallet.UpdatedAt = now;
                decimal balAfter = wallet.Balance;

                var tx = new Transaction
                {
                    UserId = currentUserId,
                    LoanId = null,
                    TransactionType = txType,
                    ActionType = actionType,
                    Amount = feeAmount,
                    SwitchFee = quote.SwitchFee,
                    ProrationCredit = quote.ProrationCredit,
                    BalanceBefore = balBefore,
                    BalanceAfter = balAfter,
                    IdempotencyKey = cleanIdemKey,
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

                // Vô hiệu hóa toàn bộ gói cũ đang hoạt động & hủy hóa đơn Pending cũ để đảm bảo duy nhất 1 gói active
                var activeRecords = await _db.Memberships
                    .Where(m => m.UserId == currentUserId && m.IsActive)
                    .ToListAsync();
                foreach (var oldRec in activeRecords)
                {
                    oldRec.IsActive = false;
                    if (!oldRec.ExpiryDate.HasValue || oldRec.ExpiryDate.Value > now)
                    {
                        oldRec.ExpiryDate = now;
                    }
                }

                _db.Memberships.Add(new Membership
                {
                    UserId = currentUserId,
                    Tier = policy.Tier,
                    MaxConcurrentLoans = policy.MaxConcurrentLoans,
                    StartDate = now,
                    ExpiryDate = now.AddMonths(durationMonths),
                    IsActive = true
                });

                var stalePending = await _db.Transactions
                    .Where(t => t.UserId == currentUserId
                        && t.PaymentStatus == "Pending"
                        && (t.TransactionType == "Membership" || t.TransactionType == "MembershipSwitch"))
                    .ToListAsync();
                foreach (var p in stalePending)
                {
                    p.PaymentStatus = "Failed";
                }

                _db.Notifications.Add(new Notification
                {
                    UserId = currentUserId,
                    Title = quote.IsSwitch
                        ? $"Chuyển thành công sang gói {policy.DisplayName}"
                        : $"Kích hoạt thành công gói {policy.DisplayName}",
                    Content = quote.IsSwitch
                        ? $"Đã chuyển từ gói {quote.CurrentTier} sang {policy.DisplayName} ({durationMonths}T). Tổng trừ {feeAmount:N0}đ (gồm lệ phí chuyển {quote.SwitchFee:N0}đ, đã hoàn trừ {quote.ProrationCredit:N0}đ gói cũ). Số dư ví: {balBefore:N0}đ -> {balAfter:N0}đ."
                        : $"Đã trừ {feeAmount:N0}đ từ Ví Tiền Ảo (Số dư: {balBefore:N0}đ -> {balAfter:N0}đ). Hạn mức mới: {policy.MaxConcurrentLoans} cuốn (+{user.LendQuotaBonus} thưởng P2P), tối đa {policy.MaxBorrowDays} ngày/lượt.",
                    Type = "System",
                    IsRead = false,
                    EmailSentViaResend = false,
                    CreatedAt = now
                });

                await _db.SaveChangesAsync();
                await dbTx.CommitAsync();

                string okMsg = quote.IsSwitch
                    ? $"Chuyển gói thành công sang {policy.DisplayName} ({durationMonths} tháng) bằng Ví Tiền Ảo! Tổng trừ {feeAmount:N0}đ (lệ phí chuyển {quote.SwitchFee:N0}đ, hoàn trừ {quote.ProrationCredit:N0}đ), số dư còn lại: {balAfter:N0}đ."
                    : $"Đăng ký thành công gói {policy.DisplayName} ({durationMonths} tháng) bằng Ví Tiền Ảo! Đã trừ {feeAmount:N0}đ, số dư còn lại: {balAfter:N0}đ.";

                return (true, okMsg, null, null, null);
            }
            catch
            {
                try { await dbTx.RollbackAsync(); } catch { /* giao dịch đã đóng hoặc đã rollback */ }
                throw;
            }
        }

        // Chế độ tạo hóa đơn QR Thanh Toán (có tái sử dụng hóa đơn Pending chống spam)
        var (orderOk, orderMsg, txId, rawPayload, expectedHmac) = await _paymentService.CreateSandboxOrderAsync(
            currentUserId,
            new S17CreateSandboxOrderDto
            {
                TransactionType = txType,
                ActionType = actionType,
                Amount = feeAmount,
                SwitchFee = quote.SwitchFee,
                ProrationCredit = quote.ProrationCredit,
                PaymentGateway = "QrPay",
                LoanId = null,
                TargetTier = policy.Tier,
                DurationMonths = durationMonths,
                Description = invoiceDesc,
                IdempotencyKey = cleanIdemKey
            });

        if (!orderOk)
        {
            return (false, string.IsNullOrWhiteSpace(orderMsg) ? $"Không thể khởi tạo hóa đơn cho gói {policy.DisplayName}." : orderMsg, null, null, null);
        }

        return (true,
            $"Đã tạo hóa đơn #TX-{txId} ({feeAmount:N0}đ ảo — {invoiceDesc}). Quét mã QR hoặc mở Ví Tiền Ảo để xác nhận thanh toán!",
            txId,
            rawPayload,
            expectedHmac);
    }

    public async Task<(bool Success, string Message)> CancelMembershipAsync(
        string currentUserId,
        string? reason = null)
    {
        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản thành viên.");
        }

        var now = DateTime.UtcNow;
        var activePaid = await FindActivePaidMembershipAsync(currentUserId, now);
        if (activePaid == null)
        {
            return (false, "Bạn hiện đang ở gói Free (miễn phí trọn đời), không có gói trả phí nào đang hoạt động để hủy.");
        }

        // Chốt an toàn hạn mức mượn khi hạ về gói Free
        int freeLimit = MembershipTierCatalog.Free.MaxConcurrentLoans + user.LendQuotaBonus;
        int activeLoansCount = await _db.Loans
            .AsNoTracking()
            .CountAsync(l => l.LoanRequest.BorrowerId == currentUserId && (l.Status == "Active" || l.Status == "Overdue" || l.Status == "Disputed"));

        if (activeLoansCount > freeLimit)
        {
            return (false,
                $"Bạn hiện đang mượn {activeLoansCount} cuốn sách, vượt quá hạn mức gói Free ({freeLimit} cuốn). Vui lòng hoàn trả bớt {activeLoansCount - freeLimit} cuốn trước khi hủy gói {activePaid.Tier}!");
        }

        string cancelledTier = activePaid.Tier;
        string cleanReason = string.IsNullOrWhiteSpace(reason) ? "Thành viên chủ động hủy gói" : reason.Trim();

        await using var dbTx = await _db.Database.BeginTransactionAsync();
        try
        {
            var allActive = await _db.Memberships
                .Where(m => m.UserId == currentUserId && m.IsActive)
                .ToListAsync();
            foreach (var rec in allActive)
            {
                rec.IsActive = false;
                if (!rec.ExpiryDate.HasValue || rec.ExpiryDate.Value > now)
                {
                    rec.ExpiryDate = now;
                }
            }

            _db.Memberships.Add(new Membership
            {
                UserId = currentUserId,
                Tier = "Free",
                MaxConcurrentLoans = MembershipTierCatalog.Free.MaxConcurrentLoans,
                StartDate = now,
                ExpiryDate = null,
                IsActive = true
            });

            // Hủy sạch các hóa đơn đăng ký/chuyển gói đang Pending để tránh quét nhầm sau khi hủy
            var stalePending = await _db.Transactions
                .Where(t => t.UserId == currentUserId
                    && t.PaymentStatus == "Pending"
                    && (t.TransactionType == "Membership" || t.TransactionType == "MembershipSwitch"))
                .ToListAsync();
            foreach (var p in stalePending)
            {
                p.PaymentStatus = "Failed";
            }

            var wallet = await _db.UserWallets.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == currentUserId);
            decimal bal = wallet?.Balance ?? 0m;

            string cancelDesc = $"Hủy gói {cancelledTier} về Free ({cleanReason})";
            if (cancelDesc.Length > 290) cancelDesc = cancelDesc[..290];

            var cancelTx = new Transaction
            {
                UserId = currentUserId,
                LoanId = null,
                TransactionType = "MembershipCancel",
                ActionType = "Cancel",
                Amount = 0m,
                SwitchFee = 0m,
                ProrationCredit = 0m,
                BalanceBefore = bal,
                BalanceAfter = bal,
                PaymentGateway = "VirtualWallet",
                PaymentStatus = "Completed",
                TargetTier = "Free",
                DurationMonths = null,
                Description = cancelDesc,
                CreatedAt = now
            };
            _db.Transactions.Add(cancelTx);
            await _db.SaveChangesAsync();

            string canonical = _paymentService.BuildCanonicalPayload(cancelTx.Id, currentUserId, cancelTx.TransactionType, 0m, cancelTx.PaymentGateway, null);
            cancelTx.HmacSignature = _paymentService.ComputeHmacSha512(canonical);

            _db.Notifications.Add(new Notification
            {
                UserId = currentUserId,
                Title = $"Đã hủy gói hội viên {cancelledTier}",
                Content = $"Gói {cancelledTier} của bạn đã được hủy theo yêu cầu. Tài khoản hiện quay về gói Free (Tối đa {freeLimit} cuốn, 14 ngày/lượt). Số dư Ví Tiền Ảo giữ nguyên {bal:N0}đ.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            await dbTx.CommitAsync();

            return (true, $"Đã hủy gói {cancelledTier} thành công! Tài khoản đã chuyển về gói Free (Tối đa {freeLimit} cuốn) và tự động hủy các hóa đơn chờ cũ.");
        }
        catch
        {
            try { await dbTx.RollbackAsync(); } catch { /* giao dịch đã đóng hoặc đã rollback */ }
            throw;
        }
    }

    private async Task<Membership?> FindActivePaidMembershipAsync(string userId, DateTime nowUtc)
    {
        var activeList = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive && m.Tier != "Free" && (!m.ExpiryDate.HasValue || m.ExpiryDate.Value > nowUtc))
            .OrderByDescending(m => m.StartDate)
            .ToListAsync();

        return activeList
            .OrderByDescending(m => MembershipTierCatalog.GetPolicy(m.Tier).PriorityRank)
            .FirstOrDefault();
    }
}
