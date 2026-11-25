using System.Security.Cryptography;
using System.Text;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [S12] Dịch vụ xử lý gia hạn (+7 ngày, tối đa 2 lần, chặn khi sách quá hạn hoặc đang có người xếp hàng chờ) và ghi nhận/thu phí trễ hạn 5.000đ/ngày.
/// </summary>
/// <remarks>Bảo mật Anti-IDOR: Chỉ chủ hợp đồng mượn hoặc Thủ thư/Admin được gia hạn.</remarks>
public class S12LoanRenewalFineService : IS12LoanRenewalFineService
{
    private readonly BookShareDbContext _db;
    private readonly IS17SandboxPaymentService _paymentService;
    private const decimal DailyFineRateVnd = 5000m; // 5.000đ / ngày quá hạn (Phí phạt hành chính thư viện, không phải bán sách)

    public S12LoanRenewalFineService(BookShareDbContext db, IS17SandboxPaymentService paymentService)
    {
        _db = db;
        _paymentService = paymentService;
    }

    public async Task<S12LoanRenewalFinePageViewModel> GetLoanRenewalFinePageAsync(string currentUserId, string currentUserRole = "User")
    {
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return new S12LoanRenewalFinePageViewModel
            {
                CurrentUserId = string.Empty,
                Loans = new List<S12LoanFineItemDto>(),
                FineTransactions = new List<S12FineTransactionDto>()
            };
        }

        var now = DateTime.UtcNow;
        bool isPrivileged = currentUserRole is "Librarian" or "Admin";

