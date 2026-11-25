using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [CORE-QR-HANDOVER] Dịch vụ phát hành mã QR bàn giao/hoàn trả dùng một lần (có thời hạn ngắn) và xử lý quét xác nhận chuyển trạng thái hợp đồng mượn (S08/S09) tức thì.
/// </summary>
/// <remarks>Kiểm tra quyền hai chiều giữa Chủ sách/Thủ thư/Trưởng CLB và Người mượn.</remarks>
public class BookHandoverQrService : IBookHandoverQrService
{
    private const string Prefix = "BOOKSHARE-HANDOVER:v1:";
    private readonly BookShareDbContext _db;
    private readonly ILoanFlowCoordinatorService _loanCoordinator;

    public BookHandoverQrService(BookShareDbContext db, ILoanFlowCoordinatorService loanCoordinator)
    {
        _db = db;
        _loanCoordinator = loanCoordinator;
    }

    public async Task<(bool Success, string Message, CreateHandoverQrResultDto? Data)> CreateHandoverQrAsync(
        string currentUserId,
        string currentUserRole,
        CreateHandoverQrRequestDto request)
    {
        string type = string.Equals(request.HandoverType, "Return", StringComparison.OrdinalIgnoreCase)
            ? "Return"
            : "Handover";

        var req = await _db.LoanRequests
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Book)
            .Include(lr => lr.Loan)
            .Include(lr => lr.Borrower)
            .FirstOrDefaultAsync(lr => lr.Id == request.LoanRequestId);

        if (req == null)
        {
            return (false, "Không tìm thấy phiếu mượn sách tương ứng.", null);
        }

        bool isStaff = currentUserRole is "Librarian" or "Admin";
        bool isCuratorOrAdmin = currentUserRole is "Curator" or "Admin";
        bool canIssue = req.LoanType == "Library"
            ? isStaff
            : (req.BookCopy.OwnerUserId == currentUserId || isCuratorOrAdmin);

