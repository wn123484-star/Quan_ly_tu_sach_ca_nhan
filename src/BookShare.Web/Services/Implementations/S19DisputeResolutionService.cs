using System.Text.Json;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [S19] Dịch vụ tạo/sửa/rút hồ sơ khiếu nại (tạm khóa hợp đồng mượn), cách ly dữ liệu theo bên liên quan và thực thi phán quyết của Quản trị viên (trừ điểm uy tín, cập nhật trạng thái bản sao, phát hành hóa đơn bồi thường S17).
/// </summary>
/// <remarks>Chống spam tối đa 6 hồ sơ/10 phút và kiểm tra URL ảnh bằng chứng an toàn.</remarks>
public class S19DisputeResolutionService : IS19DisputeResolutionService
{
    private readonly BookShareDbContext _db;
    private readonly IS17SandboxPaymentService _paymentService;
    private readonly IAntiSpamRateLimiterService _rateLimiter;

    public S19DisputeResolutionService(
        BookShareDbContext db,
        IS17SandboxPaymentService paymentService,
        IAntiSpamRateLimiterService rateLimiter)
    {
        _db = db;
        _paymentService = paymentService;
        _rateLimiter = rateLimiter;
    }

    public async Task<S19DisputeResolutionPageViewModel> GetDisputeResolutionPageAsync(
        string currentUserId,
        string currentUserRole)
    {
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return new S19DisputeResolutionPageViewModel
            {
                CurrentUserId = string.Empty,
                CurrentUserRole = "Guest",
                EligibleLoans = new List<S19LoanForDisputeDto>(),
                Disputes = new List<S19DisputeDetailDto>()
            };
        }

        bool isPrivileged = currentUserRole is "Librarian" or "Curator" or "Admin";