        var loansQuery = _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
                        .ThenInclude(b => b.Reservations)
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.Borrower)
                    .ThenInclude(b => b.Memberships)
            .AsQueryable();

        if (!isPrivileged)
        {
            loansQuery = loansQuery.Where(l =>
                l.LoanRequest.BorrowerId == currentUserId
                || l.LoanRequest.BookCopy.OwnerUserId == currentUserId);
        }

        var loans = await loansQuery
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

            var bPlan = MembershipTierCatalog.GetEffectivePolicy(l.LoanRequest.Borrower?.Memberships, now);
            return new S12LoanFineItemDto
            {
                LoanId = l.Id,
                Barcode = l.LoanRequest.BookCopy.Barcode,
                BookId = l.LoanRequest.BookCopy.BookId,
                BookTitle = l.LoanRequest.BookCopy.Book.Title,
                BorrowerId = l.LoanRequest.BorrowerId,
                BorrowerName = l.LoanRequest.Borrower?.FullName ?? "Độc giả BookShare",
                LoanType = l.LoanRequest.LoanType,
                StartDate = l.StartDate,
                DueDate = l.DueDate,
                ReturnDate = l.ReturnDate,
                RenewCount = l.RenewCount,
                MaxRenewals = bPlan.MaxRenewals,
                HasWaitingReservations = hasWaitingQueue,
                OverdueDays = overdueDays,
                CalculatedFine = Math.Max(l.FineAmount, calcFine),
                RecordedFineAmount = l.FineAmount,
                Status = l.Status
            };
        }).ToList();

        var fineTxsQuery = _db.Transactions
            .AsNoTracking()
            .Include(t => t.User)
            .Where(t => t.TransactionType == "Fine");

        if (!isPrivileged)
        {
            fineTxsQuery = fineTxsQuery.Where(t => t.UserId == currentUserId);
        }

        var fineTxs = await fineTxsQuery
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
        string currentUserRole,
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

        bool isPrivileged = currentUserRole is "Librarian" or "Admin";
        if (loan.LoanRequest.BorrowerId != currentUserId && !isPrivileged)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền gia hạn lượt mượn sách của thành viên khác.");
        }

        if (loan.Status != "Active")
        {
            return (false, $"Chỉ có thể gia hạn lượt mượn đang ở trạng thái hoạt động (Hiện tại: {loan.Status}).", null, loan.RenewCount);
        }

        if (loan.DueDate < DateTime.UtcNow)
        {
            return (false, $"Hợp đồng mượn #{loan.Id} đã quá hạn trả ({loan.DueDate:dd/MM/yyyy}), không thể tự gia hạn. Vui lòng hoàn trả sách hoặc liên hệ Thủ thư.", null, loan.RenewCount);
        }

        var borrowerMemberships = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == loan.LoanRequest.BorrowerId && m.IsActive)
            .ToListAsync();
        var tierPlan = MembershipTierCatalog.GetEffectivePolicy(borrowerMemberships, DateTime.UtcNow);

        if (loan.RenewCount >= tierPlan.MaxRenewals)
        {
            return (false, $"Hợp đồng mượn #{loan.Id} đã đạt giới hạn gia hạn tối đa của gói {tierPlan.DisplayName} ({loan.RenewCount}/{tierPlan.MaxRenewals} lần).", null, loan.RenewCount);
        }

        bool hasQueue = loan.LoanRequest.BookCopy.Book.Reservations
            .Any(r => r.Status == "Waiting" || r.Status == "Notified");

        if (hasQueue)
        {
            return (false, $"Từ chối gia hạn: Đầu sách \"{loan.LoanRequest.BookCopy.Book.Title}\" đang có độc giả xếp hàng đặt trước.", null, loan.RenewCount);
        }

        loan.RenewCount += 1;
        loan.DueDate = loan.DueDate.AddDays(tierPlan.RenewalDays);

        await _db.SaveChangesAsync();

        return (true, $"Đã gia hạn hợp đồng mượn #{loan.Id} ([{loan.LoanRequest.BookCopy.Barcode}]) thêm +{tierPlan.RenewalDays} ngày theo quyền lợi gói {tierPlan.DisplayName}! Lần gia hạn: {loan.RenewCount}/{tierPlan.MaxRenewals} — Hạn trả mới: {loan.DueDate:dd/MM/yyyy}.", loan.DueDate, loan.RenewCount);
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

        if (actualOverdueDays <= 0)
        {
            loan.DueDate = now.AddDays(-daysToCharge);
        }

        decimal fineAmount = daysToCharge * DailyFineRateVnd;

        var tx = new Transaction
        {
            UserId = loan.LoanRequest.BorrowerId,
            LoanId = loan.Id,
            TransactionType = "Fine",
            Amount = fineAmount,
            PaymentGateway = "QrPay",
            PaymentStatus = request.MarkPaidImmediately ? "Completed" : "Pending",
            Description = $"Nộp phí trễ hạn {daysToCharge} ngày cho hợp đồng mượn #{loan.Id} ([{loan.LoanRequest.BookCopy.Barcode}])",
            HmacSignature = null,
            CreatedAt = now
        };

        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();

        if (request.MarkPaidImmediately)
        {
            string canonical = _paymentService.BuildCanonicalPayload(
                tx.Id,
                tx.UserId,
                tx.TransactionType,
                tx.Amount,
                tx.PaymentGateway,
                tx.LoanId);
            tx.HmacSignature = _paymentService.ComputeHmacSha512(canonical);
            loan.FineAmount = 0m;
            if (loan.Status == "Overdue")
            {
                loan.Status = loan.ReturnDate.HasValue ? "Returned" : "Active";
                if (!loan.ReturnDate.HasValue)
                {
                    loan.DueDate = now.AddDays(3);
                }
            }
        }
        else
        {
            loan.FineAmount = fineAmount;
            if (loan.Status == "Active")
            {
                loan.Status = "Overdue";
            }
        }

        _db.Notifications.Add(new Notification
        {
            UserId = loan.LoanRequest.BorrowerId,
            Title = request.MarkPaidImmediately
                ? $"Đã thanh toán phí trễ hạn {fineAmount:N0} VNĐ (Mã GD #{tx.Id})"
                : $"Thông báo phí trễ hạn {fineAmount:N0} VNĐ cho lượt mượn #{loan.Id}",
            Content = request.MarkPaidImmediately
                ? $"Khoản phí trễ hạn {daysToCharge} ngày cho sách [{loan.LoanRequest.BookCopy.Barcode}] đã được ghi nhận hoàn tất."
                : $"Lượt mượn sách [{loan.LoanRequest.BookCopy.Barcode}] phát sinh phí trễ hạn {daysToCharge} ngày ({fineAmount:N0} VNĐ). Vui lòng hoàn tất thanh toán tại Ví Bảo Lãnh.",
            Type = "DueReminder",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        return (true,
            $"Đã tính phí trễ hạn {daysToCharge} ngày ({fineAmount:N0} VNĐ) cho hợp đồng mượn #{loan.Id} ([{loan.LoanRequest.BookCopy.Barcode}]) và tạo phiếu thu #{tx.Id}!",
            fineAmount,
            tx.Id);
    }
}
