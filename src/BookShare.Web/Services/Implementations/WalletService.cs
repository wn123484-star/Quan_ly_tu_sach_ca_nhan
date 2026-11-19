using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [BS-WALLET-PAY] Quản lý Ví Bảo Lãnh BookShare Pay, Nạp tiền Demo ký HMAC-SHA512,
/// Thanh toán QR 2 bước có mã PIN 6 số (mặc định 123456) kèm khóa chống trừ lặp,
/// và nghiệp vụ Khóa/Hoàn/Giải ngân Cọc Escrow khi Thuê &amp; Trả sách.
/// </summary>
public class WalletService : IWalletService
{
    private const string DefaultPinHash = "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92"; // SHA-256("123456")
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PayRefLocks = new();

    private readonly BookShareDbContext _db;
    private readonly IConfiguration _config;

    public WalletService(BookShareDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<UserWallet> EnsureWalletAsync(string userId)
    {
        var wallet = await _db.UserWallets.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet != null)
        {
            return wallet;
        }

        wallet = new UserWallet
        {
            UserId = userId,
            Balance = 500000m, // Số dư demo mặc định 500.000đ để test thuê sách Escrow mượt mà
            PinHash = DefaultPinHash,
            UpdatedAt = DateTime.UtcNow
        };
        _db.UserWallets.Add(wallet);
        await _db.SaveChangesAsync();
        return wallet;
    }

    public async Task<WalletSummaryDto> GetWalletSummaryAsync(string userId)
    {
        var user = await _db.AspNetUsers
            .AsNoTracking()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return new WalletSummaryDto
            {
                UserId = userId,
                UserFullName = "Độc giả BookShare",
                UserRole = "User",
                Balance = 0m
            };
        }

        var wallet = await EnsureWalletAsync(user.Id);

        var escrowHeld = await _db.Loans
            .AsNoTracking()
            .Where(l => l.LoanRequest.BorrowerId == user.Id && l.EscrowStatus == "Held")
            .SumAsync(l => (decimal?)l.DepositHeld) ?? 0m;