        var loansQuery = _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.Borrower)
            .Include(l => l.ConditionReports)
            .AsQueryable();

        if (!isPrivileged)
        {
            loansQuery = loansQuery.Where(l =>
                l.LoanRequest.BorrowerId == currentUserId ||
                l.LoanRequest.BookCopy.OwnerUserId == currentUserId);
        }

        var loans = await loansQuery
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

        var disputesQuery = _db.Disputes
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
            .AsQueryable();

        if (!isPrivileged)
        {
            disputesQuery = disputesQuery.Where(d =>
                d.RaisedByUserId == currentUserId ||
                d.Loan.LoanRequest.BorrowerId == currentUserId ||
                d.Loan.LoanRequest.BookCopy.OwnerUserId == currentUserId);
        }

        var disputes = await disputesQuery
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
                ?? "/images/book-placeholder.svg";

            string defaultReturnImg = returnReport?.ImageUrl
                ?? ExtractFirstEvidenceUrl(d.EvidenceUrlsJson)
                ?? "/images/book-placeholder.svg";

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
        string currentUserRole,
        S19CreateDisputeRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return (false, "Vui lòng nhập lý do khiếu nại tình trạng sách.", 0);
        }

        if (request.Reason.Trim().Length > 2000)
        {
            return (false, "Lý do khiếu nại không được vượt quá 2.000 ký tự.", 0);
        }

        if (!AuthConfigHelper.IsSafeImageUrl(request.EvidenceImageUrl))
        {
            return (false, "Đường dẫn ảnh bằng chứng không hợp lệ hoặc không an toàn.", 0);
        }

        var (allowed, retrySec, rlMsg) = _rateLimiter.TryConsume(
            "s19-create-dispute",
            currentUserId,
            maxRequests: 6,
            window: TimeSpan.FromMinutes(10),
            customMessage: "Bạn đã gửi quá nhiều hồ sơ khiếu nại trong 10 phút (tối đa 6 hồ sơ). Vui lòng đợi Quản trị viên xử lý.");
        if (!allowed)
        {
            throw new RateLimitExceededException(rlMsg, retrySec);
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

        bool isParticipant = loan.LoanRequest.BorrowerId == currentUserId ||
                             loan.LoanRequest.BookCopy.OwnerUserId == currentUserId;
        bool isPrivileged = currentUserRole is "Librarian" or "Curator" or "Admin";
        if (!isParticipant && !isPrivileged)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền mở hồ sơ khiếu nại đối với lượt mượn không liên quan tới mình.");
        }

        if (!isPrivileged)
        {
            int userOpenDisputes = await _db.Disputes.CountAsync(d =>
                d.RaisedByUserId == currentUserId && (d.Status == "Open" || d.Status == "UnderReview"));
            if (userOpenDisputes >= 5)
            {
                return (false, "Bạn đang có 5 hồ sơ khiếu nại chưa xử lý xong. Vui lòng đợi Quản trị viên phân xử trước khi mở thêm.", 0);
            }
        }

        bool hasOpenDispute = await _db.Disputes.AnyAsync(d =>
            d.LoanId == loan.Id && (d.Status == "Open" || d.Status == "UnderReview"));
        if (hasOpenDispute)
        {
            return (false, $"Hợp đồng mượn #{loan.Id} hiện đã có một hồ sơ khiếu nại đang chờ xử lý.", 0);
        }

        var now = DateTime.UtcNow;
        string evidenceUrl = string.IsNullOrWhiteSpace(request.EvidenceImageUrl)
            ? "/images/book-placeholder.svg"
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
            $"Đã mở hồ sơ khiếu nại #DSP-{dispute.Id:D4} cho bản sao [{loan.LoanRequest.BookCopy.Barcode}] ({loan.LoanRequest.BookCopy.Book.Title}). Hợp đồng đang được tạm giữ để Quản trị viên phân xử!",
            dispute.Id);
    }

    public async Task<(bool Success, string Message, S19ResolveDisputeOutcomeDto? Outcome)> ResolveDisputeByAdminAsync(
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
            return (false, "Không tìm thấy hồ sơ tranh chấp cần phân xử.", null);
        }

        if (dispute.Status is "Resolved" or "Rejected")
        {
            return (false, $"Hồ sơ tranh chấp #DSP-{dispute.Id:D4} đã có phán quyết cuối cùng ({dispute.Status}), không thể phân xử lặp lại.", null);
        }

        var allowedStatuses = new[] { "Resolved", "Rejected", "UnderReview" };
        string status = allowedStatuses.Contains(request.DecisionStatus) ? request.DecisionStatus : "Resolved";
        var now = DateTime.UtcNow;

        dispute.Status = status;
        dispute.ResolvedByAdminId = adminUserId;
        dispute.ResolutionNote = string.IsNullOrWhiteSpace(request.ResolutionNote)
            ? "Quản trị viên đã đối chiếu ảnh tình trạng sách lúc giao và lúc trả để ban hành phán quyết."
            : request.ResolutionNote.Trim();
        dispute.ResolvedAt = status == "UnderReview" ? null : now;

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
            if (request.CopyTargetStatus is "Damaged" or "Lost")
            {
                bookCopy.Condition = "Worn";
                bookCopy.IsLendable = false;
            }
            else if (request.CopyTargetStatus == "Available")
            {
                bookCopy.IsLendable = true;
            }
        }

        var outcome = new S19ResolveDisputeOutcomeDto { LoanUnlocked = false };
        decimal compensationToInvoice = status == "Resolved" ? Math.Max(0m, request.CompensationAmount) : 0m;

        // Mở khóa trạng thái hợp đồng mượn khi tranh chấp đã phân xử dứt điểm và KHÔNG có hóa đơn bồi thường chờ thanh toán
        if (status is "Resolved" or "Rejected")
        {
            bool otherOpenExists = await _db.Disputes.AnyAsync(d =>
                d.LoanId == dispute.LoanId &&
                d.Id != dispute.Id &&
                (d.Status == "Open" || d.Status == "UnderReview"));

            if (compensationToInvoice > 0)
            {
                dispute.Loan.FineAmount += compensationToInvoice;
                dispute.Loan.Status = "Disputed"; // Giữ khóa quyền mượn cho đến khi thanh toán xong tại Ví Bảo Lãnh
            }
            else if (!otherOpenExists)
            {
                if (status == "Resolved" || dispute.Loan.ReturnDate.HasValue)
                {
                    dispute.Loan.ReturnDate ??= now;
                    dispute.Loan.Status = "Returned";
                }
                else
                {
                    dispute.Loan.Status = dispute.Loan.DueDate < now ? "Overdue" : "Active";
                }
                outcome.LoanUnlocked = true;
            }
        }

        if (compensationToInvoice > 0)
        {
            var pendingTx = new Transaction
            {
                UserId = borrower.Id,
                LoanId = dispute.LoanId,
                TransactionType = "Fine",
                Amount = compensationToInvoice,
                PaymentGateway = "QrPay",
                PaymentStatus = "Pending",
                Description = $"Bồi thường tranh chấp #DSP-{dispute.Id:D4} — cuốn \"{bookCopy.Book.Title}\"",
                HmacSignature = null,
                CreatedAt = now
            };
            _db.Transactions.Add(pendingTx);
            await _db.SaveChangesAsync();

            string canonical = _paymentService.BuildCanonicalPayload(
                pendingTx.Id,
                pendingTx.UserId,
                pendingTx.TransactionType,
                pendingTx.Amount,
                pendingTx.PaymentGateway,
                pendingTx.LoanId);

            outcome.PendingTransactionId = pendingTx.Id;
            outcome.RawPayload = canonical;
            outcome.ExpectedHmac = _paymentService.ComputeHmacSha512(canonical);
            outcome.PaymentUrl = $"/S17SandboxPayment?txId={pendingTx.Id}";

            _db.Notifications.Add(new Notification
            {
                UserId = borrower.Id,
                Title = $"Yêu cầu thanh toán bồi thường #DSP-{dispute.Id:D4} ({compensationToInvoice:N0} VNĐ)",
                Content = $"Phán quyết tranh chấp cuốn \"{bookCopy.Book.Title}\" phát sinh khoản bồi thường {compensationToInvoice:N0} VNĐ. Quyền mượn sách mới được mở lại ngay sau khi bạn hoàn tất thanh toán tại: {outcome.PaymentUrl}",
                Type = "Dispute",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });
        }
        else if (request.DepositDeductionAmount > 0)
        {
            var deductionTx = new Transaction
            {
                UserId = borrower.Id,
                LoanId = dispute.LoanId,
                TransactionType = "Fine",
                Amount = request.DepositDeductionAmount,
                PaymentGateway = "VirtualWallet",
                PaymentStatus = "Completed",
                Description = $"Khấu trừ ký quỹ tranh chấp #DSP-{dispute.Id:D4} — cuốn \"{bookCopy.Book.Title}\"",
                HmacSignature = null,
                CreatedAt = now
            };
            _db.Transactions.Add(deductionTx);
            await _db.SaveChangesAsync();

            string canonical = _paymentService.BuildCanonicalPayload(
                deductionTx.Id,
                deductionTx.UserId,
                deductionTx.TransactionType,
                deductionTx.Amount,
                deductionTx.PaymentGateway,
                deductionTx.LoanId);
            deductionTx.HmacSignature = _paymentService.ComputeHmacSha512(canonical);
        }

        _db.Notifications.Add(new Notification
        {
            UserId = borrower.Id,
            Title = $"Phán quyết hồ sơ tranh chấp #DSP-{dispute.Id:D4} ({status})",
            Content = $"Quản trị viên đã xử lý tranh chấp cuốn \"{bookCopy.Book.Title}\": {dispute.ResolutionNote}. Điểm uy tín hiện tại: {borrower.TrustScore}.",
            Type = "Dispute",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        if (!string.Equals(dispute.RaisedByUserId, borrower.Id, StringComparison.Ordinal))
        {
            _db.Notifications.Add(new Notification
            {
                UserId = dispute.RaisedByUserId,
                Title = $"Cập nhật kết quả khiếu nại #DSP-{dispute.Id:D4} ({status})",
                Content = $"Hồ sơ khiếu nại cuốn \"{bookCopy.Book.Title}\" đã được phân xử: {dispute.ResolutionNote}.",
                Type = "Dispute",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync();

        string extraInvoiceMsg = outcome.PendingTransactionId.HasValue
            ? $" Đã phát hành hóa đơn bồi thường #{outcome.PendingTransactionId.Value} ({compensationToInvoice:N0} VNĐ) ở Ví Bảo Lãnh — hợp đồng vẫn tạm khóa cho đến khi thanh toán xong."
            : string.Empty;

        return (true,
            $"Đã ban hành phán quyết [{status}] cho hồ sơ #DSP-{dispute.Id:D4}: Trừ {penalty} điểm uy tín của {borrower.FullName} (Còn {borrower.TrustScore} điểm) và cập nhật trạng thái bản sao [{bookCopy.Barcode}].{extraInvoiceMsg}",
            outcome);
    }

        public async Task<(bool Success, string Message)> UpdateDisputeReasonAsync(
            string currentUserId,
            string currentUserRole,
            S19UpdateDisputeRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return (false, "Nội dung lý do khiếu nại không được để trống.");
            }

            if (request.Reason.Trim().Length > 2000)
            {
                return (false, "Nội dung lý do khiếu nại không được vượt quá 2.000 ký tự.");
            }

            var dispute = await _db.Disputes.FirstOrDefaultAsync(d => d.Id == request.DisputeId);
            if (dispute == null)
            {
                return (false, "Không tìm thấy hồ sơ tranh chấp cần chỉnh sửa.");
            }

            if (dispute.RaisedByUserId != currentUserId && currentUserRole != "Admin")
            {
                throw new UnauthorizedAccessException("Bạn không có quyền chỉnh sửa hồ sơ khiếu nại của thành viên khác.");
            }

            if (dispute.Status == "Resolved")
            {
                return (false, "Hồ sơ tranh chấp đã có phán quyết cuối cùng của Trọng tài, không thể sửa đổi lý do.");
            }

            dispute.Reason = request.Reason.Trim();
            await _db.SaveChangesAsync();

            return (true, $"Đã cập nhật nội dung khiếu nại cho hồ sơ #DSP-{dispute.Id:D4}!");
        }

        public async Task<(bool Success, string Message)> DeleteDisputeAsync(
            string currentUserId,
            string currentUserRole,
            int disputeId)
        {
            var dispute = await _db.Disputes
                .Include(d => d.Loan)
                .FirstOrDefaultAsync(d => d.Id == disputeId);

            if (dispute == null)
            {
                return (false, "Không tìm thấy hồ sơ tranh chấp cần rút/xóa.");
            }

            if (dispute.RaisedByUserId != currentUserId && currentUserRole != "Admin")
            {
                throw new UnauthorizedAccessException("Chỉ người gửi khiếu nại hoặc Quản trị viên mới được quyền rút hoặc xóa hồ sơ tranh chấp.");
            }

            if (dispute.Status == "Resolved" && currentUserRole != "Admin")
            {
                return (false, "Hồ sơ đã được Trọng tài phân xử hoàn tất, chỉ Admin mới có thể xóa.");
            }

            if (dispute.Loan != null && dispute.Loan.Status == "Disputed")
            {
                dispute.Loan.Status = dispute.Loan.ReturnDate.HasValue ? "Returned" : "Active";
            }

            _db.Disputes.Remove(dispute);
            await _db.SaveChangesAsync();

            return (true, $"Đã rút và xóa hồ sơ tranh chấp #DSP-{disputeId:D4} khỏi hệ thống!");
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
