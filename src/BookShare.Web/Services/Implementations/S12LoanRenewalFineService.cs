using System.Security.Cryptography;
using System.Text;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S12LoanRenewalFineService : IS12LoanRenewalFineService
{
    private readonly BookShareDbContext _db;
    private const decimal DailyFineRateVnd = 5000m; // 5.000đ / ngày quá hạn (Phí phạt hành chính thư viện, không phải bán sách)

    public S12LoanRenewalFineService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S12LoanRenewalFinePageViewModel> GetLoanRenewalFinePageAsync(string currentUserId)
    {
        var now = DateTime.UtcNow;

        var loans = await _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
                        .ThenInclude(b => b.Reservations)
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.Borrower)
            .OrderByDescending(l => l.Status == "Overdue")
            .ThenByDescending(l => l.Status == "Active")
            .ThenBy(l => l.DueDate)
            .ToListAsync();

        var loanItems = loans.Select(l =>
        {
            var effectiveEnd = l.ReturnDate ?? now;
            int overdueDays = effectiveEnd > l.DueDate
                ? Math.Max(0, (int)Math.Ceiling((effectiveEnd - l.DueDate).TotalDays))
                : 0;

            decimal calcFine = overdueDays * DailyFineRateVnd;
            bool hasWaitingQueue = l.LoanRequest.BookCopy.Book.Reservations
                .Any(r => r.Status == "Waiting" || r.Status == "Notified");

            return new S12LoanFineItemDto
            {
                LoanId = l.Id,
                Barcode = l.LoanRequest.BookCopy.Barcode,
                BookId = l.LoanRequest.BookCopy.BookId,
                BookTitle = l.LoanRequest.BookCopy.Book.Title,
                BorrowerId = l.LoanRequest.BorrowerId,
                BorrowerName = l.LoanRequest.Borrower.FullName,
                LoanType = l.LoanRequest.LoanType,
                StartDate = l.StartDate,
                DueDate = l.DueDate,
                ReturnDate = l.ReturnDate,
                RenewCount = l.RenewCount,
                MaxRenewals = 2,
                HasWaitingReservations = hasWaitingQueue,
                OverdueDays = overdueDays,
                CalculatedFine = Math.Max(l.FineAmount, calcFine),
                RecordedFineAmount = l.FineAmount,
                Status = l.Status
            };
        }).ToList();

        var fineTxs = await _db.Transactions
            .AsNoTracking()
            .Include(t => t.User)
            .Where(t => t.TransactionType == "Fine")
            .OrderByDescending(t => t.CreatedAt)
            .Take(15)
            .Select(t => new S12FineTransactionDto
            {
                TransactionId = t.Id,
                LoanId = t.LoanId,
                UserFullName = t.User.FullName,
                Amount = t.Amount,
                PaymentGateway = t.PaymentGateway,
                PaymentStatus = t.PaymentStatus,
                HmacSignature = t.HmacSignature,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        return new S12LoanRenewalFinePageViewModel
        {
            CurrentUserId = currentUserId,
            ActiveLoansCount = loanItems.Count(x => x.Status == "Active"),
            OverdueLoansCount = loanItems.Count(x => x.OverdueDays > 0 || x.Status == "Overdue"),
            TotalOutstandingFines = loanItems.Sum(x => x.CalculatedFine),
            Loans = loanItems,
            FineTransactions = fineTxs
        };
    }

    public async Task<(bool Success, string Message, DateTime? NewDueDate, int NewRenewCount)> RenewLoanAsync(
        string currentUserId,
        int loanId)
    {
        var loan = await _db.Loans
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
                        .ThenInclude(b => b.Reservations)
            .FirstOrDefaultAsync(l => l.Id == loanId);

        if (loan == null)
        {
            return (false, "Không tìm thấy hợp đồng mượn sách.", null, 0);
        }

        if (loan.Status != "Active")
        {
            return (false, $"Chỉ có thể gia hạn hợp đồng đang ở trạng thái 'Active' (Hiện tại: {loan.Status}).", null, loan.RenewCount);
        }

        if (loan.RenewCount >= 2)
        {
            return (false, $"Hợp đồng Loan #{loan.Id} đã đạt giới hạn gia hạn tối đa (2/2 lần).", null, loan.RenewCount);
        }

        bool hasQueue = loan.LoanRequest.BookCopy.Book.Reservations
            .Any(r => r.Status == "Waiting" || r.Status == "Notified");

        if (hasQueue)
        {
            return (false, $"Từ chối gia hạn: Đầu sách \"{loan.LoanRequest.BookCopy.Book.Title}\" đang có độc giả xếp hàng đặt trước (S11 HotBookQueue).", null, loan.RenewCount);
        }

        loan.RenewCount += 1;
        loan.DueDate = loan.DueDate.AddDays(7);

        await _db.SaveChangesAsync();

        return (true, $"Đã gia hạn hợp đồng Loan #{loan.Id} ([{loan.LoanRequest.BookCopy.Barcode}]) thêm +7 ngày! Lần gia hạn: {loan.RenewCount}/2 — Hạn trả mới: {loan.DueDate:dd/MM/yyyy}.", loan.DueDate, loan.RenewCount);
    }

    public async Task<(bool Success, string Message, decimal FineAmount, int TransactionId)> AssessOverdueFineAsync(
        string currentUserId,
        AssessOverdueFineRequestDto request)
    {
        var loan = await _db.Loans
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.Borrower)
            .FirstOrDefaultAsync(l => l.Id == request.LoanId);

        if (loan == null)
        {
            return (false, "Không tìm thấy hợp đồng mượn sách cần tính phạt.", 0, 0);
        }

        var now = DateTime.UtcNow;
        int actualOverdueDays = now > loan.DueDate
            ? (int)Math.Ceiling((now - loan.DueDate).TotalDays)
            : 0;

        int daysToCharge = actualOverdueDays > 0
            ? actualOverdueDays
            : Math.Clamp(request.SimulatedOverdueDays, 1, 60);

        // Nếu hợp đồng chưa quá hạn thật trên đồng hồ thì lùi DueDate để mô phỏng tình huống trễ hạn thực tế
        if (actualOverdueDays <= 0)
        {
            loan.DueDate = now.AddDays(-daysToCharge);
        }

        decimal fineAmount = daysToCharge * DailyFineRateVnd;
        loan.FineAmount = fineAmount;
        if (loan.Status == "Active")
        {
            loan.Status = "Overdue";
        }

        string rawPayload = $"FINE|LOAN:{loan.Id}|USER:{loan.LoanRequest.BorrowerId}|AMOUNT:{fineAmount}|TS:{now:O}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload));
        string signature = Convert.ToHexString(hashBytes).ToLowerInvariant()[..32];

        var tx = new Transaction
        {
            UserId = loan.LoanRequest.BorrowerId,
            LoanId = loan.Id,
            TransactionType = "Fine",
            Amount = fineAmount,
            PaymentGateway = "VNPaySandbox",
            PaymentStatus = request.MarkPaidImmediately ? "Completed" : "Pending",
            HmacSignature = $"hmac-sha256:{signature}",
            CreatedAt = now
        };

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();

        return (true,
            $"Đã tính phí trễ hạn {daysToCharge} ngày ({fineAmount:N0} VNĐ) cho hợp đồng Loan #{loan.Id} ([{loan.LoanRequest.BookCopy.Barcode}]) và tạo phiếu thu Sandbox #{tx.Id}!",
            fineAmount,
            tx.Id);
    }
}
