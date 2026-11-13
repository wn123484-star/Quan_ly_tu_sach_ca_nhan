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

    public async Task<S17SandboxPaymentPageViewModel> GetSandboxPaymentPageAsync(string currentUserId)
    {
        var user = await _db.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId)
            ?? await _db.AspNetUsers.AsNoTracking().FirstAsync(u => u.Id == "u-user-01");

        var membership = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.IsActive)
            .OrderByDescending(m => m.StartDate)
            .FirstOrDefaultAsync();

        var unpaidLoans = await _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.Borrower)
            .OrderByDescending(l => l.FineAmount)
            .Select(l => new S17UnpaidFineLoanDto
            {
                LoanId = l.Id,
                Barcode = l.LoanRequest.BookCopy.Barcode,
                BookTitle = l.LoanRequest.BookCopy.Book.Title,
                BorrowerName = l.LoanRequest.Borrower.FullName,
                FineAmount = l.FineAmount > 0 ? l.FineAmount : 25000m
            })
            .ToListAsync();

        var txs = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.User)
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

        return new S17SandboxPaymentPageViewModel
        {
            CurrentUserId = user.Id,
            CurrentUserName = user.FullName,
            CurrentTier = membership?.Tier ?? "Free",
            TotalCompletedAmount = txs.Where(t => t.PaymentStatus == "Completed").Sum(t => t.Amount),
            TotalPendingAmount = txs.Where(t => t.PaymentStatus == "Pending").Sum(t => t.Amount),
            TotalVerifiedHmacCount = txs.Count(t => !string.IsNullOrWhiteSpace(t.HmacSignature)),
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
            $"Đã khởi tạo lệnh thanh toán Sandbox #{tx.Id} ({txType} - {amount:N0} VNĐ qua {gateway}) và sinh chuỗi chữ ký HMAC-SHA512. Vui lòng bấm Xác thực Callback để hoàn tất!",
            tx.Id,
            rawPayload,
            expectedHmac);
    }

    public async Task<(bool Success, string Message)> VerifyCallbackAndCompleteTransactionAsync(
        string currentUserId,
        S17SandboxCallbackDto callback)
    {
        var tx = await _db.Transactions
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Id == callback.TransactionId);

        if (tx == null)
        {
            return (false, $"Không tìm thấy giao dịch Sandbox #{callback.TransactionId}.");
        }

        string canonicalPayload = string.IsNullOrWhiteSpace(callback.RawPayload)
            ? BuildCanonicalPayload(tx.Id, tx.UserId, tx.TransactionType, tx.Amount, tx.PaymentGateway, tx.LoanId)
            : callback.RawPayload.Trim();

        string expectedHmac = ComputeHmacSha512(canonicalPayload);
        string providedHmac = (callback.HmacSignature ?? string.Empty).Trim().ToLowerInvariant();

        // Kiểm tra chữ ký điện tử HMAC-SHA512 bằng thuật toán chống Timing Attack
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHmac);
        byte[] providedBytes = Encoding.UTF8.GetBytes(providedHmac);

        if (expectedBytes.Length != providedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
        {
            tx.PaymentStatus = "Failed";
            await _db.SaveChangesAsync();
            return (false,
                $"CẢNH BÁO BẢO MẬT (MÃ 400): Chữ ký điện tử HMAC-SHA512 không hợp lệ hoặc dữ liệu Callback đã bị can thiệp! Giao dịch #{tx.Id} đã bị khóa ở trạng thái Failed.");
        }

        if (callback.ResponseCode != "00")
        {
            tx.PaymentStatus = "Failed";
            tx.HmacSignature = expectedHmac;
            await _db.SaveChangesAsync();
            return (false, $"Người dùng đã hủy thanh toán tại cổng {tx.PaymentGateway} (ResponseCode = {callback.ResponseCode}).");
        }

        // Chữ ký HMAC-SHA512 hợp lệ & ResponseCode == 00 -> Cập nhật Completed
        tx.PaymentStatus = "Completed";
        tx.HmacSignature = expectedHmac;

        string extraEffect = string.Empty;
        var now = DateTime.UtcNow;

        if (tx.TransactionType == "Membership")
        {
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
                membership.ExpiryDate = baseDate.AddMonths(12);
            }
            else
            {
                _db.Memberships.Add(new Membership
                {
                    UserId = tx.UserId,
                    Tier = "Premium",
                    MaxConcurrentLoans = 5,
                    StartDate = now,
                    ExpiryDate = now.AddMonths(12),
                    IsActive = true
                });
            }

            extraEffect = "Đã tự động kích hoạt gói thành viên Premium (Hạn mức 5 cuốn + Ưu tiên đầu hàng chờ S11).";
        }
        else if (tx.TransactionType == "Fine" && tx.LoanId.HasValue)
        {
            var loan = await _db.Loans.FirstOrDefaultAsync(l => l.Id == tx.LoanId.Value);
            if (loan != null)
            {
                loan.FineAmount = 0m;
                if (loan.Status == "Overdue")
                {
                    loan.Status = "Active";
                    loan.DueDate = now.AddDays(3);
                }
                extraEffect = $"Đã xóa nợ phí phạt trễ hạn cho hợp đồng Loan #{loan.Id}.";
            }
        }
        else if (tx.TransactionType == "SimulatedDeposit")
        {
            extraEffect = "Đã ghi nhận khoản tiền cọc mô phỏng bảo đảm sách quý (Sẽ hoàn trả 100% khi trả sách nguyên vẹn).";
        }

        _db.Notifications.Add(new Notification
        {
            UserId = tx.UserId,
            Title = $"Xác thực Callback {tx.PaymentGateway} thành công (#{tx.Id})",
            Content = $"Giao dịch {tx.TransactionType} trị giá {tx.Amount:N0} VNĐ đã khớp chữ ký HMAC-SHA512. {extraEffect}",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        return (true,
            $"Xác thực chữ ký điện tử HMAC-SHA512 hợp lệ 100%! Giao dịch #{tx.Id} ({tx.PaymentGateway} - {tx.Amount:N0} VNĐ) đã chuyển sang Completed. {extraEffect}");
    }

    public async Task<(bool Success, string Message)> RefundSimulatedDepositAsync(string currentUserId, int transactionId)
    {
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId);
        if (tx == null)
        {
            return (false, "Không tìm thấy giao dịch tiền cọc cần hoàn trả.");
        }

        if (tx.TransactionType != "SimulatedDeposit")
        {
            return (false, "Chỉ các giao dịch Tiền cọc mô phỏng (SimulatedDeposit) mới hỗ trợ hoàn cọc.");
        }

        tx.PaymentStatus = "Refunded";
        await _db.SaveChangesAsync();

        return (true, $"Đã hoàn trả 100% tiền cọc mô phỏng ({tx.Amount:N0} VNĐ) của giao dịch #{tx.Id} về ví {tx.PaymentGateway}!");
    }

    private static string BuildCanonicalPayload(
        int txId,
        string userId,
        string txType,
        decimal amount,
        string gateway,
        int? loanId)
    {
        return $"vnp_TxnRef={txId}&vnp_UserId={userId}&vnp_Type={txType}&vnp_Amount={amount:0}&vnp_Gateway={gateway}&vnp_LoanId={loanId ?? 0}";
    }

    private string ComputeHmacSha512(string rawData)
    {
        string secret = _config["VNPay:HashSecret"] is { Length: > 0 } s
            ? s
            : "BOOKSHARE_VNPAY_SANDBOX_SECRET_KEY_2026_HMAC512";

        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] dataBytes = Encoding.UTF8.GetBytes(rawData);
        byte[] hashBytes = HMACSHA512.HashData(keyBytes, dataBytes);

        // Cột HmacSignature trong SQL Server có độ dài NVARCHAR(256), chuỗi Hex của SHA-512 dài đúng 128 ký tự
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