        var lenderRevenue = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == user.Id && t.TransactionType == "LenderPayout" && t.PaymentStatus == "Completed")
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        var txs = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Take(20)
            .Select(t => new WalletTransactionDto
            {
                TransactionId = t.Id,
                LoanId = t.LoanId,
                TransactionType = t.TransactionType,
                Amount = t.Amount,
                PaymentGateway = t.PaymentGateway,
                PaymentStatus = t.PaymentStatus,
                HmacSignature = t.HmacSignature,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        string suffix = Math.Abs(user.Id.GetHashCode()).ToString("D4")[^4..];

        return new WalletSummaryDto
        {
            UserId = user.Id,
            UserFullName = user.FullName,
            UserRole = user.Roles.FirstOrDefault()?.Name ?? "User",
            Balance = wallet.Balance,
            EscrowHeldTotal = escrowHeld,
            LenderRevenueTotal = lenderRevenue,
            MaskedCardNumber = $"4826 •••• •••• {suffix}",
            HasCustomPin = !string.Equals(wallet.PinHash, DefaultPinHash, StringComparison.OrdinalIgnoreCase),
            UpdatedAt = wallet.UpdatedAt,
            RecentTransactions = txs
        };
    }

    public async Task<(bool Success, string Message, decimal NewBalance, int TransactionId, string HmacSignature)> TopUpDemoAsync(
        string userId,
        WalletTopUpRequestDto request)
    {
        if (request.Amount < 1000m || request.Amount > 50_000_000m)
        {
            return (false, "Số tiền nạp Demo phải nằm trong khoảng từ 1.000 VNĐ đến 50.000.000 VNĐ mỗi lượt.", 0m, 0, string.Empty);
        }

        var wallet = await EnsureWalletAsync(userId);
        var now = DateTime.UtcNow;
        wallet.Balance += request.Amount;
        wallet.UpdatedAt = now;

        string payload = $"TOPUP|USER:{userId}|AMT:{request.Amount:0}|TS:{now:O}";
        string hmac = ComputeHmacSha512(payload);

        var tx = new Transaction
        {
            UserId = userId,
            LoanId = null,
            TransactionType = "WalletTopUp",
            Amount = request.Amount,
            PaymentGateway = "BookSharePay",
            PaymentStatus = "Completed",
            HmacSignature = hmac,
            CreatedAt = now
        };
        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();

        return (true,
            $"Đã nạp thành công +{request.Amount:N0} VNĐ vào Ví Bảo Lãnh BookShare Pay (Chữ ký HMAC-SHA512 hợp lệ). Số dư mới: {wallet.Balance:N0} VNĐ.",
            wallet.Balance,
            tx.Id,
            hmac);
    }

    public async Task<(bool Success, string Message, decimal NewBalance, int TransactionId, string HmacSignature)> PayQrWithPinAsync(
        string userId,
        WalletPayQrRequestDto request)
    {
        if (request.Amount <= 0m)
        {
            return (false, "Số tiền thanh toán phải lớn hơn 0 VNĐ.", 0m, 0, string.Empty);
        }

        string cleanPin = (request.Pin ?? string.Empty).Trim();
        if (cleanPin.Length != 6 || !cleanPin.All(char.IsDigit))
        {
            return (false, "Mã PIN xác thực giao dịch phải gồm đúng 6 chữ số (PIN mặc định: 123456).", 0m, 0, string.Empty);
        }

        string cleanRef = string.IsNullOrWhiteSpace(request.PaymentRef)
            ? $"QRPAY-{Guid.NewGuid():N}"[..18].ToUpperInvariant()
            : request.PaymentRef.Trim().ToUpperInvariant();

        // [BS-WALLET-IDEMPOTENCY] Khóa theo mã thanh toán PaymentRef chống trừ lặp 2 lần
        string lockKey = $"{userId}:{cleanRef}";
        var gate = PayRefLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            string refPrefix = $"ref:{cleanRef}|";
            bool alreadyPaid = await _db.Transactions
                .AsNoTracking()
                .AnyAsync(t => t.UserId == userId && t.PaymentStatus == "Completed" && t.HmacSignature != null && t.HmacSignature.StartsWith(refPrefix));

            if (alreadyPaid)
            {
                var curWallet = await EnsureWalletAsync(userId);
                return (false, $"Giao dịch mã [{cleanRef}] đã được thanh toán trước đó. Hệ thống đã tự động chặn trừ tiền lặp!", curWallet.Balance, 0, string.Empty);
            }

            var wallet = await EnsureWalletAsync(userId);
            string providedHash = ComputeSha256(cleanPin);
            if (!FixedTimeStringEquals(providedHash, wallet.PinHash))
            {
                return (false, "Mã PIN Ví BookShare Pay không chính xác! Vui lòng kiểm tra lại (PIN mặc định: 123456).", wallet.Balance, 0, string.Empty);
            }

            if (wallet.Balance < request.Amount)
            {
                return (false, $"Số dư Ví ({wallet.Balance:N0} VNĐ) không đủ để thanh toán {request.Amount:N0} VNĐ. Vui lòng nạp thêm tiền Demo vào Ví Pay!", wallet.Balance, 0, string.Empty);
            }

            var now = DateTime.UtcNow;
            wallet.Balance -= request.Amount;
            wallet.UpdatedAt = now;

            string rawPayload = $"QRPAY|REF:{cleanRef}|USER:{userId}|AMT:{request.Amount:0}|TS:{now:O}";
            string hmacHex = ComputeHmacSha512(rawPayload);
            string storedSig = $"{refPrefix}{hmacHex[..Math.Min(128, 250 - refPrefix.Length)]}";

            var tx = new Transaction
            {
                UserId = userId,
                LoanId = request.LoanId,
                TransactionType = "QrPay",
                Amount = request.Amount,
                PaymentGateway = "BookSharePay",
                PaymentStatus = "Completed",
                HmacSignature = storedSig,
                CreatedAt = now
            };
            _db.Transactions.Add(tx);
            await _db.SaveChangesAsync();

            return (true,
                $"Thanh toán QR [{cleanRef}] trị giá {request.Amount:N0} VNĐ thành công bằng PIN 2 bước! Số dư còn lại: {wallet.Balance:N0} VNĐ.",
                wallet.Balance,
                tx.Id,
                storedSig);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<(bool Success, string Message)> ChangePinAsync(string userId, WalletChangePinRequestDto request)
    {
        string cur = (request.CurrentPin ?? string.Empty).Trim();
        string next = (request.NewPin ?? string.Empty).Trim();

        if (next.Length != 6 || !next.All(char.IsDigit))
        {
            return (false, "Mã PIN mới bắt buộc phải gồm đúng 6 chữ số.");
        }

        var wallet = await EnsureWalletAsync(userId);
        if (!FixedTimeStringEquals(ComputeSha256(cur), wallet.PinHash))
        {
            return (false, "Mã PIN hiện tại không chính xác.");
        }

        wallet.PinHash = ComputeSha256(next);
        wallet.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (true, "Đã cập nhật mã PIN bảo mật 6 chữ số cho Ví BookShare Pay thành công!");
    }

    public async Task<(bool Success, string Message)> HoldEscrowForLoanAsync(
        string borrowerId,
        Loan loan,
        decimal rentalFeeTotal,
        decimal depositAmount)
    {
        decimal totalHold = Math.Max(0m, rentalFeeTotal) + Math.Max(0m, depositAmount);
        loan.RentalFeeTotal = Math.Max(0m, rentalFeeTotal);
        loan.DepositHeld = Math.Max(0m, depositAmount);

        if (totalHold <= 0m)
        {
            loan.EscrowStatus = "None";
            return (true, "Sách miễn phí 0đ — không cần tạm giữ Escrow.");
        }

        var wallet = await EnsureWalletAsync(borrowerId);
        if (wallet.Balance < totalHold)
        {
            return (false,
                $"Số dư Ví Bảo Lãnh của người thuê ({wallet.Balance:N0} VNĐ) không đủ để tạm giữ Escrow {totalHold:N0} VNĐ (Phí thuê: {loan.RentalFeeTotal:N0}đ + Cọc bảo lãnh: {loan.DepositHeld:N0}đ). Vui lòng nạp thêm tiền tại Ví BookShare Pay!");
        }

        var now = DateTime.UtcNow;
        wallet.Balance -= totalHold;
        wallet.UpdatedAt = now;
        loan.EscrowStatus = "Held";

        if (loan.RentalFeeTotal > 0m)
        {
            _db.Transactions.Add(new Transaction
            {
                UserId = borrowerId,
                LoanId = loan.Id,
                TransactionType = "RentalFee",
                Amount = loan.RentalFeeTotal,
                PaymentGateway = "BookSharePay",
                PaymentStatus = "Completed",
                HmacSignature = ComputeHmacSha512($"RENTAL|LOAN:{loan.Id}|USER:{borrowerId}|AMT:{loan.RentalFeeTotal:0}|{now:O}"),
                CreatedAt = now
            });
        }

        if (loan.DepositHeld > 0m)
        {
            _db.Transactions.Add(new Transaction
            {
                UserId = borrowerId,
                LoanId = loan.Id,
                TransactionType = "DepositHold",
                Amount = loan.DepositHeld,
                PaymentGateway = "BookSharePay",
                PaymentStatus = "Completed",
                HmacSignature = ComputeHmacSha512($"ESCROW_HOLD|LOAN:{loan.Id}|USER:{borrowerId}|AMT:{loan.DepositHeld:0}|{now:O}"),
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync();
        return (true,
            $"Đã tạm giữ bảo lãnh Escrow {totalHold:N0} VNĐ (Phí thuê {loan.RentalFeeTotal:N0}đ + Cọc {loan.DepositHeld:N0}đ) từ Ví người thuê.");
    }

    public async Task<(bool Success, string Message)> SettleEscrowOnReturnAsync(
        Loan loan,
        string? lenderUserId,
        bool isDamaged)
    {
        if (loan.EscrowStatus != "Held" || (loan.RentalFeeTotal <= 0m && loan.DepositHeld <= 0m))
        {
            return (true, string.Empty);
        }

        var now = DateTime.UtcNow;
        string borrowerId = loan.LoanRequest.BorrowerId;

        if (isDamaged)
        {
            loan.EscrowStatus = "Deducted";
            decimal compensation = loan.RentalFeeTotal + loan.DepositHeld;
            if (!string.IsNullOrWhiteSpace(lenderUserId) && compensation > 0m)
            {
                var lenderWallet = await EnsureWalletAsync(lenderUserId);
                lenderWallet.Balance += compensation;
                lenderWallet.UpdatedAt = now;
                _db.Transactions.Add(new Transaction
                {
                    UserId = lenderUserId,
                    LoanId = loan.Id,
                    TransactionType = "LenderPayout",
                    Amount = compensation,
                    PaymentGateway = "BookSharePay",
                    PaymentStatus = "Completed",
                    HmacSignature = ComputeHmacSha512($"ESCROW_DEDUCT|LOAN:{loan.Id}|LENDER:{lenderUserId}|AMT:{compensation:0}|{now:O}"),
                    CreatedAt = now
                });
            }
            await _db.SaveChangesAsync();
            return (true, $" Phát hiện sách hư hại: Đã khấu trừ cọc Escrow ({loan.DepositHeld:N0}đ) để bồi hoàn cho đơn vị/chủ sách.");
        }

        // Trả sách nguyên vẹn: Hoàn 100% tiền cọc về Ví người thuê + Giải ngân 100% phí thuê về Ví chủ sách
        if (loan.DepositHeld > 0m)
        {
            var borrowerWallet = await EnsureWalletAsync(borrowerId);
            borrowerWallet.Balance += loan.DepositHeld;
            borrowerWallet.UpdatedAt = now;
            _db.Transactions.Add(new Transaction
            {
                UserId = borrowerId,
                LoanId = loan.Id,
                TransactionType = "DepositRefund",
                Amount = loan.DepositHeld,
                PaymentGateway = "BookSharePay",
                PaymentStatus = "Refunded",
                HmacSignature = ComputeHmacSha512($"ESCROW_REFUND|LOAN:{loan.Id}|USER:{borrowerId}|AMT:{loan.DepositHeld:0}|{now:O}"),
                CreatedAt = now
            });
        }

        string payoutNote = string.Empty;
        if (loan.RentalFeeTotal > 0m && !string.IsNullOrWhiteSpace(lenderUserId))
        {
            var lenderWallet = await EnsureWalletAsync(lenderUserId);
            lenderWallet.Balance += loan.RentalFeeTotal;
            lenderWallet.UpdatedAt = now;
            _db.Transactions.Add(new Transaction
            {
                UserId = lenderUserId,
                LoanId = loan.Id,
                TransactionType = "LenderPayout",
                Amount = loan.RentalFeeTotal,
                PaymentGateway = "BookSharePay",
                PaymentStatus = "Completed",
                HmacSignature = ComputeHmacSha512($"LENDER_PAYOUT|LOAN:{loan.Id}|LENDER:{lenderUserId}|AMT:{loan.RentalFeeTotal:0}|{now:O}"),
                CreatedAt = now
            });
            loan.EscrowStatus = "ReleasedToLender";
            payoutNote = $" và giải ngân {loan.RentalFeeTotal:N0} VNĐ phí thuê vào Ví chủ sách";
        }
        else
        {
            loan.EscrowStatus = "RefundedToBorrower";
        }

        await _db.SaveChangesAsync();
        return (true, $" Đã hoàn trả 100% tiền cọc ({loan.DepositHeld:N0} VNĐ) về Ví người thuê{payoutNote}!");
    }

    public async Task<(bool Success, string Message)> ChargeRenewalFeeAsync(
        string borrowerId,
        Loan loan,
        decimal renewalFee)
    {
        if (renewalFee <= 0m)
        {
            return (true, string.Empty);
        }

        var wallet = await EnsureWalletAsync(borrowerId);
        if (wallet.Balance < renewalFee)
        {
            return (false, $"Số dư Ví ({wallet.Balance:N0} VNĐ) không đủ để thanh toán phí gia hạn thuê {renewalFee:N0} VNĐ. Vui lòng nạp thêm vào Ví BookShare Pay.");
        }

        var now = DateTime.UtcNow;
        wallet.Balance -= renewalFee;
        wallet.UpdatedAt = now;
        loan.RentalFeeTotal += renewalFee;

        _db.Transactions.Add(new Transaction
        {
            UserId = borrowerId,
            LoanId = loan.Id,
            TransactionType = "RenewalFee",
            Amount = renewalFee,
            PaymentGateway = "BookSharePay",
            PaymentStatus = "Completed",
            HmacSignature = ComputeHmacSha512($"RENEW|LOAN:{loan.Id}|USER:{borrowerId}|AMT:{renewalFee:0}|{now:O}"),
            CreatedAt = now
        });

        await _db.SaveChangesAsync();
        return (true, $" Đã trừ phí gia hạn thuê +{renewalFee:N0} VNĐ từ Ví BookShare Pay.");
    }

    private static string ComputeSha256(string input)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private string ComputeHmacSha512(string rawData)
    {
        string secret = _config["VNPay:HashSecret"] is { Length: > 0 } s
            ? s
            : "BOOKSHARE_PAY_WALLET_HMAC512_SECRET_2026";
        byte[] hash = HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeStringEquals(string a, string b)
    {
        byte[] ba = Encoding.UTF8.GetBytes(a.ToLowerInvariant());
        byte[] bb = Encoding.UTF8.GetBytes(b.ToLowerInvariant());
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
