using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [CORE-COORDINATOR] Bộ điều phối tập trung kiểm tra điều kiện mượn (hạn mức gói + suất thưởng, sách quá hạn/tranh chấp chưa xử lý), chống duyệt trùng 1 bản sao và tự động báo hàng chờ S11 khi có sách trả.
/// </summary>
/// <remarks>Bảo đảm nhất quán nghiệp vụ mượn trả trên toàn hệ thống.</remarks>
public class LoanFlowCoordinatorService : ILoanFlowCoordinatorService
{
    private readonly BookShareDbContext _db;
    private readonly IResendEmailService _resendEmailService;

    public LoanFlowCoordinatorService(BookShareDbContext db, IResendEmailService resendEmailService)
    {
        _db = db;
        _resendEmailService = resendEmailService;
    }

    public async Task<(bool IsEligible, string Reason)> ValidateBorrowEligibilityAsync(string borrowerUserId, BookCopy copy)
    {
        if (string.IsNullOrWhiteSpace(borrowerUserId))
        {
            return (false, "Vui lòng đăng nhập tài khoản để thực hiện mượn sách.");
        }

        if (!copy.IsLendable)
        {
            return (false, "Bản sao sách này hiện đang tạm khóa tính năng cho mượn.");
        }

        if (copy.OwnerUserId == borrowerUserId)
        {
            return (false, "Bạn không thể tự tạo yêu cầu mượn sách thuộc tủ sách cá nhân của chính mình.");
        }

        var nowUtc = DateTime.UtcNow;

        bool hasNotifiedHold = await _db.Reservations.AnyAsync(r =>
            r.BookId == copy.BookId &&
            r.UserId == borrowerUserId &&
            r.Status == "Notified" &&
            (r.ExpiresAt == null || r.ExpiresAt > nowUtc));

        bool hasOtherApprovedRequest = await _db.LoanRequests.AnyAsync(lr =>
            lr.BookCopyId == copy.Id &&
            lr.BorrowerId != borrowerUserId &&
            lr.Status == "Approved" &&
            (lr.Loan == null || lr.Loan.Status != "Returned"));

        bool copyReady = copy.Status == "Available" ||
            (copy.Status == "Reserved" && hasNotifiedHold && !hasOtherApprovedRequest);

        if (!copyReady)
        {
            return (false, $"Bản sao [{copy.Barcode}] hiện đang ở trạng thái chưa sẵn sàng cho mượn.");
        }

        var borrower = await _db.AspNetUsers
            .Include(u => u.Memberships)
            .FirstOrDefaultAsync(u => u.Id == borrowerUserId);

        if (borrower == null)
        {
            return (false, "Không tìm thấy hồ sơ độc giả hợp lệ.");
        }

        if (borrower.LockoutEnd.HasValue && borrower.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, "Tài khoản của bạn hiện đang bị tạm khóa quyền giao dịch.");
        }

        if (borrower.TrustScore < 50)
        {
            return (false, $"Tạm khóa quyền mượn sách mới: Điểm uy tín của bạn hiện là {borrower.TrustScore}/100 (yêu cầu tối thiểu 50 điểm).");
        }

        int overdueCount = await _db.Loans.CountAsync(l =>
            l.LoanRequest.BorrowerId == borrowerUserId &&
            (l.Status == "Overdue" || (l.Status == "Active" && l.DueDate < nowUtc)));

        if (overdueCount > 0)
        {
            return (false, $"Tạm khóa quyền mượn sách mới: Bạn đang có {overdueCount} lượt mượn quá hạn chưa hoàn trả. Vui lòng hoàn trả sách trước khi mượn thêm.");
        }

        int openDisputeCount = await _db.Disputes.CountAsync(d =>
            d.Loan.LoanRequest.BorrowerId == borrowerUserId &&
            (d.Status == "Open" || d.Status == "UnderReview"));

        bool hasDisputedLoan = await _db.Loans.AnyAsync(l =>
            l.LoanRequest.BorrowerId == borrowerUserId && l.Status == "Disputed");

        if (openDisputeCount > 0 || hasDisputedLoan)
        {
            return (false, "Tạm khóa quyền mượn sách mới: Tài khoản của bạn đang có hồ sơ khiếu nại tranh chấp chưa được giải quyết xong.");
        }

        var effectivePlan = MembershipTierCatalog.GetEffectivePolicy(borrower.Memberships, nowUtc);
        int baseQuota = Math.Max(
            effectivePlan.MaxConcurrentLoans,
            borrower.Memberships
                .Where(m => m.IsActive && (m.ExpiryDate == null || m.ExpiryDate > nowUtc))
                .Select(m => m.MaxConcurrentLoans)
                .DefaultIfEmpty(effectivePlan.MaxConcurrentLoans)
                .Max());

