using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S08LibraryLoanService : IS08LibraryLoanService
{
    private readonly BookShareDbContext _db;

    public S08LibraryLoanService(BookShareDbContext db)
    {
        _db = db;
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

        var requests = await _db.LoanRequests
            .AsNoTracking()
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Book)
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Library)
            .Include(lr => lr.Borrower)
                .ThenInclude(b => b.Memberships)
            .Include(lr => lr.Loan)
            .Where(lr => lr.LoanType == "Library")
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
            CurrentUserName = user?.FullName ?? "Nguyễn Hùng (BIT250157)",
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

        if (copy.Status != "Available")
        {
            return (false, $"Bản sao [{copy.Barcode}] hiện đang ở trạng thái '{copy.Status}', không thể tạo phiếu mượn trực tiếp.");
        }

        var user = await _db.AspNetUsers
            .Include(u => u.Memberships)
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        int maxQuota = (user?.Memberships.Where(m => m.IsActive).Select(m => m.MaxConcurrentLoans).DefaultIfEmpty(2).Max() ?? 2)
                       + (user?.LendQuotaBonus ?? 0);

        int currentActive = await _db.Loans
            .CountAsync(l => l.LoanRequest.BorrowerId == currentUserId && l.Status == "Active");

        if (currentActive >= maxQuota)
        {
            return (false, $"Bạn đang mượn {currentActive}/{maxQuota} cuốn (đạt trần hạn mức gói thành viên). Vui lòng trả sách hoặc nâng cấp gói Premium (S16).");
        }

        int days = Math.Clamp(request.DesiredDays, 3, 30);

        var loanReq = new LoanRequest
        {
            BookCopyId = copy.Id,
            BorrowerId = currentUserId,
            ApproverId = null, // Gán khi Thủ thư bấm Duyệt
            LoanType = "Library",
            Status = "Pending",
            RequestedAt = DateTime.UtcNow,
            DesiredDays = days,
            PickupHubId = copy.LibraryId ?? 1,
            RequestNote = string.IsNullOrWhiteSpace(request.RequestNote)
                ? $"Đặt mượn Kho chung bản sao [{copy.Barcode}] trong {days} ngày."
                : request.RequestNote.Trim()
        };

        _db.LoanRequests.Add(loanReq);
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
                loanReq.BookCopy.Status = "Available";
            }
            await _db.SaveChangesAsync();
            return (true, $"Đã từ chối phiếu mượn #{loanReq.Id} ({loanReq.BookCopy.Book.Title}).");
        }

        // Kiểm tra hạn mức gói thành viên của người mượn trước khi duyệt
        int maxQuota = (loanReq.Borrower.Memberships.Where(m => m.IsActive).Select(m => m.MaxConcurrentLoans).DefaultIfEmpty(2).Max())
                       + loanReq.Borrower.LendQuotaBonus;
        int activeCount = await _db.Loans
            .CountAsync(l => l.LoanRequest.BorrowerId == loanReq.BorrowerId && l.Status == "Active");

        if (activeCount >= maxQuota)
        {
            return (false, $"Độc giả {loanReq.Borrower.FullName} đang mượn {activeCount}/{maxQuota} cuốn (vượt hạn mức gói). Không thể duyệt thêm!");
        }

        loanReq.Status = "Approved";
        loanReq.ApproverId = string.IsNullOrWhiteSpace(currentUserId) ? "u-lib-01" : currentUserId;
        loanReq.BookCopy.Status = "Reserved";

        _db.Notifications.Add(new Notification
        {
            UserId = loanReq.BorrowerId,
            Title = $"Phiếu mượn Kho chung #{loanReq.Id} đã được Thủ thư duyệt",
            Content = $"Bản sao [{loanReq.BookCopy.Barcode}] - \"{loanReq.BookCopy.Book.Title}\" đã được giữ chỗ (Reserved). Mời bạn tới quầy Thư viện nhận sách.",
            Type = "LoanApproved",
            IsRead = false,
            EmailSentViaResend = true,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return (true, $"Thủ thư đã duyệt phiếu #{loanReq.Id}! Bản sao [{loanReq.BookCopy.Barcode}] đã chuyển sang trạng thái 'Reserved' chờ độc giả tới nhận.");
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
            ImageUrl = loanReq.BookCopy.Book.CoverImageUrl ?? "https://images.unsplash.com/photo-1544947950-fa07a98d237f?w=500",
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
            loanReq.Borrower.TrustScore = Math.Max(0, loanReq.Borrower.TrustScore - 10);
        }
        else
        {
            loanReq.BookCopy.Status = "Available";
            if (allowedCopyConditions.Contains(request.ReturnCondition))
            {
                loanReq.BookCopy.Condition = request.ReturnCondition;
            }
            loanReq.Borrower.TrustScore += 2;
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
            ImageUrl = loanReq.BookCopy.Book.CoverImageUrl ?? "https://images.unsplash.com/photo-1544716278-ca5e3f4abd8c?w=500",
            CreatedAt = now
        });

        // Nếu có người đang xếp hàng chờ (Reservations) đầu sách này thì thông báo tự động cho vị trí #1
        string queueNotice = string.Empty;
        if (!isDamaged)
        {
            var topWaiting = await _db.Reservations
                .Include(r => r.User)
                .Where(r => r.BookId == loanReq.BookCopy.BookId && r.Status == "Waiting")
                .OrderBy(r => r.QueuePosition)
                .FirstOrDefaultAsync();

            if (topWaiting != null)
            {
                topWaiting.Status = "Notified";
                topWaiting.NotifiedAt = now;
                topWaiting.ExpiresAt = now.AddHours(48);

                _db.Notifications.Add(new Notification
                {
                    UserId = topWaiting.UserId,
                    Title = $"[Hàng chờ S11] Sách \"{loanReq.BookCopy.Book.Title}\" đã có bản sao trả về kho!",
                    Content = $"Bạn đang đứng đầu hàng chờ (#{topWaiting.QueuePosition}). Bản sao [{loanReq.BookCopy.Barcode}] vừa được trả về kho. Vui lòng xác nhận nhận sách trong 48h.",
                    Type = "QueueAvailable",
                    IsRead = false,
                    EmailSentViaResend = true,
                    CreatedAt = now
                });

                queueNotice = $" Đồng thời hệ thống đã tự động thông báo cho độc giả đứng đầu hàng chờ S11 ({topWaiting.User.FullName})!";
            }
        }

        await _db.SaveChangesAsync();

        return (true, $"Đã xác nhận trả sách [{loanReq.BookCopy.Barcode}] thành công! Trạng thái kho: '{loanReq.BookCopy.Status}'.{queueNotice}");
    }
}
