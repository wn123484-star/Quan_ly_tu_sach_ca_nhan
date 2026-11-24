using System.Security.Cryptography;
using System.Text;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S17SandboxPaymentService : IS17SandboxPaymentService
{
    private readonly BookShareDbContext _db;
    private readonly IConfiguration _config;

    public S17SandboxPaymentService(BookShareDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
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

        var membership = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.IsActive)
            .OrderByDescending(m => m.StartDate)
            .FirstOrDefaultAsync();

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
                HmacSignature = t.HmacSignature,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        int? pendingTxId = null;
        string? pendingRaw = null;
        string? pendingHmac = null;
        if (selectedTxId.HasValue)
        {
            var selectedTx = txs.FirstOrDefault(t => t.TransactionId == selectedTxId.Value && t.PaymentStatus == "Pending");
            if (selectedTx != null)
            {
                pendingTxId = selectedTx.TransactionId;
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
            CurrentTier = membership?.Tier ?? "Free",
            TotalCompletedAmount = txs.Where(t => t.PaymentStatus == "Completed").Sum(t => t.Amount),
            TotalPendingAmount = txs.Where(t => t.PaymentStatus == "Pending").Sum(t => t.Amount),
            TotalVerifiedHmacCount = txs.Count(t => !string.IsNullOrWhiteSpace(t.HmacSignature)),
            SelectedPendingTxId = pendingTxId,
            SelectedRawPayload = pendingRaw,
            SelectedExpectedHmac = pendingHmac,
            UnpaidFineLoans = unpaidLoans,
            Transactions = txs
        };
    }

    public async Task<(bool Success, string Message, int TransactionId, string RawPayload, string ExpectedHmacSignature)> CreateSandboxOrderAsync(
        string currentUserId,
        S17CreateSandboxOrderDto request)
    {
        var allowedTypes = new[] { "Membership", "Fine", "SimulatedDeposit" };
        string txType = allowedTypes.Contains(request.TransactionType) ? request.TransactionType : "Membership";

        var allowedGateways = new[] { "VNPaySandbox", "MoMoSandbox" };
        string gateway = allowedGateways.Contains(request.PaymentGateway) ? request.PaymentGateway : "VNPaySandbox";

        if (request.LoanId.HasValue)
        {
            bool loanExists = await _db.Loans.AnyAsync(l => l.Id == request.LoanId.Value);
            if (!loanExists)
            {
                return (false, "Không tìm thấy hợp đồng mượn được liên kết.", 0, string.Empty, string.Empty);
            }
        }

        decimal amount = request.Amount > 0 ? request.Amount : (txType == "Membership" ? 99000m : txType == "SimulatedDeposit" ? 50000m : 25000m);
        var now = DateTime.UtcNow;

        var tx = new Transaction
        {
            UserId = currentUserId,
            LoanId = request.LoanId,
            TransactionType = txType,
            Amount = amount,
            PaymentGateway = gateway,
            PaymentStatus = "Pending",
            HmacSignature = null,
            CreatedAt = now
        };

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();

        string rawPayload = BuildCanonicalPayload(tx.Id, currentUserId, txType, amount, gateway, tx.LoanId);
        string expectedHmac = ComputeHmacSha512(rawPayload);

        return (true,
            $"Đã khởi tạo lệnh giao dịch #{tx.Id} ({txType} - {amount:N0} VNĐ) và sinh chữ ký xác thực điện tử. Vui lòng hoàn tất xác thực để cập nhật trạng thái!",
            tx.Id,
            rawPayload,
            expectedHmac);
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

        // Luôn tự tính lại chuỗi dữ liệu chuẩn từ bản ghi giao dịch trong CSDL (không tin chuỗi từ trình duyệt)
        string canonicalPayload = BuildCanonicalPayload(tx.Id, tx.UserId, tx.TransactionType, tx.Amount, tx.PaymentGateway, tx.LoanId);
        if (!string.IsNullOrWhiteSpace(callback.RawPayload) &&
            !string.Equals(callback.RawPayload.Trim(), canonicalPayload, StringComparison.Ordinal))
        {
            return (false, "Dữ liệu xác thực không khớp với bản ghi giao dịch gốc trên máy chủ.");
        }

        string expectedHmac = ComputeHmacSha512(canonicalPayload);
        string providedHmac = (callback.HmacSignature ?? string.Empty).Trim().ToLowerInvariant();

        // Kiểm tra chữ ký điện tử HMAC-SHA512 chống timing attack
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHmac);
        byte[] providedBytes = Encoding.UTF8.GetBytes(providedHmac);

        if (expectedBytes.Length != providedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
        {
            return (false,
                $"Từ chối xác thực: Chữ ký điện tử HMAC-SHA512 không hợp lệ hoặc dữ liệu đã bị thay đổi! Giao dịch #{tx.Id} vẫn giữ nguyên chờ xác thực hợp lệ.");
        }

        if (callback.ResponseCode != "00")
        {
            tx.PaymentStatus = "Failed";
            tx.HmacSignature = expectedHmac;
            await _db.SaveChangesAsync();
            return (false, $"Giao dịch #{tx.Id} đã bị hủy tại cổng thanh toán (Mã phản hồi = {callback.ResponseCode}).");
        }

        tx.PaymentStatus = "Completed";
        tx.HmacSignature = expectedHmac;

        string extraEffect = string.Empty;
        var now = DateTime.UtcNow;

        if (tx.TransactionType == "Membership")
        {
            int durationMonths = tx.Amount switch
            {
                <= 20000m => 1,
                <= 65000m => 6,
                _ => 12
            };

            var membership = await _db.Memberships
                .Where(m => m.UserId == tx.UserId && m.IsActive)
                .OrderByDescending(m => m.StartDate)
                .FirstOrDefaultAsync();

            if (membership != null)
            {
                membership.Tier = "Premium";
                membership.MaxConcurrentLoans = 5;
                var baseDate = membership.ExpiryDate.HasValue && membership.ExpiryDate.Value > now
                    ? membership.ExpiryDate.Value
                    : now;
                membership.ExpiryDate = baseDate.AddMonths(durationMonths);
            }
            else
            {
                _db.Memberships.Add(new Membership
                {
                    UserId = tx.UserId,
                    Tier = "Premium",
                    MaxConcurrentLoans = 5,
                    StartDate = now,
                    ExpiryDate = now.AddMonths(durationMonths),
                    IsActive = true
                });
            }

            extraEffect = $"Đã kích hoạt hạng thẻ thành viên Premium (+{durationMonths} tháng, hạn mức 5 cuốn + ưu tiên hàng chờ).";
        }
        else if (tx.TransactionType == "Fine" && tx.LoanId.HasValue)
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
                extraEffect = $"Đã hoàn tất thanh toán khoản phí/bồi thường cho lượt mượn #{loan.Id}.";
            }
        }
        else if (tx.TransactionType == "SimulatedDeposit")
        {
            extraEffect = "Đã ghi nhận khoản tiền cọc bảo lãnh sách quý (sẽ hoàn trả 100% khi trả sách nguyên vẹn).";
        }

        _db.Notifications.Add(new Notification
        {
            UserId = tx.UserId,
            Title = $"Xác thực giao dịch #{tx.Id} thành công",
            Content = $"Giao dịch {tx.TransactionType} trị giá {tx.Amount:N0} VNĐ đã được đối soát hợp lệ. {extraEffect}",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        return (true,
            $"Xác thực chữ ký điện tử HMAC-SHA512 hợp lệ! Giao dịch #{tx.Id} ({tx.Amount:N0} VNĐ) đã hoàn tất. {extraEffect}");
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

        tx.PaymentStatus = "Refunded";
        _db.Notifications.Add(new Notification
        {
            UserId = tx.UserId,
            Title = $"Đã hoàn trả tiền cọc #{tx.Id}",
            Content = $"Khoản tiền cọc bảo lãnh {tx.Amount:N0} VNĐ đã được hoàn trả 100%.",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return (true, $"Đã hoàn trả 100% tiền cọc bảo lãnh ({tx.Amount:N0} VNĐ) của giao dịch #{tx.Id}!");
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
            return (false, "Giao dịch đã hoàn tất đối soát chữ ký điện tử, không thể xóa.");
        }

        _db.Transactions.Remove(tx);
        await _db.SaveChangesAsync();

        return (true, $"Đã hủy và xóa lệnh giao dịch #{transactionId} khỏi hệ thống!");
    }

    public string BuildCanonicalPayload(
        int txId,
        string userId,
        string txType,
        decimal amount,
        string gateway,
        int? loanId)
    {
        return $"vnp_TxnRef={txId}&vnp_UserId={userId}&vnp_Type={txType}&vnp_Amount={amount:0}&vnp_Gateway={gateway}&vnp_LoanId={loanId ?? 0}";
    }

    public string ComputeHmacSha512(string rawData)
    {
        string secret = _config["VNPay:HashSecret"] is { Length: > 0 } s
            ? s
            : (Environment.GetEnvironmentVariable("VNPAY_HASH_SECRET") ?? "BOOKSHARE_VNPAY_SANDBOX_SECRET_KEY_2026_HMAC512");

        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] dataBytes = Encoding.UTF8.GetBytes(rawData);
        byte[] hashBytes = HMACSHA512.HashData(keyBytes, dataBytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