        // Luật phạm vi sách theo cấp hội viên: sách Hot có từ 3 người xếp hàng chờ trở lên mở mượn trực tiếp từ gói Standard trở lên
        if (effectivePlan.Tier == "Free" && !hasNotifiedHold)
        {
            int bookHotQueueCount = await _db.Reservations.CountAsync(r =>
                r.BookId == copy.BookId && (r.Status == "Waiting" || r.Status == "Notified"));
            if (bookHotQueueCount >= 3)
            {
                return (false,
                    $"Đầu sách này đang có {bookHotQueueCount} độc giả xếp hàng Sách Hot — chỉ mở mượn trực tiếp từ gói Hội Viên Đọc Sâu (Standard) trở lên. Bạn có thể đặt trước hàng chờ hoặc nâng cấp gói hội viên bằng tiền ảo miễn phí!");
            }
        }

        int maxQuota = baseQuota + borrower.LendQuotaBonus;

        int currentOpenLoans = await _db.Loans.CountAsync(l =>
            l.LoanRequest.BorrowerId == borrowerUserId &&
            (l.Status == "Active" || l.Status == "Overdue" || l.Status == "Disputed"));

        if (currentOpenLoans >= maxQuota)
        {
            return (false, $"Bạn đang mượn {currentOpenLoans}/{maxQuota} cuốn (đạt hạn mức tối đa của gói {effectivePlan.DisplayName}). Vui lòng hoàn trả sách hoặc nâng cấp hạng hội viên bằng tiền ảo miễn phí.");
        }

        int maxPendingRequests = Math.Max(maxQuota * 2, 6);
        int currentPendingRequests = await _db.LoanRequests.CountAsync(lr =>
            lr.BorrowerId == borrowerUserId && lr.Status == "Pending");
        if (currentPendingRequests >= maxPendingRequests)
        {
            return (false, $"Bạn hiện đang có {currentPendingRequests}/{maxPendingRequests} phiếu yêu cầu mượn chờ duyệt. Vui lòng chờ duyệt hoặc hủy bớt phiếu cũ trước khi gửi thêm để tránh spam hệ thống.");
        }

