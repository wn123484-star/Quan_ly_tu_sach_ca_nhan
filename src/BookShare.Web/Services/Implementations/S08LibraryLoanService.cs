using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S08LibraryLoanService : IS08LibraryLoanService
{
    private readonly BookShareDbContext _db;
    private readonly ILoanFlowCoordinatorService _loanCoordinator;

    public S08LibraryLoanService(BookShareDbContext db, ILoanFlowCoordinatorService loanCoordinator)
    {
        _db = db;
        _loanCoordinator = loanCoordinator;
    }

    public async Task<S08LibraryLoanPageViewModel> GetLibraryLoanFlowAsync(string currentUserId, string currentUserRole)
    {
        var user = await _db.AspNetUsers
            .Include(u => u.Memberships)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        var activeMembership = user?.Memberships
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.MaxConcurrentLoans)
            .FirstOrDefault();

        string tier = activeMembership?.Tier ?? "Free";
        int maxLoans = activeMembership?.MaxConcurrentLoans ?? 2;
        int bonus = user?.LendQuotaBonus ?? 0;

        int activeLoansCount = await _db.Loans
            .CountAsync(l => l.LoanRequest.BorrowerId == currentUserId && l.Status == "Active");

        var availableCopies = await _db.BookCopies
            .AsNoTracking()
            .Include(bc => bc.Book)
            .Include(bc => bc.Library)
            .Where(bc => bc.OwnerType == "System" && bc.IsLendable && bc.Status == "Available")
            .OrderBy(bc => bc.Book.Title)
            .Select(bc => new S08AvailableLibraryCopyDto
            {
                BookCopyId = bc.Id,
                Barcode = bc.Barcode,
                Title = bc.Book.Title,
                Author = bc.Book.Author,
                LibraryName = bc.Library != null ? bc.Library.Name : "Thư viện Đại học CMC",
                ShelfLocation = bc.ShelfLocation ?? "Kệ A1 - Tầng 2",
                LibraryId = bc.LibraryId
            })
            .ToListAsync();

        bool isStaff = string.Equals(currentUserRole, "Librarian", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);

        var requests = string.IsNullOrWhiteSpace(currentUserId)
            ? new List<BookShare.Web.Models.Entities.LoanRequest>()
            : await _db.LoanRequests
                .AsNoTracking()
                .Include(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
                .Include(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Library)
                .Include(lr => lr.Borrower)
                    .ThenInclude(b => b.Memberships)
                .Include(lr => lr.Loan)
                .Where(lr => lr.LoanType == "Library" && (isStaff || lr.BorrowerId == currentUserId))
                .OrderByDescending(lr => lr.RequestedAt)
                .ToListAsync();

        var activeLoanCountsByUser = await _db.Loans
            .AsNoTracking()
            .Where(l => l.Status == "Active")
            .GroupBy(l => l.LoanRequest.BorrowerId)
            .Select(g => new { BorrowerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BorrowerId, x => x.Count);

        var items = requests.Select(lr =>
        {
            var bMembership = lr.Borrower.Memberships
                .Where(m => m.IsActive)
                .OrderByDescending(m => m.MaxConcurrentLoans)
                .FirstOrDefault();
            int bMax = (bMembership?.MaxConcurrentLoans ?? 2) + lr.Borrower.LendQuotaBonus;
            activeLoanCountsByUser.TryGetValue(lr.BorrowerId, out int bActive);

            return new S08LibraryLoanItemDto
            {
                LoanRequestId = lr.Id,
                LoanId = lr.Loan?.Id,
                BookId = lr.BookCopy.BookId,
                BookCopyId = lr.BookCopyId,
                Barcode = lr.BookCopy.Barcode,
                BookTitle = lr.BookCopy.Book.Title,
                BookAuthor = lr.BookCopy.Book.Author,
                CoverImageUrl = lr.BookCopy.Book.CoverImageUrl,
                LibraryName = lr.BookCopy.Library?.Name ?? lr.PickupHub?.Name ?? "Thư viện Đại học CMC",
                ShelfLocation = lr.BookCopy.ShelfLocation ?? "Kệ A1 - Tầng 2",
                BorrowerId = lr.BorrowerId,
                BorrowerName = lr.Borrower.FullName,
                BorrowerTier = bMembership?.Tier ?? "Free",
                BorrowerActiveLoans = bActive,
                BorrowerMaxQuota = bMax,
                DesiredDays = lr.DesiredDays,
                RequestedAt = lr.RequestedAt,
                RequestStatus = lr.Status,
                CopyStatus = lr.BookCopy.Status,
                CopyCondition = lr.BookCopy.Condition,
                LoanStatus = lr.Loan?.Status,
                StartDate = lr.Loan?.StartDate,
                DueDate = lr.Loan?.DueDate,
                ReturnDate = lr.Loan?.ReturnDate,
                RequestNote = lr.RequestNote
            };
        }).ToList();

        return new S08LibraryLoanPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserName = user?.FullName ?? "Độc giả BookShare",
            CurrentUserRole = currentUserRole,
            MembershipTier = tier,
            MaxConcurrentLoans = maxLoans,
            LendQuotaBonus = bonus,
            CurrentActiveLoansCount = activeLoansCount,
            AvailableLibraryCopies = availableCopies,
            LoanItems = items
        };
    }

    public async Task<(bool Success, string Message)> CreateLibraryLoanRequestAsync(
        string currentUserId,
        CreateLibraryLoanRequestDto request)
    {
        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .FirstOrDefaultAsync(bc => bc.Id == request.BookCopyId && bc.OwnerType == "System");

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách Kho chung hợp lệ.");
        }

        var (isEligible, reason) = await _loanCoordinator.ValidateBorrowEligibilityAsync(currentUserId, copy);
        if (!isEligible)
        {
            return (false, reason);
        }

        bool existingPending = await _db.LoanRequests
            .AnyAsync(lr => lr.BookCopyId == copy.Id && lr.BorrowerId == currentUserId && lr.Status == "Pending");
        if (existingPending)
        {
            return (false, "Bạn đã có yêu cầu mượn bản sao này đang chờ Thủ thư duyệt.");
        }

        int days = Math.Clamp(request.DesiredDays, 3, 30);

        var loanReq = new LoanRequest
        {
            BookCopyId = copy.Id,
            BorrowerId = currentUserId,
            ApproverId = null,
            LoanType = "Library",
            Status = "Pending",
            RequestedAt = DateTime.UtcNow,
            DesiredDays = days,
            PickupHubId = copy.LibraryId,
            RequestNote = string.IsNullOrWhiteSpace(request.RequestNote)
                ? $"Đặt mượn Kho chung bản sao [{copy.Barcode}] trong {days} ngày."
                : request.RequestNote.Trim()
        };

        _db.LoanRequests.Add(loanReq);
        await _loanCoordinator.FulfillNotifiedReservationAsync(currentUserId, copy.BookId);
        await _db.SaveChangesAsync();

        return (true, $"Đã tạo yêu cầu mượn Kho chung #{loanReq.Id} cho sách \"{copy.Book.Title}\" ([{copy.Barcode}]). Chờ Thủ thư duyệt!");
    }

    public async Task<(bool Success, string Message)> ApproveOrRejectLibraryLoanAsync(
        string currentUserId,
        ApproveRejectLibraryLoanDto request)
    {
        var loanReq = await _db.LoanRequests
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Book)
            .Include(lr => lr.Borrower)
                .ThenInclude(b => b.Memberships)
            .FirstOrDefaultAsync(lr => lr.Id == request.LoanRequestId && lr.LoanType == "Library");

        if (loanReq == null)
        {
            return (false, "Không tìm thấy phiếu yêu cầu mượn Kho chung.");
        }

        if (loanReq.Status != "Pending")
        {
            return (false, $"Phiếu mượn #{loanReq.Id} đã ở trạng thái '{loanReq.Status}'.");
        }

        if (!request.Approve)
        {
            loanReq.Status = "Rejected";
            loanReq.ApproverId = currentUserId;
            if (loanReq.BookCopy.Status == "Reserved")
            {
                bool hasOtherHold = await _db.LoanRequests.AnyAsync(lr =>
                    lr.BookCopyId == loanReq.BookCopyId && lr.Id != loanReq.Id && lr.Status == "Approved" && (lr.Loan == null || lr.Loan.Status != "Returned"));
                if (!hasOtherHold)
                {
                    loanReq.BookCopy.Status = "Available";
                }
            }
            _db.Notifications.Add(new Notification
            {
                UserId = loanReq.BorrowerId,
                Title = $"Phiếu mượn Kho chung #{loanReq.Id} chưa được duyệt",
                Content = $"Yêu cầu mượn bản sao [{loanReq.BookCopy.Barcode}] - \"{loanReq.BookCopy.Book.Title}\" đã bị từ chối.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            return (true, $"Đã từ chối phiếu mượn #{loanReq.Id} ({loanReq.BookCopy.Book.Title}).");
        }

        var (canApprove, reason) = await _loanCoordinator.ValidateCopyApprovalAsync(loanReq);
        if (!canApprove)
        {
            return (false, reason);
        }

        loanReq.Status = "Approved";
        loanReq.ApproverId = currentUserId;
        loanReq.BookCopy.Status = "Reserved";

        _db.Notifications.Add(new Notification
        {
            UserId = loanReq.BorrowerId,
            Title = $"Phiếu mượn Kho chung #{loanReq.Id} đã được Thủ thư duyệt",
            Content = $"Bản sao [{loanReq.BookCopy.Barcode}] - \"{loanReq.BookCopy.Book.Title}\" đã được giữ chỗ. Mời bạn tới quầy Thư viện nhận sách.",
            Type = "LoanApproved",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return (true, $"Thủ thư đã duyệt phiếu #{loanReq.Id}! Bản sao [{loanReq.BookCopy.Barcode}] đã chuyển sang trạng thái giữ chỗ chờ độc giả tới nhận.");
    }

    public async Task<(bool Success, string Message)> ConfirmHandoverLibraryLoanAsync(
        string currentUserId,
        HandoverLibraryLoanDto request)
    {
        var loanReq = await _db.LoanRequests
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Book)
            .Include(lr => lr.Loan)
            .FirstOrDefaultAsync(lr => lr.Id == request.LoanRequestId && lr.LoanType == "Library");

        if (loanReq == null)
        {
            return (false, "Không tìm thấy phiếu mượn Kho chung.");
        }

        if (loanReq.Status != "Approved")
        {
            return (false, "Phiếu mượn phải được Thủ thư Duyệt (Approved) trước khi xác nhận bàn giao sách.");
        }

        if (loanReq.Loan != null)
        {
            return (false, $"Hợp đồng mượn (Loan #{loanReq.Loan.Id}) đã được khởi tạo trước đó.");
        }

        var now = DateTime.UtcNow;
        var newLoan = new Loan
        {
            LoanRequestId = loanReq.Id,
            StartDate = now,
            DueDate = now.AddDays(loanReq.DesiredDays),
            RenewCount = 0,
            FineAmount = 0,
            Status = "Active"
        };

        loanReq.BookCopy.Status = "Borrowed";
        _db.Loans.Add(newLoan);
        await _db.SaveChangesAsync();

        // Tự động tạo biên bản ảnh lúc giao (Handover) mặc định nếu chưa có
        _db.ConditionReports.Add(new ConditionReport
        {
            LoanId = newLoan.Id,
            ReportType = "Handover",
            ReportedByUserId = currentUserId,
            ConditionGrade = loanReq.BookCopy.Condition,
            Note = $"Biên bản bàn giao tại quầy Thư viện cho bản sao [{loanReq.BookCopy.Barcode}]. Sách nguyên vẹn.",
            ImageUrl = loanReq.BookCopy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
            CreatedAt = now
        });
        await _db.SaveChangesAsync();

        return (true, $"Đã xác nhận giao sách tại Thư viện! Khởi tạo hợp đồng Loan #{newLoan.Id}, trạng thái bản sao [{loanReq.BookCopy.Barcode}] -> 'Borrowed' (Hạn trả: {newLoan.DueDate:dd/MM/yyyy}).");
    }

    public async Task<(bool Success, string Message)> ConfirmReturnLibraryLoanAsync(
        string currentUserId,
        ReturnLibraryLoanDto request)
    {
        var loanReq = await _db.LoanRequests
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Book)
            .Include(lr => lr.Borrower)
            .Include(lr => lr.Loan)
            .FirstOrDefaultAsync(lr => lr.Id == request.LoanRequestId && lr.LoanType == "Library");

        if (loanReq?.Loan == null)
        {
            return (false, "Không tìm thấy hợp đồng mượn sách đang hoạt động để thu hồi.");
        }

        if (loanReq.Loan.Status == "Returned")
        {
            return (false, "Hợp đồng mượn này đã được hoàn trả trước đó.");
        }

        var now = DateTime.UtcNow;
        loanReq.Loan.ReturnDate = now;
        loanReq.Loan.Status = "Returned";

        var allowedCopyConditions = new[] { "New", "Good", "Fair", "Worn" };
        bool isDamaged = string.Equals(request.ReturnCondition, "Damaged", StringComparison.OrdinalIgnoreCase);

        if (isDamaged)
        {
            loanReq.BookCopy.Status = "Damaged";
            loanReq.BookCopy.Condition = "Worn";
            loanReq.BookCopy.IsLendable = false;
            loanReq.Borrower.TrustScore = Math.Clamp(loanReq.Borrower.TrustScore - 10, 0, 200);
        }
        else
        {
            loanReq.BookCopy.Status = "Available";
            if (allowedCopyConditions.Contains(request.ReturnCondition))
            {
                loanReq.BookCopy.Condition = request.ReturnCondition;
            }
            loanReq.Borrower.TrustScore = Math.Clamp(loanReq.Borrower.TrustScore + 2, 0, 200);
        }

        // Lưu biên bản tình trạng lúc trả
        _db.ConditionReports.Add(new ConditionReport
        {
            LoanId = loanReq.Loan.Id,
            ReportType = "Return",
            ReportedByUserId = currentUserId,
            ConditionGrade = isDamaged ? "Damaged" : (allowedCopyConditions.Contains(request.ReturnCondition) ? request.ReturnCondition : "Good"),
            Note = isDamaged
                ? $"Thu hồi tại quầy Thư viện: Phát hiện sách [{loanReq.BookCopy.Barcode}] bị hư hại."
                : $"Thu hồi tại quầy Thư viện: Bản sao [{loanReq.BookCopy.Barcode}] trả về nguyên vẹn ({request.ReturnCondition}).",
            ImageUrl = loanReq.BookCopy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
            CreatedAt = now
        });

        // Nếu có người đang xếp hàng chờ đầu sách này thì thông báo tự động cho vị trí #1
        string queueNotice = string.Empty;
        if (!isDamaged)
        {
            var (notifiedNext, notifiedUserName) = await _loanCoordinator.HandleCopyReturnedAndNotifyQueueAsync(loanReq.BookCopy, now);
            if (notifiedNext)
            {
                queueNotice = $" Đồng thời hệ thống đã tự động thông báo giữ chỗ cho độc giả đứng đầu hàng chờ ({notifiedUserName})!";
            }
        }

        await _db.SaveChangesAsync();

        return (true, $"Đã xác nhận trả sách [{loanReq.BookCopy.Barcode}] thành công!{queueNotice}");
    }

    public async Task<(bool Success, string Message)> CancelLibraryLoanRequestAsync(
        string currentUserId,
        string currentUserRole,
        int loanRequestId)
    {
        var loanReq = await _db.LoanRequests
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Book)
            .Include(lr => lr.Loan)
            .FirstOrDefaultAsync(lr => lr.Id == loanRequestId);

        if (loanReq == null)
        {
            return (false, "Không tìm thấy phiếu yêu cầu mượn cần hủy.");
        }

        bool isPrivileged = currentUserRole == "Librarian" || currentUserRole == "Admin";
        if (loanReq.BorrowerId != currentUserId && !isPrivileged)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền hủy hoặc xóa phiếu yêu cầu mượn của độc giả khác.");
        }

        if (loanReq.Loan != null)
        {
            return (false, "Phiếu mượn này đã phát sinh hợp đồng bàn giao sách thực tế, không thể xóa.");
        }

        if (loanReq.BookCopy.Status == "Reserved")
        {
            loanReq.BookCopy.Status = "Available";
        }

        _db.LoanRequests.Remove(loanReq);
        await _db.SaveChangesAsync();

        return (true, $"Đã hủy và xóa phiếu yêu cầu mượn #{loanRequestId} (\"{loanReq.BookCopy.Book.Title}\") thành công!");
    }
}
