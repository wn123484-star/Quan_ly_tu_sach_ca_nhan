using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BookShare.Web.Services.Implementations;

public class S17SandboxPaymentService : IS17SandboxPaymentService
{
    private const decimal MinTopUpAmount = 1_000m;
    private const decimal MaxTopUpAmount = 10_000_000m;
    private const decimal MaxWalletBalanceCap = 50_000_000m;
    private const int MaxTopUpsPerMinute = 5;
    private const int MaxConcurrentPendingInvoices = 3;

    private readonly BookShareDbContext _db;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;

    public S17SandboxPaymentService(BookShareDbContext db, IConfiguration config, IMemoryCache cache)
    {
        _db = db;
        _config = config;
        _cache = cache;
    }

    public async Task<decimal> GetWalletBalanceAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return 0m;
        }

        var wallet = await _db.UserWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId);

        return wallet?.Balance ?? 0m;
    }

    public async Task<S17SandboxPaymentPageViewModel> GetSandboxPaymentPageAsync(
        string currentUserId,
        string currentUserRole = "User",
        int? selectedTxId = null)
    {
        var user = !string.IsNullOrWhiteSpace(currentUserId)
            ? await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            : null;

        if (user == null)
        {
            return new S17SandboxPaymentPageViewModel
            {
                CurrentUserId = string.Empty,
                CurrentUserName = "Khách",
                CurrentTier = "Free"
            };
        }

        bool isPrivileged = currentUserRole is "Librarian" or "Admin";

        var wallet = await _db.UserWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == user.Id);

        var memberships = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.IsActive)
            .ToListAsync();
        var activePolicy = MembershipTierCatalog.GetEffectivePolicy(memberships, DateTime.UtcNow);

        var fineQuery = _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.Borrower)
            .Where(l => l.FineAmount > 0);

        if (!isPrivileged)
        {
            fineQuery = fineQuery.Where(l => l.LoanRequest.BorrowerId == user.Id);
        }

        var unpaidLoans = await fineQuery
            .OrderByDescending(l => l.FineAmount)
            .Select(l => new S17UnpaidFineLoanDto
            {
                LoanId = l.Id,
                Barcode = l.LoanRequest.BookCopy.Barcode,
                BookTitle = l.LoanRequest.BookCopy.Book.Title,
                BorrowerName = l.LoanRequest.Borrower.FullName,
                FineAmount = l.FineAmount
            })
            .ToListAsync();

        var txQuery = _db.Transactions
            .AsNoTracking()
            .Include(t => t.User)
            .AsQueryable();

        if (!isPrivileged)
        {
            txQuery = txQuery.Where(t => t.UserId == user.Id);
        }

        var txs = await txQuery
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new S17TransactionItemDto
            {
                TransactionId = t.Id,
                UserId = t.UserId,
                UserFullName = t.User.FullName,
                LoanId = t.LoanId,
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                PaymentGateway = t.PaymentGateway,
                PaymentStatus = t.PaymentStatus,
                TargetTier = t.TargetTier,
                DurationMonths = t.DurationMonths,
                Description = t.Description,
                ActionType = t.ActionType,
                SwitchFee = t.SwitchFee,
                ProrationCredit = t.ProrationCredit,
                BalanceBefore = t.BalanceBefore,
                BalanceAfter = t.BalanceAfter,
                HmacSignature = t.HmacSignature,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        int? pendingTxId = null;
        string? pendingQrPayload = null;
        string? pendingDesc = null;
        decimal pendingAmount = 0m;
        string? pendingRaw = null;
        string? pendingHmac = null;

        if (selectedTxId.HasValue)
        {
            var selectedTx = txs.FirstOrDefault(t => t.TransactionId == selectedTxId.Value && t.PaymentStatus == "Pending");
            if (selectedTx != null)
            {
                pendingTxId = selectedTx.TransactionId;
                pendingQrPayload = selectedTx.QrPayload;
                pendingDesc = ResolveDescription(selectedTx.TransactionType, selectedTx.TargetTier, selectedTx.DurationMonths, selectedTx.LoanId, selectedTx.Description);
                pendingAmount = selectedTx.Amount;
                pendingRaw = BuildCanonicalPayload(
                    selectedTx.TransactionId,
                    selectedTx.UserId,
                    selectedTx.TransactionType,
                    selectedTx.Amount,
                    selectedTx.PaymentGateway,
                    selectedTx.LoanId);
                pendingHmac = ComputeHmacSha512(pendingRaw);
            }
        }

        return new S17SandboxPaymentPageViewModel
        {
            CurrentUserId = user.Id,
            CurrentUserName = user.FullName,
            CurrentTier = activePolicy.Tier,
            WalletBalance = wallet?.Balance ?? 0m,
            TotalToppedUp = wallet?.TotalToppedUp ?? 0m,
            TotalSpent = wallet?.TotalSpent ?? 0m,
            TotalCompletedAmount = txs.Where(t => t.PaymentStatus == "Completed" && t.TransactionType != "WalletTopUp").Sum(t => t.Amount),
            TotalPendingAmount = txs.Where(t => t.PaymentStatus == "Pending").Sum(t => t.Amount),
            TotalVerifiedHmacCount = txs.Count(t => !string.IsNullOrWhiteSpace(t.HmacSignature)),
            SelectedPendingTxId = pendingTxId,
            SelectedQrPayload = pendingQrPayload,
            SelectedDescription = pendingDesc,
            SelectedPendingAmount = pendingAmount,
            SelectedRawPayload = pendingRaw,
            SelectedExpectedHmac = pendingHmac,
            UnpaidFineLoans = unpaidLoans,
            Transactions = txs
        };
    }

    public async Task<(bool Success, string Message, decimal NewBalance, int TransactionId, bool RateLimited)> TopUpVirtualWalletAsync(
        string currentUserId,
        decimal amount,
        string? note = null,
        string? idempotencyKey = null)
    {
        if (amount < MinTopUpAmount || amount > MaxTopUpAmount)
        {
            return (false, $"Số tiền ảo mỗi lần nạp phải từ {MinTopUpAmount:N0}đ đến {MaxTopUpAmount:N0}đ ảo.", await GetWalletBalanceAsync(currentUserId), 0, false);
        }

        string? cleanIdemKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? null
            : idempotencyKey.Trim()[..Math.Min(100, idempotencyKey.Trim().Length)];

        if (!string.IsNullOrWhiteSpace(cleanIdemKey))
        {
            var existingIdem = await _db.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IdempotencyKey == cleanIdemKey);
            if (existingIdem != null)
            {
                return (false,
                    $"Lượt nạp tiền này đã được ghi nhận (Mã biên lai #TX-{existingIdem.Id}). Hệ thống tự động chặn ghi lặp để đảm bảo minh bạch sổ cái.",
                    await GetWalletBalanceAsync(currentUserId),
                    existingIdem.Id,
                    false);
            }
        }

        // Chống spam nạp tiền liên tục (Sliding window 5 lần / 60 giây mỗi tài khoản)
        string rateKey = $"bookshare:topup-rate:{currentUserId}";
        var now = DateTime.UtcNow;
        var timestamps = _cache.GetOrCreate(rateKey, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(2);
            return new List<DateTime>();
        }) ?? new List<DateTime>();

        lock (timestamps)
        {
            timestamps.RemoveAll(ts => ts < now.AddSeconds(-60));
            if (timestamps.Count >= MaxTopUpsPerMinute)
            {
                return (false,
                    $"Bạn đã nạp tiền ảo {MaxTopUpsPerMinute} lần trong 1 phút gần đây. Vui lòng chờ giây lát để tránh gửi trùng lặp!",
                    0m,
                    0,
                    true);
            }
        }

        var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản thành viên.", 0m, 0, false);
        }

        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var wallet = await EnsureWalletLockedAsync(currentUserId, now);
            decimal balBefore = wallet.Balance;

            if (balBefore + amount > MaxWalletBalanceCap)
            {
                await dbTx.RollbackAsync();
                return (false,
                    $"Số dư Ví Tiền Ảo tối đa cho phép là {MaxWalletBalanceCap:N0}đ ảo (hiện có {balBefore:N0}đ). Hãy dùng số dư hiện tại trước khi nạp thêm!",
                    balBefore,
                    0,
                    false);
            }

            wallet.Balance += amount;
            wallet.TotalToppedUp += amount;
            wallet.UpdatedAt = now;
            decimal balAfter = wallet.Balance;

            string cleanDesc = string.IsNullOrWhiteSpace(note)
                ? $"Nạp +{amount:N0}đ tiền ảo miễn phí vào Ví BookShare"
                : note.Trim()[..Math.Min(250, note.Trim().Length)];

            var tx = new Transaction
            {
                UserId = currentUserId,
                LoanId = null,
                TransactionType = "WalletTopUp",
                ActionType = "TopUp",
                Amount = amount,
                SwitchFee = 0m,
                ProrationCredit = 0m,
                BalanceBefore = balBefore,
                BalanceAfter = balAfter,
                IdempotencyKey = cleanIdemKey,
                PaymentGateway = "VirtualWallet",
                PaymentStatus = "Completed",
                Description = cleanDesc,
                CreatedAt = now
            };
            _db.Transactions.Add(tx);
            await _db.SaveChangesAsync();

            string canonical = BuildCanonicalPayload(tx.Id, currentUserId, tx.TransactionType, amount, tx.PaymentGateway, null);
            tx.HmacSignature = ComputeHmacSha512(canonical);
            await _db.SaveChangesAsync();

            await dbTx.CommitAsync();

            lock (timestamps)
            {
                timestamps.Add(now);
            }

            return (true,
                $"Nạp miễn phí +{amount:N0}đ thành công (Mã #TX-{tx.Id})! Số dư Ví: {balBefore:N0}đ -> {balAfter:N0}đ ảo.",
                balAfter,
                tx.Id,
                false);
        }
        catch
        {
            try { await dbTx.RollbackAsync(); } catch { /* giao dịch đã đóng hoặc đã rollback */ }
            throw;
        }
    }

    public async Task<(bool Success, string Message, int TransactionId, string RawPayload, string ExpectedHmacSignature)> CreateSandboxOrderAsync(
        string currentUserId,
        S17CreateSandboxOrderDto request)
    {
        var allowedTypes = new[] { "Membership", "MembershipSwitch", "Fine", "SimulatedDeposit" };
        string txType = allowedTypes.Contains(request.TransactionType) ? request.TransactionType : "Membership";

        var allowedGateways = new[] { "QrPay", "VirtualWallet", "VNPaySandbox", "MoMoSandbox" };
        string gateway = allowedGateways.Contains(request.PaymentGateway) ? request.PaymentGateway : "QrPay";

        string? cleanIdemKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim()[..Math.Min(100, request.IdempotencyKey.Trim().Length)];

        if (!string.IsNullOrWhiteSpace(cleanIdemKey))
        {
            var existingByIdem = await _db.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IdempotencyKey == cleanIdemKey);
            if (existingByIdem != null)
            {
                string existingRaw = BuildCanonicalPayload(existingByIdem.Id, existingByIdem.UserId, existingByIdem.TransactionType, existingByIdem.Amount, existingByIdem.PaymentGateway, existingByIdem.LoanId);
                return (true,
                    $"Đang sử dụng lại hóa đơn #TX-{existingByIdem.Id} đã khởi tạo trước đó (chống tạo trùng lặp).",
                    existingByIdem.Id,
                    existingRaw,
                    ComputeHmacSha512(existingRaw));
            }
        }

        if (request.LoanId.HasValue)
        {
            bool loanExists = await _db.Loans.AnyAsync(l => l.Id == request.LoanId.Value);
            if (!loanExists)
            {
                return (false, "Không tìm thấy hợp đồng mượn được liên kết.", 0, string.Empty, string.Empty);
            }
        }

        var now = DateTime.UtcNow;
        bool isMembershipTx = txType is "Membership" or "MembershipSwitch";

        string? targetTier = isMembershipTx
            ? (string.IsNullOrWhiteSpace(request.TargetTier) ? "Premium" : MembershipTierCatalog.NormalizeTier(request.TargetTier))
            : null;
        int? durationMonths = isMembershipTx
            ? MembershipTierCatalog.NormalizeDurationMonths(request.DurationMonths ?? 12)
            : null;

        decimal switchFee = request.SwitchFee;
        decimal prorationCredit = request.ProrationCredit;
        decimal amount = request.Amount;

        // Đồng bộ luật chống mua trùng gói & tự tính phí chuyển gói nếu gọi thẳng từ trang S17
        if (isMembershipTx)
        {
            var activePaid = await _db.Memberships
                .AsNoTracking()
                .Where(m => m.UserId == currentUserId && m.IsActive && m.Tier != "Free" && (!m.ExpiryDate.HasValue || m.ExpiryDate.Value > now))
                .OrderByDescending(m => m.StartDate)
                .FirstOrDefaultAsync();

            var quote = MembershipTierCatalog.CalculateSwitchQuote(activePaid, targetTier, durationMonths ?? 12, now);
            if (quote.IsSameActiveTier)
            {
                return (false,
                    $"Bạn đang sở hữu gói {quote.CurrentTier} còn {quote.RemainingDays} ngày hiệu lực, không thể tạo hóa đơn mua trùng. Hãy chọn chuyển sang gói khác hoặc hủy gói tại mục Gói Hội Viên.",
                    0, string.Empty, string.Empty);
            }

            if (quote.IsSwitch)
            {
                txType = "MembershipSwitch";
                switchFee = quote.SwitchFee;
                prorationCredit = quote.ProrationCredit;
                amount = quote.TotalPayable;
            }
            else if (amount <= 0)
            {
                amount = MembershipTierCatalog.GetPrice(targetTier, durationMonths ?? 12);
            }
        }
        else if (amount <= 0)
        {
            amount = txType == "SimulatedDeposit" ? 50000m : 25000m;
        }

        // Chống spam hóa đơn Pending: nếu đã có hóa đơn Pending giống hệt trong 10 phút gần đây -> tái sử dụng ngay
        var recentCutoff = now.AddMinutes(-10);
        var reusablePending = await _db.Transactions
            .Where(t => t.UserId == currentUserId
                && t.PaymentStatus == "Pending"
                && t.TransactionType == txType
                && t.TargetTier == targetTier
                && t.DurationMonths == durationMonths
                && t.LoanId == request.LoanId
                && t.Amount == amount
                && t.CreatedAt >= recentCutoff)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (reusablePending != null)
        {
            string reuseRaw = BuildCanonicalPayload(reusablePending.Id, reusablePending.UserId, reusablePending.TransactionType, reusablePending.Amount, reusablePending.PaymentGateway, reusablePending.LoanId);
            return (true,
                $"Hóa đơn #TX-{reusablePending.Id} ({reusablePending.Amount:N0}đ ảo) vẫn đang chờ thanh toán — hệ thống tái sử dụng ngay để tránh trùng lặp!",
                reusablePending.Id,
                reuseRaw,
                ComputeHmacSha512(reuseRaw));
        }

        // Chặn tạo quá 3 hóa đơn Pending đồng thời (tránh spam rác CSDL)
        int currentPendingCount = await _db.Transactions
            .CountAsync(t => t.UserId == currentUserId && t.PaymentStatus == "Pending");
        if (currentPendingCount >= MaxConcurrentPendingInvoices)
        {
            return (false,
                $"Bạn hiện có {currentPendingCount} hóa đơn đang chờ thanh toán. Vui lòng thanh toán hoặc hủy bớt hóa đơn cũ tại Ví Tiền Ảo trước khi tạo hóa đơn mới.",
                0, string.Empty, string.Empty);
        }

        string description = ResolveDescription(txType, targetTier, durationMonths, request.LoanId, request.Description);
        string actionType = !string.IsNullOrWhiteSpace(request.ActionType)
            ? request.ActionType.Trim()
            : txType switch
            {
                "MembershipSwitch" => "Switch",
                "Membership" => "Register",
                "Fine" => "Fine",
                _ => "Deposit"
            };

        var tx = new Transaction
        {
            UserId = currentUserId,
            LoanId = request.LoanId,
            TransactionType = txType,
            ActionType = actionType,
            Amount = amount,
            SwitchFee = switchFee,
            ProrationCredit = prorationCredit,
            IdempotencyKey = cleanIdemKey,
            PaymentGateway = gateway,
            PaymentStatus = "Pending",
            TargetTier = targetTier,
            DurationMonths = durationMonths,
            Description = description,
            HmacSignature = null,
            CreatedAt = now
        };

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();

        string rawPayload = BuildCanonicalPayload(tx.Id, currentUserId, txType, amount, gateway, tx.LoanId);
        string expectedHmac = ComputeHmacSha512(rawPayload);

        return (true,
            $"Đã khởi tạo hóa đơn #TX-{tx.Id} ({description} — {amount:N0}đ ảo). Quét mã QR thanh toán hoặc xác nhận ngay trên Ví Tiền Ảo!",
            tx.Id,
            rawPayload,
            expectedHmac);
    }

    public async Task<(bool Success, string Message, S17QrInvoicePreviewResultDto? Invoice)> PreviewQrInvoiceAsync(
        string currentUserId,
        string currentUserRole,
        string qrPayload)
    {
        int txId = ParseInvoiceId(qrPayload);
        if (txId <= 0)
        {
            return (false, "Mã QR hóa đơn không đúng định dạng BookShare (BOOKSHARE-PAY:{mã hóa đơn}).", null);
        }

        var tx = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Id == txId);

        if (tx == null)
        {
            return (false, $"Không tìm thấy hóa đơn #TX-{txId} trên hệ thống.", null);
        }

        bool isAdmin = string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        if (tx.UserId != currentUserId && !isAdmin)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xem hoặc thanh toán hóa đơn của thành viên khác.");
        }

        decimal balance = await GetWalletBalanceAsync(tx.UserId);
        var dto = new S17QrInvoicePreviewResultDto
        {
            TransactionId = tx.Id,
            OwnerFullName = tx.User.FullName,
            TransactionType = tx.TransactionType,
            ServiceLabel = GetServiceLabel(tx.TransactionType),
            Description = ResolveDescription(tx.TransactionType, tx.TargetTier, tx.DurationMonths, tx.LoanId, tx.Description),
            TargetTier = tx.TargetTier,
            DurationMonths = tx.DurationMonths,
            Amount = tx.Amount,
            SwitchFee = tx.SwitchFee,
            ProrationCredit = tx.ProrationCredit,
            ActionType = tx.ActionType,
            PaymentStatus = tx.PaymentStatus,
            WalletBalance = balance,
            CreatedAt = tx.CreatedAt
        };

        if (tx.PaymentStatus != "Pending")
        {
            return (false, $"Hóa đơn #TX-{tx.Id} đã ở trạng thái '{tx.PaymentStatus}', không cần thanh toán lại.", dto);
        }

        return (true, $"Đã đọc thông tin hóa đơn #TX-{tx.Id}. Kiểm tra kỹ số tiền và bấm Xác nhận thanh toán.", dto);
    }

    public async Task<(bool Success, string Message, decimal NewBalance)> ConfirmQrInvoicePaymentAsync(
        string currentUserId,
        string currentUserRole,
        int transactionId)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var tx = await _db.Transactions
                .FromSqlRaw("SELECT * FROM [dbo].[Transactions] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {0}", transactionId)
                .FirstOrDefaultAsync();

            if (tx == null)
            {
                await dbTx.RollbackAsync();
                return (false, $"Không tìm thấy hóa đơn #TX-{transactionId}.", await GetWalletBalanceAsync(currentUserId));
            }

            bool isAdmin = string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
            if (tx.UserId != currentUserId && !isAdmin)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền thanh toán hóa đơn của thành viên khác.");
            }

            if (tx.PaymentStatus != "Pending")
            {
                decimal curBal = await GetWalletBalanceAsync(tx.UserId);
                await dbTx.RollbackAsync();
                return (false, $"Hóa đơn #TX-{tx.Id} đã ở trạng thái '{tx.PaymentStatus}', không thể thanh toán lặp lại.", curBal);
            }

            var now = DateTime.UtcNow;
            var wallet = await EnsureWalletLockedAsync(tx.UserId, now);
            decimal balBefore = wallet.Balance;

            if (balBefore < tx.Amount)
            {
                decimal shortfall = tx.Amount - balBefore;
                await dbTx.RollbackAsync();
                return (false,
                    $"Số dư Ví Tiền Ảo không đủ ({balBefore:N0}đ / cần {tx.Amount:N0}đ, còn thiếu {shortfall:N0}đ). Hãy nạp thêm tiền ảo miễn phí rồi xác nhận lại!",
                    balBefore);
            }

            wallet.Balance -= tx.Amount;
            wallet.TotalSpent += tx.Amount;
            wallet.UpdatedAt = now;
            decimal balAfter = wallet.Balance;

            tx.PaymentStatus = "Completed";
            tx.PaymentGateway = "QrPay";
            tx.BalanceBefore = balBefore;
            tx.BalanceAfter = balAfter;
            string canonical = BuildCanonicalPayload(tx.Id, tx.UserId, tx.TransactionType, tx.Amount, tx.PaymentGateway, tx.LoanId);
            tx.HmacSignature = ComputeHmacSha512(canonical);

            string extraEffect = await ApplyCompletedTransactionEffectsAsync(tx, now);

            _db.Notifications.Add(new Notification
            {
                UserId = tx.UserId,
                Title = $"Thanh toán QR hóa đơn #TX-{tx.Id} thành công",
                Content = $"Đã trừ {tx.Amount:N0}đ từ Ví Tiền Ảo (Số dư: {balBefore:N0}đ -> {balAfter:N0}đ) cho hóa đơn #{tx.Id}. {extraEffect}",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            await dbTx.CommitAsync();

            return (true,
                $"Thanh toán QR thành công hóa đơn #TX-{tx.Id} ({tx.Amount:N0}đ ảo)! Số dư Ví: {balBefore:N0}đ -> {balAfter:N0}đ. {extraEffect}",
                balAfter);
        }
        catch
        {
            try { await dbTx.RollbackAsync(); } catch { /* giao dịch đã đóng hoặc đã rollback */ }
            throw;
        }
    }

    public async Task<(bool Found, string Status, decimal Amount, string? Description)> GetInvoiceStatusAsync(
        string currentUserId,
        string currentUserRole,
        int transactionId)
    {
        var tx = await _db.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId);

        if (tx == null)
        {
            return (false, "NotFound", 0m, null);
        }

        bool isPrivileged = currentUserRole is "Librarian" or "Admin";
        if (tx.UserId != currentUserId && !isPrivileged)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền tra cứu hóa đơn của thành viên khác.");
        }

        return (true, tx.PaymentStatus, tx.Amount, ResolveDescription(tx.TransactionType, tx.TargetTier, tx.DurationMonths, tx.LoanId, tx.Description));
    }

    public async Task<(bool Success, string Message)> VerifyCallbackAndCompleteTransactionAsync(
        string currentUserId,
        string currentUserRole,
        S17SandboxCallbackDto callback)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var tx = await _db.Transactions
                .FromSqlRaw("SELECT * FROM [dbo].[Transactions] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {0}", callback.TransactionId)
                .FirstOrDefaultAsync();

            if (tx == null)
            {
                await dbTx.RollbackAsync();
                return (false, $"Không tìm thấy giao dịch #{callback.TransactionId}.");
            }

            bool isPrivileged = currentUserRole is "Librarian" or "Admin";
            if (tx.UserId != currentUserId && !isPrivileged)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền xác thực giao dịch của thành viên khác.");
            }

            if (tx.PaymentStatus != "Pending")
            {
                await dbTx.RollbackAsync();
                return (false, $"Giao dịch #{tx.Id} đã ở trạng thái '{tx.PaymentStatus}', không thể xử lý lặp lại.");
            }

            string canonicalPayload = BuildCanonicalPayload(tx.Id, tx.UserId, tx.TransactionType, tx.Amount, tx.PaymentGateway, tx.LoanId);
            if (!string.IsNullOrWhiteSpace(callback.RawPayload) &&
                !string.Equals(callback.RawPayload.Trim(), canonicalPayload, StringComparison.Ordinal))
            {
                await dbTx.RollbackAsync();
                return (false, "Dữ liệu xác thực không khớp với bản ghi giao dịch gốc trên máy chủ.");
            }

            string expectedHmac = ComputeHmacSha512(canonicalPayload);
            string providedHmac = (callback.HmacSignature ?? string.Empty).Trim().ToLowerInvariant();

            byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHmac);
            byte[] providedBytes = Encoding.UTF8.GetBytes(providedHmac);

            if (expectedBytes.Length != providedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
            {
                await dbTx.RollbackAsync();
                return (false,
                    $"Từ chối xác thực: Mã xác nhận giao dịch không hợp lệ hoặc dữ liệu đã bị thay đổi! Hóa đơn #{tx.Id} vẫn giữ nguyên chờ xử lý.");
            }

            if (callback.ResponseCode != "00")
            {
                tx.PaymentStatus = "Failed";
                tx.HmacSignature = expectedHmac;
                await _db.SaveChangesAsync();
                await dbTx.CommitAsync();
                return (false, $"Giao dịch #{tx.Id} đã được hủy (Mã phản hồi = {callback.ResponseCode}).");
            }

            tx.PaymentStatus = "Completed";
            tx.HmacSignature = expectedHmac;

            var now = DateTime.UtcNow;
            string extraEffect = await ApplyCompletedTransactionEffectsAsync(tx, now);

            _db.Notifications.Add(new Notification
            {
                UserId = tx.UserId,
                Title = $"Xác thực giao dịch #{tx.Id} thành công",
                Content = $"Giao dịch {GetServiceLabel(tx.TransactionType)} trị giá {tx.Amount:N0}đ ảo đã hoàn tất. {extraEffect}",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            await dbTx.CommitAsync();

            return (true,
                $"Xác thực giao dịch #{tx.Id} ({tx.Amount:N0}đ ảo) thành công! {extraEffect}");
        }
        catch
        {
            try { await dbTx.RollbackAsync(); } catch { /* giao dịch đã đóng hoặc đã rollback */ }
            throw;
        }
    }

    public async Task<(bool Success, string Message)> RefundSimulatedDepositAsync(
        string currentUserId,
        string currentUserRole,
        int transactionId)
    {
        if (currentUserRole != "Librarian" && currentUserRole != "Admin")
        {
            throw new UnauthorizedAccessException("Chỉ Thủ thư hoặc Quản trị viên mới được xác nhận hoàn cọc bảo lãnh.");
        }

        await using var dbTx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var tx = await _db.Transactions
                .FromSqlRaw("SELECT * FROM [dbo].[Transactions] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {0}", transactionId)
                .FirstOrDefaultAsync();
            if (tx == null)
            {
                await dbTx.RollbackAsync();
                return (false, "Không tìm thấy giao dịch tiền cọc cần hoàn trả.");
            }

            if (tx.TransactionType != "SimulatedDeposit")
            {
                await dbTx.RollbackAsync();
                return (false, "Chỉ các giao dịch Tiền cọc bảo lãnh mới hỗ trợ hoàn cọc.");
            }

            if (tx.PaymentStatus != "Completed")
            {
                await dbTx.RollbackAsync();
                return (false, $"Chỉ có thể hoàn cọc cho giao dịch đã hoàn tất thanh toán (Trạng thái hiện tại: {tx.PaymentStatus}).");
            }

            var now = DateTime.UtcNow;
            tx.PaymentStatus = "Refunded";

            var wallet = await EnsureWalletLockedAsync(tx.UserId, now);
            decimal balBefore = wallet.Balance;
            wallet.Balance += tx.Amount;
            wallet.UpdatedAt = now;
            decimal balAfter = wallet.Balance;
            tx.BalanceBefore ??= balBefore;
            tx.BalanceAfter = balAfter;

            _db.Notifications.Add(new Notification
            {
                UserId = tx.UserId,
                Title = $"Đã hoàn trả tiền cọc #{tx.Id} về Ví Tiền Ảo",
                Content = $"Khoản tiền cọc bảo lãnh {tx.Amount:N0}đ ảo đã được cộng lại 100% vào Ví Tiền Ảo (Số dư: {balBefore:N0}đ -> {balAfter:N0}đ).",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            await dbTx.CommitAsync();

            return (true, $"Đã hoàn trả 100% tiền cọc bảo lãnh ({tx.Amount:N0}đ ảo) của giao dịch #{tx.Id} về Ví Tiền Ảo!");
        }
        catch
        {
            try { await dbTx.RollbackAsync(); } catch { /* giao dịch đã đóng hoặc đã rollback */ }
            throw;
        }
    }

    public async Task<(bool Success, string Message)> CancelPendingTransactionAsync(
        string currentUserId,
        string currentUserRole,
        int transactionId)
    {
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId);
        if (tx == null)
        {
            return (false, "Không tìm thấy giao dịch cần hủy.");
        }

        if (tx.UserId != currentUserId && currentUserRole != "Admin")
        {
            throw new UnauthorizedAccessException("Bạn không có quyền hủy hoặc xóa giao dịch của thành viên khác.");
        }

        if (tx.PaymentStatus == "Completed")
        {
            return (false, "Giao dịch đã hoàn tất thanh toán, không thể xóa.");
        }

        _db.Transactions.Remove(tx);
        await _db.SaveChangesAsync();

        return (true, $"Đã hủy và xóa hóa đơn #{transactionId} khỏi hệ thống!");
    }

    public string BuildCanonicalPayload(
        int txId,
        string userId,
        string txType,
        decimal amount,
        string gateway,
        int? loanId)
    {
        return $"bs_TxnRef={txId}&bs_UserId={userId}&bs_Type={txType}&bs_Amount={amount:0}&bs_Gateway={gateway}&bs_LoanId={loanId ?? 0}";
    }

    public string ComputeHmacSha512(string rawData)
    {
        string secret = _config["BookShare:InternalInvoiceSecret"] is { Length: > 0 } s
            ? s
            : "BOOKSHARE_VIRTUAL_WALLET_INTERNAL_HMAC512_2026";

        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] dataBytes = Encoding.UTF8.GetBytes(rawData);
        byte[] hashBytes = HMACSHA512.HashData(keyBytes, dataBytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task<UserWallet> EnsureWalletLockedAsync(string userId, DateTime now)
    {
        var wallet = await _db.UserWallets
            .FromSqlRaw("SELECT * FROM [dbo].[UserWallets] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {0}", userId)
            .FirstOrDefaultAsync();

        if (wallet == null)
        {
            wallet = new UserWallet
            {
                UserId = userId,
                Balance = 0m,
                TotalToppedUp = 0m,
                TotalSpent = 0m,
                UpdatedAt = now
            };
            _db.UserWallets.Add(wallet);
            await _db.SaveChangesAsync();
        }
        return wallet;
    }

    private async Task<string> ApplyCompletedTransactionEffectsAsync(Transaction tx, DateTime now)
    {
        if (tx.TransactionType is "Membership" or "MembershipSwitch")
        {
            string targetTier = !string.IsNullOrWhiteSpace(tx.TargetTier)
                ? MembershipTierCatalog.NormalizeTier(tx.TargetTier)
                : "Premium";
            if (targetTier == "Free") targetTier = "Premium";

            var policy = MembershipTierCatalog.GetPolicy(targetTier);
            int durationMonths = tx.DurationMonths is 1 or 6 or 12
                ? tx.DurationMonths.Value
                : (tx.Amount <= 50000m ? 1 : tx.Amount <= 250000m ? 6 : 12);

            var activeRecords = await _db.Memberships
                .Where(m => m.UserId == tx.UserId && m.IsActive)
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
                UserId = tx.UserId,
                Tier = policy.Tier,
                MaxConcurrentLoans = policy.MaxConcurrentLoans,
                StartDate = now,
                ExpiryDate = now.AddMonths(durationMonths),
                IsActive = true
            });

            // Hủy các hóa đơn đăng ký/chuyển gói Pending khác của cùng user để tránh quét trùng
            var stalePending = await _db.Transactions
                .Where(t => t.UserId == tx.UserId
                    && t.Id != tx.Id
                    && t.PaymentStatus == "Pending"
                    && (t.TransactionType == "Membership" || t.TransactionType == "MembershipSwitch"))
                .ToListAsync();
            foreach (var p in stalePending)
            {
                p.PaymentStatus = "Failed";
            }

            return tx.TransactionType == "MembershipSwitch"
                ? $"Đã chuyển đổi thành công sang gói {policy.DisplayName} ({policy.Tier} +{durationMonths} tháng — tối đa {policy.MaxConcurrentLoans} cuốn, {policy.MaxBorrowDays} ngày/lượt; lệ phí chuyển {tx.SwitchFee:N0}đ)."
                : $"Đã kích hoạt gói {policy.DisplayName} ({policy.Tier} +{durationMonths} tháng — tối đa {policy.MaxConcurrentLoans} cuốn, {policy.MaxBorrowDays} ngày/lượt).";
        }

        if (tx.TransactionType == "Fine" && tx.LoanId.HasValue)
        {
            var loan = await _db.Loans.FirstOrDefaultAsync(l => l.Id == tx.LoanId.Value);
            if (loan != null)
            {
                loan.FineAmount = Math.Max(0m, loan.FineAmount - tx.Amount);
                if (loan.FineAmount <= 0m && loan.Status is "Overdue" or "Disputed")
                {
                    bool hasPendingOpenDispute = await _db.Disputes.AnyAsync(d =>
                        d.LoanId == loan.Id && (d.Status == "Open" || d.Status == "UnderReview"));
                    if (!hasPendingOpenDispute)
                    {
                        bool hasResolvedDispute = await _db.Disputes.AnyAsync(d =>
                            d.LoanId == loan.Id && d.Status == "Resolved");
                        if (loan.ReturnDate.HasValue || hasResolvedDispute)
                        {
                            loan.ReturnDate ??= now;
                            loan.Status = "Returned";
                        }
                        else
                        {
                            loan.Status = "Active";
                            if (loan.DueDate <= now)
                            {
                                loan.DueDate = now.AddDays(3);
                            }
                        }
                    }
                }
                return $"Đã hoàn tất thanh toán khoản phí/bồi thường cho lượt mượn #{loan.Id}.";
            }
        }

        if (tx.TransactionType == "SimulatedDeposit")
        {
            return "Đã ghi nhận khoản ký quỹ bảo lãnh sách quý (hoàn trả 100% vào Ví Tiền Ảo khi trả sách nguyên vẹn).";
        }

        return string.Empty;
    }

    private static int ParseInvoiceId(string? qrPayload)
    {
        if (string.IsNullOrWhiteSpace(qrPayload)) return 0;
        string s = qrPayload.Trim();
        var m = Regex.Match(s, @"(?:BOOKSHARE[-:]PAY:|#?TX-)?(\d+)", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out int id) ? id : 0;
    }

    private static string GetServiceLabel(string txType) => txType switch
    {
        "Membership" => "Đăng ký Gói Hội Viên mới",
        "MembershipSwitch" => "Chuyển đổi Gói Hội Viên (kèm lệ phí chuyển)",
        "MembershipCancel" => "Hủy Gói Hội Viên về Free",
        "Fine" => "Thanh toán Phí trễ hạn / Bồi thường",
        "SimulatedDeposit" => "Ký quỹ Bảo lãnh Sách quý",
        "WalletTopUp" => "Nạp Tiền Ảo Miễn Phí",
        _ => "Giao dịch Nội bộ BookShare"
    };

    private static string ResolveDescription(string txType, string? targetTier, int? durationMonths, int? loanId, string? explicitDesc)
    {
        if (!string.IsNullOrWhiteSpace(explicitDesc)) return explicitDesc;
        if (txType is "Membership" or "MembershipSwitch")
        {
            var policy = MembershipTierCatalog.GetPolicy(string.IsNullOrWhiteSpace(targetTier) ? "Premium" : targetTier);
            int months = durationMonths ?? 12;
            return txType == "MembershipSwitch"
                ? $"Chuyển đổi sang gói {policy.DisplayName} ({policy.Tier} — {months} tháng)"
                : $"Đăng ký gói {policy.DisplayName} ({policy.Tier} — {months} tháng)";
        }
        if (txType == "MembershipCancel")
        {
            return "Hủy gói hội viên trả phí về Free";
        }
        if (txType == "Fine")
        {
            return loanId.HasValue ? $"Thanh toán phí trễ hạn / bồi thường cho hợp đồng #{loanId.Value}" : "Thanh toán phí trễ hạn mượn sách";
        }
        if (txType == "SimulatedDeposit")
        {
            return loanId.HasValue ? $"Ký quỹ bảo lãnh sách cho hợp đồng #{loanId.Value}" : "Ký quỹ bảo lãnh sách quý (Hoàn 100%)";
        }
        return "Nạp tiền ảo miễn phí vào Ví BookShare";
    }
}