        return (true, string.Empty);
    }

    public async Task<bool> FulfillNotifiedReservationAsync(string borrowerUserId, int bookId)
    {
        var nowUtc = DateTime.UtcNow;
        var notifiedRes = await _db.Reservations
            .Where(r => r.BookId == bookId && r.UserId == borrowerUserId && r.Status == "Notified")
            .OrderBy(r => r.QueuePosition)
            .FirstOrDefaultAsync();

        if (notifiedRes == null)
        {
            return false;
        }

        int oldPos = notifiedRes.QueuePosition;
        notifiedRes.Status = "Fulfilled";
        notifiedRes.ExpiresAt ??= nowUtc;

        var behind = await _db.Reservations
            .Where(r => r.BookId == bookId && (r.Status == "Waiting" || r.Status == "Notified") && r.QueuePosition > oldPos)
            .ToListAsync();

        foreach (var item in behind)
        {
            item.QueuePosition = Math.Max(1, item.QueuePosition - 1);
        }

        return true;
    }

    public async Task<(bool CanApprove, string Reason)> ValidateCopyApprovalAsync(LoanRequest loanRequest)
    {
        if (loanRequest.Status != "Pending")
        {
            return (false, $"Phiếu mượn #{loanRequest.Id} không ở trạng thái chờ duyệt.");
        }

        var copy = loanRequest.BookCopy;
        if (!copy.IsLendable)
        {
            return (false, $"Bản sao [{copy.Barcode}] hiện đang khóa cho mượn.");
        }

        var nowUtc = DateTime.UtcNow;

        bool hasAnotherActiveApproval = await _db.LoanRequests.AnyAsync(lr =>
            lr.BookCopyId == copy.Id &&
            lr.Id != loanRequest.Id &&
            lr.Status == "Approved" &&
            (lr.Loan == null || lr.Loan.Status != "Returned"));

        if (hasAnotherActiveApproval)
        {
            return (false, $"Bản sao [{copy.Barcode}] đã được duyệt hoặc đang bàn giao cho một lượt mượn khác.");
        }

        bool borrowerReservedCopy = await _db.Reservations.AnyAsync(r =>
            r.BookId == copy.BookId &&
            r.UserId == loanRequest.BorrowerId &&
            (r.Status == "Notified" || r.Status == "Fulfilled"));

        bool copyAvailableForApproval = copy.Status == "Available" ||
            (copy.Status == "Reserved" && borrowerReservedCopy);

        if (!copyAvailableForApproval)
        {
            return (false, $"Bản sao [{copy.Barcode}] hiện không còn trống để duyệt cho mượn.");
        }

        var borrower = await _db.AspNetUsers
            .Include(u => u.Memberships)
            .FirstOrDefaultAsync(u => u.Id == loanRequest.BorrowerId);

        if (borrower == null)
        {
            return (false, "Không tìm thấy thông tin người mượn.");
        }

        if (borrower.LockoutEnd.HasValue && borrower.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            return (false, $"Tài khoản của độc giả {borrower.FullName} hiện đang bị khóa, không thể duyệt mượn sách.");
        }

        if (borrower.TrustScore < 50)
        {
            return (false, $"Người mượn {borrower.FullName} hiện có điểm uy tín dưới 50 nên chưa đủ điều kiện duyệt mượn.");
        }

        bool hasOverdueOrDispute = await _db.Loans.AnyAsync(l =>
            l.LoanRequest.BorrowerId == borrower.Id &&
            (l.Status == "Overdue" || l.Status == "Disputed" || (l.Status == "Active" && l.DueDate < nowUtc)));

        if (hasOverdueOrDispute)
        {
            return (false, $"Người mượn {borrower.FullName} đang có lượt mượn quá hạn hoặc tranh chấp chưa xử lý xong.");
        }

        int baseQuota = borrower.Memberships
            .Where(m => m.IsActive && (m.ExpiryDate == null || m.ExpiryDate > nowUtc))
            .Select(m => m.MaxConcurrentLoans)
            .DefaultIfEmpty(2)
            .Max();
        int maxQuota = baseQuota + borrower.LendQuotaBonus;

        int activeCount = await _db.Loans.CountAsync(l =>
            l.LoanRequest.BorrowerId == borrower.Id &&
            (l.Status == "Active" || l.Status == "Overdue" || l.Status == "Disputed"));

        if (activeCount >= maxQuota)
        {
            return (false, $"Độc giả {borrower.FullName} đang mượn {activeCount}/{maxQuota} cuốn (đạt trần hạn mức). Không thể duyệt thêm.");
        }

        return (true, string.Empty);
    }

    public async Task<(bool NotifiedNext, string? NotifiedUserName)> HandleCopyReturnedAndNotifyQueueAsync(
        BookCopy copy,
        DateTime nowUtc)
    {
        if (!copy.IsLendable || copy.Status == "Damaged" || copy.Status == "Lost")
        {
            return (false, null);
        }

        var topWaiting = await _db.Reservations
            .Include(r => r.User)
            .Include(r => r.Book)
            .Where(r => r.BookId == copy.BookId && r.Status == "Waiting")
            .OrderBy(r => r.QueuePosition)
            .FirstOrDefaultAsync();

        if (topWaiting == null)
        {
            copy.Status = "Available";
            return (false, null);
        }

        copy.Status = "Reserved";
        topWaiting.Status = "Notified";
        topWaiting.NotifiedAt = nowUtc;
        topWaiting.ExpiresAt = nowUtc.AddHours(48);

        string title = $"[Hàng chờ sách được quan tâm] Sách \"{topWaiting.Book.Title}\" đã sẵn sàng!";
        string content = $"Bạn đang ở vị trí ưu tiên #{topWaiting.QueuePosition}. Bản sao [{copy.Barcode}] vừa được hoàn trả và giữ chỗ cho bạn trong 48 giờ (đến {topWaiting.ExpiresAt:dd/MM/yyyy HH:mm}).";

        bool sentViaApi = false;
        if (!string.IsNullOrWhiteSpace(topWaiting.User.Email))
        {
            var (sent, _, _) = await _resendEmailService.SendNotificationEmailAsync(
                topWaiting.User.Email,
                title,
                $"<div style='font-family:Roboto,sans-serif;padding:16px;'><h3>{title}</h3><p>{content}</p></div>");
            sentViaApi = sent;
        }

        _db.Notifications.Add(new Notification
        {
            UserId = topWaiting.UserId,
            Title = title,
            Content = content,
            Type = "QueueAvailable",
            IsRead = false,
            EmailSentViaResend = sentViaApi,
            CreatedAt = nowUtc
        });

        return (true, topWaiting.User.FullName);
    }
}
