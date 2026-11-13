using System.Text.Json;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S19DisputeResolutionService : IS19DisputeResolutionService
{
    private readonly BookShareDbContext _db;

    public S19DisputeResolutionService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S19DisputeResolutionPageViewModel> GetDisputeResolutionPageAsync(
        string currentUserId,
        string currentUserRole)
    {
        var loans = await _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.Borrower)
            .Include(l => l.ConditionReports)
            .OrderByDescending(l => l.StartDate)
            .ToListAsync();

        var eligibleLoans = loans.Select(l =>
        {
            var handover = l.ConditionReports.FirstOrDefault(c => c.ReportType == "Handover");
            var ret = l.ConditionReports.FirstOrDefault(c => c.ReportType == "Return");
            return new S19LoanForDisputeDto
            {
                LoanId = l.Id,
                Barcode = l.LoanRequest.BookCopy.Barcode,
                BookTitle = l.LoanRequest.BookCopy.Book.Title,
                BorrowerId = l.LoanRequest.BorrowerId,
                BorrowerName = l.LoanRequest.Borrower.FullName,
                LoanStatus = l.Status,
                HandoverImageUrl = handover?.ImageUrl,
                ReturnImageUrl = ret?.ImageUrl
            };
        }).ToList();

        var disputes = await _db.Disputes
            .AsNoTracking()
            .Include(d => d.Loan)
                .ThenInclude(l => l.LoanRequest)
                    .ThenInclude(lr => lr.BookCopy)
                        .ThenInclude(bc => bc.Book)
            .Include(d => d.Loan)
                .ThenInclude(l => l.LoanRequest)
                    .ThenInclude(lr => lr.Borrower)
            .Include(d => d.Loan)
                .ThenInclude(l => l.ConditionReports)
            .Include(d => d.RaisedByUser)
            .Include(d => d.ResolvedByAdmin)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        var disputeDtos = disputes.Select(d =>
        {
            var handoverReport = d.Loan.ConditionReports
                .OrderBy(c => c.CreatedAt)
                .FirstOrDefault(c => c.ReportType == "Handover");

            var returnReport = d.Loan.ConditionReports
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefault(c => c.ReportType == "Return");

            string defaultHandoverImg = handoverReport?.ImageUrl
                ?? "https://images.unsplash.com/photo-1544716278-ca5e3f4abd8c?w=600";

            string defaultReturnImg = returnReport?.ImageUrl
                ?? ExtractFirstEvidenceUrl(d.EvidenceUrlsJson)
                ?? "https://images.unsplash.com/photo-1589829085413-56de8ae18c73?w=600";

            return new S19DisputeDetailDto
            {
                DisputeId = d.Id,
                LoanId = d.LoanId,
                Barcode = d.Loan.LoanRequest.BookCopy.Barcode,
                BookTitle = d.Loan.LoanRequest.BookCopy.Book.Title,
                BorrowerId = d.Loan.LoanRequest.BorrowerId,
                BorrowerName = d.Loan.LoanRequest.Borrower.FullName,
                BorrowerTrustScore = d.Loan.LoanRequest.Borrower.TrustScore,
                RaisedByUserId = d.RaisedByUserId,
                RaisedByName = d.RaisedByUser.FullName,
                Reason = d.Reason,
                Status = d.Status,
                ResolvedByAdminName = d.ResolvedByAdmin?.FullName,
                ResolutionNote = d.ResolutionNote,
                HandoverGrade = handoverReport?.ConditionGrade ?? "New",
                HandoverImageUrl = defaultHandoverImg,
                ReturnGrade = returnReport?.ConditionGrade ?? "Damaged",
                ReturnImageUrl = defaultReturnImg,
                CreatedAt = d.CreatedAt,
                ResolvedAt = d.ResolvedAt
            };
        }).ToList();

        return new S19DisputeResolutionPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserRole = currentUserRole,
            OpenDisputesCount = disputeDtos.Count(d => d.Status == "Open" || d.Status == "UnderReview"),
            ResolvedDisputesCount = disputeDtos.Count(d => d.Status == "Resolved" || d.Status == "Rejected"),
            EligibleLoans = eligibleLoans,
            Disputes = disputeDtos
        };
    }

    public async Task<(bool Success, string Message, int DisputeId)> CreateDisputeAsync(
        string currentUserId,
        S19CreateDisputeRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return (false, "Vui lòng nhập lý do khiếu nại tình trạng sách.", 0);
        }

        var loan = await _db.Loans
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .FirstOrDefaultAsync(l => l.Id == request.LoanId);

        if (loan == null)
        {
            return (false, "Không tìm thấy hợp đồng mượn sách cần khiếu nại.", 0);
        }

        var now = DateTime.UtcNow;
        string evidenceUrl = string.IsNullOrWhiteSpace(request.EvidenceImageUrl)
            ? "https://images.unsplash.com/photo-1589829085413-56de8ae18c73?w=600"
            : request.EvidenceImageUrl.Trim();

        var dispute = new Dispute
        {
            LoanId = loan.Id,
            RaisedByUserId = currentUserId,
            Reason = request.Reason.Trim(),
            EvidenceUrlsJson = JsonSerializer.Serialize(new[] { evidenceUrl }),
            Status = "Open",
            CreatedAt = now
        };

        loan.Status = "Disputed";
        _db.Disputes.Add(dispute);

        // Nếu chưa có biên bản Return trong ConditionReports thì bổ sung để Admin đối chiếu 2 bên
        bool hasReturnReport = await _db.ConditionReports.AnyAsync(c => c.LoanId == loan.Id && c.ReportType == "Return");
        if (!hasReturnReport)
        {
            _db.ConditionReports.Add(new ConditionReport
            {
                LoanId = loan.Id,
                ReportType = "Return",
                ReportedByUserId = currentUserId,
                ConditionGrade = "Damaged",
                Note = $"Ảnh bằng chứng kèm hồ sơ khiếu nại: {request.Reason.Trim()}",
                ImageUrl = evidenceUrl,
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync();

        return (true,
            $"Đã mở hồ sơ khiếu nại #DSP-{dispute.Id:D4} cho bản sao [{loan.LoanRequest.BookCopy.Barcode}] ({loan.LoanRequest.BookCopy.Book.Title}). Trạng thái hợp đồng đã chuyển sang 'Disputed' chờ Admin phân xử!",
            dispute.Id);
    }

    public async Task<(bool Success, string Message)> ResolveDisputeByAdminAsync(
        string adminUserId,
        S19AdminResolveDisputeDto request)
    {
        var dispute = await _db.Disputes
            .Include(d => d.Loan)
                .ThenInclude(l => l.LoanRequest)
                    .ThenInclude(lr => lr.BookCopy)
                        .ThenInclude(bc => bc.Book)
            .Include(d => d.Loan)
                .ThenInclude(l => l.LoanRequest)
                    .ThenInclude(lr => lr.Borrower)
            .FirstOrDefaultAsync(d => d.Id == request.DisputeId);

        if (dispute == null)
        {
            return (false, "Không tìm thấy hồ sơ tranh chấp cần phân xử.");
        }

        var allowedStatuses = new[] { "Resolved", "Rejected", "UnderReview" };
        string status = allowedStatuses.Contains(request.DecisionStatus) ? request.DecisionStatus : "Resolved";
        var now = DateTime.UtcNow;

        dispute.Status = status;
        dispute.ResolvedByAdminId = adminUserId;
        dispute.ResolutionNote = string.IsNullOrWhiteSpace(request.ResolutionNote)
            ? "Admin đã đối chiếu ảnh ConditionReports lúc giao và lúc trả để ban hành phán quyết."
            : request.ResolutionNote.Trim();
        dispute.ResolvedAt = now;

        var borrower = dispute.Loan.LoanRequest.Borrower;
        var bookCopy = dispute.Loan.LoanRequest.BookCopy;
        int penalty = Math.Clamp(request.TrustScorePenalty, 0, 50);

        if (status == "Resolved" && penalty > 0)
        {
            borrower.TrustScore = Math.Clamp(borrower.TrustScore - penalty, 0, 200);
        }

        if (!string.IsNullOrWhiteSpace(request.CopyTargetStatus) &&
            new[] { "Available", "Damaged", "Lost" }.Contains(request.CopyTargetStatus))
        {
            bookCopy.Status = request.CopyTargetStatus;
            if (request.CopyTargetStatus == "Damaged" || request.CopyTargetStatus == "Lost")
            {
                bookCopy.Condition = "Worn";
                bookCopy.IsLendable = false;
            }
        }

        if (request.DepositDeductionAmount > 0)
        {
            dispute.Loan.FineAmount += request.DepositDeductionAmount;
            _db.Transactions.Add(new Transaction
            {
                UserId = borrower.Id,
                LoanId = dispute.LoanId,
                TransactionType = "Fine",
                Amount = request.DepositDeductionAmount,
                PaymentGateway = "VNPaySandbox",
                PaymentStatus = "Completed",
                HmacSignature = $"admin-dispute-{dispute.Id}-deduction",
                CreatedAt = now
            });
        }

        _db.Notifications.Add(new Notification
        {
            UserId = borrower.Id,
            Title = $"Phán quyết hồ sơ tranh chấp #DSP-{dispute.Id:D4} ({status})",
            Content = $"Admin đã xử lý tranh chấp cuốn \"{bookCopy.Book.Title}\": {dispute.ResolutionNote}. Biến động TrustScore: -{penalty} điểm (Hiện tại: {borrower.TrustScore}).",
            Type = "Dispute",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        return (true,
            $"Admin đã ban hành phán quyết [{status}] cho hồ sơ #DSP-{dispute.Id:D4}: Trừ {penalty} điểm TrustScore của {borrower.FullName} (Còn {borrower.TrustScore} điểm), cập nhật bản sao [{bookCopy.Barcode}] sang '{bookCopy.Status}'.");
    }

    private static string? ExtractFirstEvidenceUrl(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(json);
            return arr?.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
