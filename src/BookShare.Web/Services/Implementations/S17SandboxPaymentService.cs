using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S17SandboxPaymentService : IS17SandboxPaymentService
{
    private const decimal MinTopUpAmount = 1000m;
    private const decimal MaxTopUpAmount = 10_000_000m;

    private readonly BookShareDbContext _db;
    private readonly IConfiguration _config;

    public S17SandboxPaymentService(BookShareDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
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

    public async Task<(bool Success, string Message, decimal NewBalance, int TransactionId)> TopUpVirtualWalletAsync(
        string currentUserId,
        decimal amount,
        string? note = null)
    {
        if (amount < MinTopUpAmount || amount > MaxTopUpAmount)
        {
            return (false, $"Số tiền ảo mỗi lần nạp phải từ {MinTopUpAmount:N0}đ đến {MaxTopUpAmount:N0}đ ảo.", await GetWalletBalanceAsync(currentUserId), 0);
        }

        var user = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản thành viên.", 0m, 0);
        }

        var now = DateTime.UtcNow;
        var wallet = await EnsureWalletEntityAsync(currentUserId, now);
        wallet.Balance += amount;
        wallet.TotalToppedUp += amount;
        wallet.UpdatedAt = now;

        string cleanDesc = string.IsNullOrWhiteSpace(note)
            ? $"Nạp {amount:N0}đ tiền ảo miễn phí vào Ví BookShare"
            : note.Trim()[..Math.Min(250, note.Trim().Length)];

        var tx = new Transaction
        {
            UserId = currentUserId,
            LoanId = null,
            TransactionType = "WalletTopUp",
            Amount = amount,
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

        return (true,
            $"Đã nạp miễn phí +{amount:N0}đ tiền ảo vào ví! Số dư hiện tại: {wallet.Balance:N0}đ ảo.",
            wallet.Balance,
            tx.Id);
    }

    public async Task<(bool Success, string Message, int TransactionId, string RawPayload, string ExpectedHmacSignature)> CreateSandboxOrderAsync(
        string currentUserId,
        S17CreateSandboxOrderDto request)
    {
        var allowedTypes = new[] { "Membership", "Fine", "SimulatedDeposit" };
        string txType = allowedTypes.Contains(request.TransactionType) ? request.TransactionType : "Membership";

        var allowedGateways = new[] { "QrPay", "VirtualWallet", "VNPaySandbox", "MoMoSandbox" };
        string gateway = allowedGateways.Contains(request.PaymentGateway) ? request.PaymentGateway : "QrPay";

        if (request.LoanId.HasValue)
        {
            bool loanExists = await _db.Loans.AnyAsync(l => l.Id == request.LoanId.Value);
            if (!loanExists)
            {
                return (false, "Không tìm thấy hợp đồng mượn được liên kết.", 0, string.Empty, string.Empty);
            }
        }

        string? targetTier = txType == "Membership"
            ? (string.IsNullOrWhiteSpace(request.TargetTier) ? "Premium" : MembershipTierCatalog.NormalizeTier(request.TargetTier))
            : null;
        int? durationMonths = txType == "Membership"
            ? MembershipTierCatalog.NormalizeDurationMonths(request.DurationMonths ?? 12)
            : null;

        decimal amount = request.Amount > 0
            ? request.Amount
            : (txType == "Membership"
                ? MembershipTierCatalog.GetPrice(targetTier, durationMonths ?? 12)
                : txType == "SimulatedDeposit" ? 50000m : 25000m);

        string description = ResolveDescription(txType, targetTier, durationMonths, request.LoanId, request.Description);
        var now = DateTime.UtcNow;

        var tx = new Transaction
        {
            UserId = currentUserId,
            LoanId = request.LoanId,
            TransactionType = txType,
            Amount = amount,
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
        var tx = await _db.Transactions
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Id == transactionId);

        if (tx == null)
        {
            return (false, $"Không tìm thấy hóa đơn #TX-{transactionId}.", await GetWalletBalanceAsync(currentUserId));
        }

        bool isAdmin = string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        if (tx.UserId != currentUserId && !isAdmin)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền thanh toán hóa đơn của thành viên khác.");
        }

        if (tx.PaymentStatus != "Pending")
        {
            return (false, $"Hóa đơn #TX-{tx.Id} đã ở trạng thái '{tx.PaymentStatus}', không thể thanh toán lặp lại.", await GetWalletBalanceAsync(tx.UserId));
        }

        var now = DateTime.UtcNow;
        var wallet = await EnsureWalletEntityAsync(tx.UserId, now);
        if (wallet.Balance < tx.Amount)
        {
            decimal shortfall = tx.Amount - wallet.Balance;
            return (false,
                $"Số dư Ví Tiền Ảo không đủ ({wallet.Balance:N0}đ / cần {tx.Amount:N0}đ, còn thiếu {shortfall:N0}đ). Hãy nạp thêm tiền ảo miễn phí rồi xác nhận lại!",
                wallet.Balance);
        }

        wallet.Balance -= tx.Amount;
        wallet.TotalSpent += tx.Amount;
        wallet.UpdatedAt = now;

        tx.PaymentStatus = "Completed";
        tx.PaymentGateway = "QrPay";
        string canonical = BuildCanonicalPayload(tx.Id, tx.UserId, tx.TransactionType, tx.Amount, tx.PaymentGateway, tx.LoanId);
        tx.HmacSignature = ComputeHmacSha512(canonical);

        string extraEffect = await ApplyCompletedTransactionEffectsAsync(tx, now);

        _db.Notifications.Add(new Notification
        {
            UserId = tx.UserId,
            Title = $"Thanh toán QR hóa đơn #TX-{tx.Id} thành công",
            Content = $"Đã trừ {tx.Amount:N0}đ từ Ví Tiền Ảo cho hóa đơn #{tx.Id}. Số dư còn lại: {wallet.Balance:N0}đ. {extraEffect}",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        return (true,
            $"Thanh toán QR thành công hóa đơn #TX-{tx.Id} ({tx.Amount:N0}đ ảo)! Số dư còn lại: {wallet.Balance:N0}đ. {extraEffect}",
            wallet.Balance);
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
        var tx = await _db.Transactions
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Id == callback.TransactionId);

        if (tx == null)
        {
            return (false, $"Không tìm thấy giao dịch #{callback.TransactionId}.");
        }

        bool isPrivileged = currentUserRole is "Librarian" or "Admin";
        if (tx.UserId != currentUserId && !isPrivileged)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xác thực giao dịch của thành viên khác.");
        }

        if (tx.PaymentStatus != "Pending")
        {
            return (false, $"Giao dịch #{tx.Id} đã ở trạng thái '{tx.PaymentStatus}', không thể xử lý lặp lại.");
        }

        string canonicalPayload = BuildCanonicalPayload(tx.Id, tx.UserId, tx.TransactionType, tx.Amount, tx.PaymentGateway, tx.LoanId);
        if (!string.IsNullOrWhiteSpace(callback.RawPayload) &&
            !string.Equals(callback.RawPayload.Trim(), canonicalPayload, StringComparison.Ordinal))
        {
            return (false, "Dữ liệu xác thực không khớp với bản ghi giao dịch gốc trên máy chủ.");
        }

        string expectedHmac = ComputeHmacSha512(canonicalPayload);
        string providedHmac = (callback.HmacSignature ?? string.Empty).Trim().ToLowerInvariant();

        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHmac);
        byte[] providedBytes = Encoding.UTF8.GetBytes(providedHmac);

        if (expectedBytes.Length != providedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
        {
            return (false,
                $"Từ chối xác thực: Mã xác nhận giao dịch không hợp lệ hoặc dữ liệu đã bị thay đổi! Hóa đơn #{tx.Id} vẫn giữ nguyên chờ xử lý.");
        }

        if (callback.ResponseCode != "00")
        {
            tx.PaymentStatus = "Failed";
            tx.HmacSignature = expectedHmac;
            await _db.SaveChangesAsync();
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

        return (true,
            $"Xác thực giao dịch #{tx.Id} ({tx.Amount:N0}đ ảo) thành công! {extraEffect}");
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

        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId);
        if (tx == null)
        {
            return (false, "Không tìm thấy giao dịch tiền cọc cần hoàn trả.");
        }

        if (tx.TransactionType != "SimulatedDeposit")
        {
            return (false, "Chỉ các giao dịch Tiền cọc bảo lãnh mới hỗ trợ hoàn cọc.");
        }

        if (tx.PaymentStatus != "Completed")
        {
            return (false, $"Chỉ có thể hoàn cọc cho giao dịch đã hoàn tất thanh toán (Trạng thái hiện tại: {tx.PaymentStatus}).");
        }

        var now = DateTime.UtcNow;
        tx.PaymentStatus = "Refunded";

        // Hoàn thẳng khoản ký quỹ ảo về Ví Tiền Ảo của thành viên
        var wallet = await EnsureWalletEntityAsync(tx.UserId, now);
        wallet.Balance += tx.Amount;
        wallet.UpdatedAt = now;

        _db.Notifications.Add(new Notification
        {
            UserId = tx.UserId,
            Title = $"Đã hoàn trả tiền cọc #{tx.Id} về Ví Tiền Ảo",
            Content = $"Khoản tiền cọc bảo lãnh {tx.Amount:N0}đ ảo đã được cộng lại 100% vào Ví Tiền Ảo của bạn (Số dư mới: {wallet.Balance:N0}đ).",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        return (true, $"Đã hoàn trả 100% tiền cọc bảo lãnh ({tx.Amount:N0}đ ảo) của giao dịch #{tx.Id} về Ví Tiền Ảo!");
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

    private async Task<UserWallet> EnsureWalletEntityAsync(string userId, DateTime now)
    {
        var wallet = await _db.UserWallets.FirstOrDefaultAsync(w => w.UserId == userId);
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
        }
        return wallet;
    }

    private async Task<string> ApplyCompletedTransactionEffectsAsync(Transaction tx, DateTime now)
    {
        if (tx.TransactionType == "Membership")
        {
            string targetTier = !string.IsNullOrWhiteSpace(tx.TargetTier)
                ? MembershipTierCatalog.NormalizeTier(tx.TargetTier)
                : "Premium";
            if (targetTier == "Free") targetTier = "Premium";

            var policy = MembershipTierCatalog.GetPolicy(targetTier);
            int durationMonths = tx.DurationMonths is 1 or 6 or 12
                ? tx.DurationMonths.Value
                : (tx.Amount <= 50000m ? 1 : tx.Amount <= 250000m ? 6 : 12);

            var membership = await _db.Memberships
                .Where(m => m.UserId == tx.UserId && m.IsActive)
                .OrderByDescending(m => m.StartDate)
                .FirstOrDefaultAsync();

            if (membership != null)
            {
                bool sameTierValid = membership.Tier.Equals(policy.Tier, StringComparison.OrdinalIgnoreCase)
                    && membership.ExpiryDate.HasValue
                    && membership.ExpiryDate.Value > now;
                membership.Tier = policy.Tier;
                membership.MaxConcurrentLoans = policy.MaxConcurrentLoans;
                var baseDate = sameTierValid ? membership.ExpiryDate!.Value : now;
                membership.ExpiryDate = baseDate.AddMonths(durationMonths);
            }
            else
            {
                _db.Memberships.Add(new Membership
                {
                    UserId = tx.UserId,
                    Tier = policy.Tier,
                    MaxConcurrentLoans = policy.MaxConcurrentLoans,
                    StartDate = now,
                    ExpiryDate = now.AddMonths(durationMonths),
                    IsActive = true
                });
            }

            return $"Đã kích hoạt gói {policy.DisplayName} ({policy.Tier} +{durationMonths} tháng — tối đa {policy.MaxConcurrentLoans} cuốn, {policy.MaxBorrowDays} ngày/lượt).";
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
        "Membership" => "Đăng ký / Nâng cấp Gói Hội Viên",
        "Fine" => "Thanh toán Phí trễ hạn / Bồi thường",
        "SimulatedDeposit" => "Ký quỹ Bảo lãnh Sách quý",
        "WalletTopUp" => "Nạp Tiền Ảo Miễn Phí",
        _ => "Giao dịch Nội bộ BookShare"
    };

    private static string ResolveDescription(string txType, string? targetTier, int? durationMonths, int? loanId, string? explicitDesc)
    {
        if (!string.IsNullOrWhiteSpace(explicitDesc)) return explicitDesc;
        if (txType == "Membership")
        {
            var policy = MembershipTierCatalog.GetPolicy(string.IsNullOrWhiteSpace(targetTier) ? "Premium" : targetTier);
            int months = durationMonths ?? 12;
            return $"Đăng ký / Gia hạn gói {policy.DisplayName} ({policy.Tier} — {months} tháng)";
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