        if (!canIssue)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền phát hành mã QR bàn giao hoặc thu hồi cho phiếu mượn này.");
        }

        if (type == "Handover")
        {
            if (req.Status != "Approved" && req.BookCopy.Status != "InTransit")
            {
                return (false, $"Phiếu mượn #{req.Id} phải được duyệt trước khi tạo mã QR bàn giao.", null);
            }
            if (req.Loan != null)
            {
                return (false, $"Phiếu mượn #{req.Id} đã được bàn giao trước đó.", null);
            }
        }
        else
        {
            if (req.Loan == null || req.Loan.Status is not ("Active" or "Overdue"))
            {
                return (false, $"Phiếu mượn #{req.Id} hiện chưa có hợp đồng đang mượn để tạo mã QR trả sách.", null);
            }
        }

        var oldPending = await _db.BookHandoverQrs
            .Where(q => q.LoanRequestId == req.Id && q.HandoverType == type && q.Status == "Pending")
            .ToListAsync();
        foreach (var item in oldPending)
        {
            item.Status = "Cancelled";
        }

        string token = AuthConfigHelper.GenerateBase64UrlToken(32);
        string shortCode = AuthConfigHelper.GenerateShortCode(6);
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(10);

        var qrEntity = new BookHandoverQr
        {
            LoanRequestId = req.Id,
            LoanId = req.Loan?.Id,
            HandoverType = type,
            TokenHash = AuthConfigHelper.ComputeSha256Hex(token),
            ShortCodeHash = AuthConfigHelper.ComputeSha256Hex(shortCode),
            CreatedByUserId = currentUserId,
            Status = "Pending",
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        _db.BookHandoverQrs.Add(qrEntity);
        await _db.SaveChangesAsync();

        var result = new CreateHandoverQrResultDto
        {
            QrId = qrEntity.Id,
            LoanRequestId = req.Id,
            HandoverType = type,
            Token = token,
            QrPayload = $"{Prefix}{token}",
            ShortCode = shortCode,
            ExpiresInSeconds = 600,
            ExpiresAt = expiresAt
        };

        string label = type == "Handover" ? "bàn giao nhận sách" : "xác nhận hoàn trả sách";
        return (true, $"Đã tạo mã QR {label} cho bản sao [{req.BookCopy.Barcode}] (hiệu lực 10 phút). Mã dự phòng: {shortCode}.", result);
    }

    public async Task<(bool Success, string Message, string ActionPerformed)> ConfirmHandoverQrAsync(
        string currentUserId,
        string currentUserRole,
        ConfirmHandoverQrRequestDto request)
    {
        var now = DateTime.UtcNow;
        string rawToken = ExtractToken(request.QrPayload, request.Token);
        string? shortCode = string.IsNullOrWhiteSpace(request.ShortCode)
            ? null
            : request.ShortCode.Trim().ToUpperInvariant();

        BookHandoverQr? qr = null;
        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            string tokenHash = AuthConfigHelper.ComputeSha256Hex(rawToken);
            qr = await _db.BookHandoverQrs
                .Include(q => q.LoanRequest)
                    .ThenInclude(lr => lr.BookCopy)
                        .ThenInclude(bc => bc.Book)
                .Include(q => q.LoanRequest)
                    .ThenInclude(lr => lr.BookCopy)
                        .ThenInclude(bc => bc.OwnerUser)
                .Include(q => q.LoanRequest)
                    .ThenInclude(lr => lr.Borrower)
                .Include(q => q.LoanRequest)
                    .ThenInclude(lr => lr.Loan)
                .FirstOrDefaultAsync(q => q.TokenHash == tokenHash);
        }
        else if (!string.IsNullOrWhiteSpace(shortCode) && request.LoanRequestId.HasValue)
        {
            string shortHash = AuthConfigHelper.ComputeSha256Hex(shortCode);
            qr = await _db.BookHandoverQrs
                .Include(q => q.LoanRequest)
                    .ThenInclude(lr => lr.BookCopy)
                        .ThenInclude(bc => bc.Book)
                .Include(q => q.LoanRequest)
                    .ThenInclude(lr => lr.BookCopy)
                        .ThenInclude(bc => bc.OwnerUser)
                .Include(q => q.LoanRequest)
                    .ThenInclude(lr => lr.Borrower)
                .Include(q => q.LoanRequest)
                    .ThenInclude(lr => lr.Loan)
                .Where(q => q.LoanRequestId == request.LoanRequestId.Value && q.ShortCodeHash == shortHash)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefaultAsync();
        }

        if (qr == null)
        {
            return (false, "Mã QR hoặc mã xác nhận không hợp lệ.", string.Empty);
        }

        if (qr.Status == "Consumed")
        {
            return (false, "Mã QR này đã được sử dụng trước đó và không thể dùng lại.", string.Empty);
        }

        if (qr.Status != "Pending" || qr.ExpiresAt <= now)
        {
            if (qr.Status == "Pending" && qr.ExpiresAt <= now)
            {
                qr.Status = "Expired";
                await _db.SaveChangesAsync();
            }
            return (false, "Mã QR bàn giao đã hết hạn (quá 10 phút) hoặc đã bị hủy. Vui lòng tạo mã mới.", string.Empty);
        }

        var req = qr.LoanRequest;
        var copy = req.BookCopy;

        bool isAllowedConfirmer = qr.HandoverType == "Handover"
            ? (req.BorrowerId == currentUserId || currentUserRole == "Admin")
            : (req.BorrowerId == currentUserId
               || copy.OwnerUserId == currentUserId
               || currentUserRole is "Librarian" or "Curator" or "Admin");

        if (!isAllowedConfirmer)
        {
            throw new UnauthorizedAccessException("Bạn không phải bên tham gia hợp đồng mượn trả này nên không thể xác nhận mã QR.");
        }

        qr.Status = "Consumed";
        qr.ConsumedByUserId = currentUserId;
        qr.ConsumedAt = now;

        if (qr.HandoverType == "Handover")
        {
            if (req.Loan != null)
            {
                await _db.SaveChangesAsync();
                return (false, "Phiếu mượn này đã được kích hoạt nhận sách trước đó.", string.Empty);
            }

            req.Status = "Approved";
            copy.Status = "Borrowed";

            var newLoan = new Loan
            {
                LoanRequestId = req.Id,
                StartDate = now,
                DueDate = now.AddDays(req.DesiredDays > 0 ? req.DesiredDays : 14),
                RenewCount = 0,
                FineAmount = 0m,
                Status = "Active"
            };
            _db.Loans.Add(newLoan);
            await _loanCoordinator.FulfillNotifiedReservationAsync(req.BorrowerId, copy.BookId);
            await _db.SaveChangesAsync();

            qr.LoanId = newLoan.Id;

            if (req.LoanType == "P2P" && copy.OwnerUser != null)
            {
                copy.OwnerUser.LendQuotaBonus += 1;
                copy.OwnerUser.TrustScore = Math.Clamp(copy.OwnerUser.TrustScore + 3, 0, 200);
            }

            _db.ConditionReports.Add(new ConditionReport
            {
                LoanId = newLoan.Id,
                ReportType = "Handover",
                ReportedByUserId = currentUserId,
                ConditionGrade = copy.Condition,
                Note = $"Xác nhận nhận sách thực tế qua mã QR cho bản sao [{copy.Barcode}].",
                ImageUrl = copy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
                CreatedAt = now
            });

            _db.Notifications.Add(new Notification
            {
                UserId = req.BorrowerId,
                Title = $"Xác nhận nhận sách [{copy.Barcode}] thành công",
                Content = $"Bạn đã nhận cuốn \"{copy.Book.Title}\" qua mã QR bàn giao (Hạn trả: {newLoan.DueDate:dd/MM/yyyy}).",
                Type = "LoanApproved",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            if (!string.IsNullOrWhiteSpace(copy.OwnerUserId) && copy.OwnerUserId != req.BorrowerId)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = copy.OwnerUserId,
                    Title = $"Người mượn đã quét QR nhận sách [{copy.Barcode}]",
                    Content = $"{req.Borrower.FullName} đã xác nhận nhận sách qua mã QR bảo mật.",
                    Type = "LoanApproved",
                    IsRead = false,
                    EmailSentViaResend = false,
                    CreatedAt = now
                });
            }

            await _db.SaveChangesAsync();
            return (true, $"Xác nhận bàn giao sách [{copy.Barcode}] qua QR thành công! Hợp đồng #{newLoan.Id} đã bắt đầu (Hạn trả: {newLoan.DueDate:dd/MM/yyyy}).", "Handover");
        }

        // Luồng xác nhận trả sách (Return)
        var loan = req.Loan;
        if (loan == null || loan.Status == "Returned")
        {
            await _db.SaveChangesAsync();
            return (false, "Hợp đồng mượn này đã hoàn trả trước đó.", string.Empty);
        }

        string cond = new[] { "Good", "Fair", "Damaged", "Lost" }.Contains(request.ReturnCondition)
            ? request.ReturnCondition
            : "Good";

        loan.Status = "Returned";
        loan.ReturnDate = now;
        qr.LoanId = loan.Id;

        if (cond is "Damaged" or "Lost")
        {
            copy.Status = cond;
            copy.Condition = "Worn";
            copy.IsLendable = false;
        }
        else
        {
            copy.Condition = cond;
            copy.IsLendable = true;
            req.Borrower.TrustScore = Math.Clamp(req.Borrower.TrustScore + 2, 0, 200);
            await _loanCoordinator.HandleCopyReturnedAndNotifyQueueAsync(copy, now);
        }

        _db.ConditionReports.Add(new ConditionReport
        {
            LoanId = loan.Id,
            ReportType = "Return",
            ReportedByUserId = currentUserId,
            ConditionGrade = cond,
            Note = $"Xác nhận hoàn trả sách qua mã QR cho bản sao [{copy.Barcode}] (Tình trạng: {cond}).",
            ImageUrl = copy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
            CreatedAt = now
        });

        _db.Notifications.Add(new Notification
        {
            UserId = req.BorrowerId,
            Title = $"Đã xác nhận trả sách [{copy.Barcode}] qua QR",
            Content = $"Cảm ơn bạn đã hoàn trả cuốn \"{copy.Book.Title}\" (Tình trạng: {cond}).",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();
        return (true, $"Hoàn trả sách [{copy.Barcode}] qua QR thành công! Hợp đồng #{loan.Id} đã được đóng.", "Return");
    }

    private static string ExtractToken(string? qrPayload, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token.Trim();
        }
        if (string.IsNullOrWhiteSpace(qrPayload))
        {
            return string.Empty;
        }
        var trimmed = qrPayload.Trim();
        return trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[Prefix.Length..].Trim()
            : trimmed;
    }
}
