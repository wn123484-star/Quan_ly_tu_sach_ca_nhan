using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.BackgroundJobs;

public class BookShareDailyJobs
{
    private readonly BookShareDbContext _db;
    private readonly IResendEmailService _resendEmailService;
    private readonly ILogger<BookShareDailyJobs> _logger;
    private const decimal DailyFineRateVnd = 5000m;

    public BookShareDailyJobs(
        BookShareDbContext db,
        IResendEmailService resendEmailService,
        ILogger<BookShareDailyJobs> logger)
    {
        _db = db;
        _resendEmailService = resendEmailService;
        _logger = logger;
    }

    /// <summary>
    /// Job 1: Quét các hợp đồng mượn sách sắp đến hạn hoặc quá hạn để gửi Email qua Resend & tạo Notification.
    /// </summary>
    public async Task<string> SendDueDateRemindersJobAsync()
    {
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(7);

        var dueLoans = await _db.Loans
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.Borrower)
            .Where(l => (l.Status == "Active" || l.Status == "Overdue") && l.DueDate <= horizon)
            .ToListAsync();

        int sentCount = 0;
        foreach (var loan in dueLoans)
        {
            var borrower = loan.LoanRequest.Borrower;
            var bookTitle = loan.LoanRequest.BookCopy.Book.Title;
            var barcode = loan.LoanRequest.BookCopy.Barcode;
            bool isOverdue = now > loan.DueDate;
            int daysDiff = (int)Math.Ceiling(Math.Abs((loan.DueDate - now).TotalDays));

            string title = isOverdue
                ? $"[Cảnh báo Quá hạn {daysDiff} ngày] Vui lòng hoàn trả sách {bookTitle}"
                : $"[Nhắc hạn trả sách] Cuốn \"{bookTitle}\" còn {daysDiff} ngày đến hạn";

            string content = isOverdue
                ? $"Hợp đồng mượn #{loan.Id} (Mã bản sao: {barcode}) đã quá hạn {daysDiff} ngày. Phí trễ hạn tạm tính: {(daysDiff * DailyFineRateVnd):N0}đ. Vui lòng hoàn trả sách tại Thư viện/Hub."
                : $"Hợp đồng mượn #{loan.Id} (Mã bản sao: {barcode}) sẽ đến hạn vào ngày {loan.DueDate:dd/MM/yyyy}. Bạn có thể gia hạn tại dịch vụ S12 nếu sách chưa có hàng chờ.";

            if (!string.IsNullOrWhiteSpace(borrower.Email))
            {
                await _resendEmailService.SendNotificationEmailAsync(
                    borrower.Email,
                    title,
                    $"<div style='font-family:Roboto,sans-serif;padding:16px;border:1px solid #cbd5e1;border-radius:10px;'>" +
                    $"<h3 style='color:#1e3a8a;'>{title}</h3><p>{content}</p></div>");
            }

            _db.Notifications.Add(new Notification
            {
                UserId = borrower.Id,
                Title = title,
                Content = content,
                Type = "DueReminder",
                IsRead = false,
                EmailSentViaResend = true,
                CreatedAt = now
            });
            sentCount++;
        }

        if (sentCount > 0)
        {
            await _db.SaveChangesAsync();
        }

        string summary = $"[DueDateReminderJob] Đã quét và gửi {sentCount} thông báo/Email nhắc hạn trả sách qua IResendEmailService.";
        _logger.LogInformation(summary);
        return summary;
    }

    /// <summary>
    /// Job 2: Tự động tính phí phạt trễ hạn (5.000đ/ngày) cho các hợp đồng quá hạn trả sách.
    /// </summary>
    public async Task<string> CalculateOverdueFinesJobAsync()
    {
        var now = DateTime.UtcNow;

        var overdueLoans = await _db.Loans
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
            .Where(l => (l.Status == "Active" || l.Status == "Overdue") && l.DueDate < now)
            .ToListAsync();

        int updatedCount = 0;
        decimal totalCalculatedFine = 0m;

        foreach (var loan in overdueLoans)
        {
            int overdueDays = Math.Max(1, (int)Math.Ceiling((now - loan.DueDate).TotalDays));
            decimal fine = overdueDays * DailyFineRateVnd;

            loan.Status = "Overdue";
            loan.FineAmount = fine;
            totalCalculatedFine += fine;
            updatedCount++;
        }

        if (updatedCount > 0)
        {
            await _db.SaveChangesAsync();
        }

        string summary = $"[OverdueFineCalculatorJob] Đã cập nhật {updatedCount} hợp đồng quá hạn (Tổng phí phạt luỹ kế: {totalCalculatedFine:N0} VNĐ).";
        _logger.LogInformation(summary);
        return summary;
    }

    /// <summary>
    /// Job 3: Tự động hủy các yêu cầu mượn sách (LoanRequests) treo quá 48 giờ không được duyệt
    /// và các lượt giữ chỗ hàng chờ (Reservations) đã thông báo quá 48 giờ không nhận sách.
    /// </summary>
    public async Task<string> CancelExpiredRequestsAndReservationsJobAsync()
    {
        var now = DateTime.UtcNow;
        var cutoff48h = now.AddHours(-48);

        var staleRequests = await _db.LoanRequests
            .Include(lr => lr.BookCopy)
            .Where(lr => lr.Status == "Pending" && lr.RequestedAt <= cutoff48h)
            .ToListAsync();

        int expiredReqCount = 0;
        foreach (var req in staleRequests)
        {
            req.Status = "Expired";
            if (req.BookCopy.Status == "Reserved")
            {
                req.BookCopy.Status = "Available";
            }

            _db.Notifications.Add(new Notification
            {
                UserId = req.BorrowerId,
                Title = $"Phiếu yêu cầu mượn #{req.Id} đã tự động hủy sau 48h",
                Content = $"Yêu cầu mượn bản sao [{req.BookCopy.Barcode}] quá 48 giờ chưa được xác nhận nên đã chuyển sang trạng thái Expired.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });
            expiredReqCount++;
        }

        var expiredReservations = await _db.Reservations
            .Where(r => r.Status == "Notified" && r.ExpiresAt != null && r.ExpiresAt <= now)
            .ToListAsync();

        int expiredResCount = 0;
        foreach (var res in expiredReservations)
        {
            res.Status = "Expired";
            expiredResCount++;
        }

        if (expiredReqCount > 0 || expiredResCount > 0)
        {
            await _db.SaveChangesAsync();
        }

        string summary = $"[ExpiredCleanupJob] Đã tự động hủy {expiredReqCount} phiếu mượn quá 48h không duyệt và {expiredResCount} lượt giữ chỗ hết hạn.";
        _logger.LogInformation(summary);
        return summary;
    }

    /// <summary>
    /// Thực thi theo mã Job từ Dashboard Admin S22 hoặc chạy toàn bộ 3 Daily Jobs.
    /// </summary>
    public async Task<(bool Success, string Message)> ExecuteJobByKeyAsync(string jobKey)
    {
        switch (jobKey)
        {
            case "DueReminder":
                return (true, await SendDueDateRemindersJobAsync());
            case "OverdueFine":
                return (true, await CalculateOverdueFinesJobAsync());
            case "ExpireCleanup":
                return (true, await CancelExpiredRequestsAndReservationsJobAsync());
            default:
                var r1 = await SendDueDateRemindersJobAsync();
                var r2 = await CalculateOverdueFinesJobAsync();
                var r3 = await CancelExpiredRequestsAndReservationsJobAsync();
                return (true, $"{r1} | {r2} | {r3}");
        }
    }
}
