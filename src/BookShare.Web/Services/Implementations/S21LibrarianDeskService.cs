using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S21LibrarianDeskService : IS21LibrarianDeskService
{
    private readonly BookShareDbContext _db;
    private readonly ILoanFlowCoordinatorService _loanCoordinator;
    private const decimal DailyFineRateVnd = 5000m;

    public S21LibrarianDeskService(BookShareDbContext db, ILoanFlowCoordinatorService loanCoordinator)
    {
        _db = db;
        _loanCoordinator = loanCoordinator;
    }

    public async Task<S21LibrarianDeskPageViewModel> GetLibrarianDeskPageAsync(
        string currentUserId,
        string currentUserRole,
        string? loanKeyword = null,
        string loanChannel = "all",
        string loanStatus = "all",
        int loanPage = 1,
        string? copyKeyword = null,
        string copyStatus = "all",
        int? copyLibraryId = null,
        int copyPage = 1)
    {
        if (!string.Equals(currentUserRole, "Librarian", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return new S21LibrarianDeskPageViewModel
            {
                CurrentUserId = currentUserId,
                CurrentUserRole = currentUserRole
            };
        }

        var now = DateTime.UtcNow;
        var nowOffset = DateTimeOffset.UtcNow;

        var borrowers = await _db.AspNetUsers
            .AsNoTracking()
            .Where(u => !u.LockoutEnd.HasValue || u.LockoutEnd.Value <= nowOffset)
            .OrderBy(u => u.FullName)
            .Select(u => new S21BorrowerOptionDto
            {
                UserId = u.Id,
                DisplayName = $"{u.FullName} ({u.Email}) • Uy tín {u.TrustScore}"
            })
            .ToListAsync();

        var books = await _db.Books
            .AsNoTracking()
            .OrderBy(b => b.Title)
            .Select(b => new S21BookOptionDto
            {
                BookId = b.Id,
                Title = b.Title,
                ISBN = b.Isbn
            })
            .ToListAsync();

        var libraries = await _db.Libraries
            .AsNoTracking()
            .OrderBy(l => l.Id)
            .Select(l => new S21LibraryOptionDto
            {
                LibraryId = l.Id,
                Name = l.Name
            })
            .ToListAsync();

        var availableLendable = await _db.BookCopies
            .AsNoTracking()
            .Include(bc => bc.Book)
            .Include(bc => bc.Library)
            .Where(bc => bc.OwnerType == "System" && bc.Status == "Available" && bc.IsLendable)
            .OrderBy(bc => bc.Book.Title)
            .ThenBy(bc => bc.Barcode)
            .Select(bc => new S21AvailableCopyOptionDto
            {
                BookCopyId = bc.Id,
                Barcode = bc.Barcode,
                BookTitle = bc.Book.Title,
                OwnerType = bc.OwnerType,
                LocationOrOwner = bc.Library != null ? bc.Library.Name : "Kho Thư viện"
            })
            .ToListAsync();

        // 1. Điều phối TẤT CẢ phiếu mượn (Kho Thư viện + Cộng đồng P2P) với lọc & phân trang
        var allRequestsRaw = await _db.LoanRequests
            .AsNoTracking()
            .Include(lr => lr.BookCopy).ThenInclude(bc => bc.Book)
            .Include(lr => lr.BookCopy).ThenInclude(bc => bc.Library)
            .Include(lr => lr.BookCopy).ThenInclude(bc => bc.OwnerUser)
            .Include(lr => lr.Borrower)
            .Include(lr => lr.Loan)
            .OrderByDescending(lr => lr.Status == "Pending")
            .ThenByDescending(lr => lr.Status == "Approved" && lr.Loan == null)
            .ThenByDescending(lr => lr.RequestedAt)
            .ToListAsync();

        int pendingLoansCount = allRequestsRaw.Count(r => r.Status == "Pending");
        int activeLoansCount = allRequestsRaw.Count(r => r.Loan != null && (r.Loan.Status == "Active" || r.Loan.Status == "Overdue"));

        IEnumerable<LoanRequest> filteredReqs = allRequestsRaw;
        if (loanChannel is "Library" or "P2P")
        {
            filteredReqs = filteredReqs.Where(r => r.LoanType == loanChannel);
        }
        if (!string.IsNullOrWhiteSpace(loanStatus) && loanStatus != "all")
        {
            filteredReqs = loanStatus switch
            {
                "Pending" => filteredReqs.Where(r => r.Status == "Pending"),
                "Approved" => filteredReqs.Where(r => r.Status == "Approved" && r.Loan == null),
                "Active" => filteredReqs.Where(r => r.Loan != null && r.Loan.Status == "Active" && r.Loan.DueDate >= now),
                "Overdue" => filteredReqs.Where(r => r.Loan != null && (r.Loan.Status == "Overdue" || (r.Loan.Status == "Active" && r.Loan.DueDate < now))),
                "Returned" => filteredReqs.Where(r => r.Loan != null && r.Loan.Status == "Returned"),
                "Rejected" => filteredReqs.Where(r => r.Status is "Rejected" or "Cancelled" or "Expired"),
                _ => filteredReqs
            };
        }
        if (!string.IsNullOrWhiteSpace(loanKeyword))
        {
            var kw = loanKeyword.Trim();
            filteredReqs = filteredReqs.Where(r =>
                r.BookCopy.Barcode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                r.BookCopy.Book.Title.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                r.Borrower.FullName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (r.Borrower.Email?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var filteredReqList = filteredReqs.ToList();
        int loanPageSize = 10;
        int loanTotalPages = Math.Max(1, (int)Math.Ceiling(filteredReqList.Count / (double)loanPageSize));
        int safeLoanPage = Math.Clamp(loanPage, 1, loanTotalPages);

        var unifiedLoans = filteredReqList
            .Skip((safeLoanPage - 1) * loanPageSize)
            .Take(loanPageSize)
            .Select(lr => new S21UnifiedLoanItemDto
            {
                LoanRequestId = lr.Id,
                LoanId = lr.Loan?.Id,
                BookCopyId = lr.BookCopyId,
                Barcode = lr.BookCopy.Barcode,
                BookTitle = lr.BookCopy.Book.Title,
                LoanType = lr.LoanType,
                SourceDisplay = lr.LoanType == "Library"
                    ? (lr.BookCopy.Library?.Name ?? "Kho chung Thư viện")
                    : $"Chủ sách: {lr.BookCopy.OwnerUser?.FullName ?? "Cộng đồng"}",
                BorrowerId = lr.BorrowerId,
                BorrowerName = lr.Borrower.FullName,
                BorrowerEmail = lr.Borrower.Email ?? string.Empty,
                BorrowerTrustScore = lr.Borrower.TrustScore,
                RequestStatus = lr.Status,
                CopyStatus = lr.BookCopy.Status,
                LoanStatus = lr.Loan != null && lr.Loan.Status == "Active" && lr.Loan.DueDate < now
                    ? "Overdue"
                    : lr.Loan?.Status,
                DesiredDays = lr.DesiredDays,
                RequestedAt = lr.RequestedAt,
                DueDate = lr.Loan?.DueDate,
                ReturnDate = lr.Loan?.ReturnDate,
                FineAmount = lr.Loan?.FineAmount ?? 0m,
                RequestNote = lr.RequestNote
            })
            .ToList();

        // 2. Danh mục Bản sao Kho chung (có bộ lọc & phân trang)
        var systemCopiesAll = await _db.BookCopies
            .AsNoTracking()
            .Include(bc => bc.Book)
            .Include(bc => bc.Library)
            .Include(bc => bc.LoanRequests)
                .ThenInclude(lr => lr.Borrower)
            .Include(bc => bc.LoanRequests)
                .ThenInclude(lr => lr.Loan)
            .Where(bc => bc.OwnerType == "System")
            .OrderByDescending(bc => bc.Status == "Borrowed" || bc.Status == "Reserved" || bc.Status == "Damaged")
            .ThenBy(bc => bc.Barcode)
            .ToListAsync();

        var allCopyDtos = systemCopiesAll.Select(bc =>
        {
            var activeReq = bc.LoanRequests
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefault(r => r.Loan != null && (r.Loan.Status == "Active" || r.Loan.Status == "Overdue"));

            var pendingReq = bc.LoanRequests
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefault(r => r.Loan == null && (r.Status == "Pending" || r.Status == "Approved"));

            return new S21SystemCopyDeskItemDto
            {
                BookCopyId = bc.Id,
                Barcode = bc.Barcode,
                BookTitle = bc.Book.Title,
                ISBN = bc.Book.Isbn,
                LibraryName = bc.Library != null ? bc.Library.Name : "Thư viện Trung tâm ĐH CMC",
                ShelfLocation = bc.ShelfLocation ?? "Kệ CNTT-A1",
                Condition = bc.Condition,
                Status = bc.Status,
                ActiveLoanId = activeReq?.Loan?.Id,
                PendingRequestId = pendingReq?.Id,
                CurrentBorrowerName = activeReq?.Borrower.FullName ?? pendingReq?.Borrower.FullName
            };
        }).ToList();

        var sampleQuickBarcodes = allCopyDtos
            .OrderBy(c => c.Status == "Available" ? 0 : 1)
            .ThenBy(c => c.Barcode)
            .Take(4)
            .ToList();

        IEnumerable<BookCopy> filteredCopyEntities = systemCopiesAll;
        if (!string.IsNullOrWhiteSpace(copyStatus) && copyStatus != "all")
        {
            filteredCopyEntities = filteredCopyEntities.Where(c => c.Status == copyStatus);
        }
        if (copyLibraryId.HasValue && copyLibraryId.Value > 0)
        {
            filteredCopyEntities = filteredCopyEntities.Where(c => c.LibraryId == copyLibraryId.Value);
        }
        if (!string.IsNullOrWhiteSpace(copyKeyword))
        {
            var ckw = copyKeyword.Trim();
            filteredCopyEntities = filteredCopyEntities.Where(c =>
                c.Barcode.Contains(ckw, StringComparison.OrdinalIgnoreCase) ||
                c.Book.Title.Contains(ckw, StringComparison.OrdinalIgnoreCase) ||
                c.Book.Isbn.Contains(ckw, StringComparison.OrdinalIgnoreCase) ||
                (c.ShelfLocation?.Contains(ckw, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var filteredCopyIds = filteredCopyEntities.Select(c => c.Id).ToHashSet();
        var filteredCopyDtos = allCopyDtos.Where(c => filteredCopyIds.Contains(c.BookCopyId)).ToList();
        int copyPageSize = 20;
        int copyTotalPages = Math.Max(1, (int)Math.Ceiling(filteredCopyDtos.Count / (double)copyPageSize));
        int safeCopyPage = Math.Clamp(copyPage, 1, copyTotalPages);
        var pagedCopies = filteredCopyDtos
            .Skip((safeCopyPage - 1) * copyPageSize)
            .Take(copyPageSize)
            .ToList();

        var overdueLoansRaw = await _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.Borrower)
            .Where(l => l.Status == "Overdue" || (l.Status == "Active" && l.DueDate < now))
            .OrderBy(l => l.DueDate)
            .ToListAsync();

        var overdueDtos = overdueLoansRaw.Select(l =>
        {
            int overdueDays = Math.Max(1, (int)Math.Ceiling((now - l.DueDate).TotalDays));
            decimal calcFine = Math.Max(l.FineAmount, overdueDays * DailyFineRateVnd);
            return new S21OverdueReaderDto
            {
                LoanId = l.Id,
                Barcode = l.LoanRequest.BookCopy.Barcode,
                BookTitle = l.LoanRequest.BookCopy.Book.Title,
                BorrowerId = l.LoanRequest.BorrowerId,
                BorrowerName = l.LoanRequest.Borrower.FullName,
                BorrowerEmail = l.LoanRequest.Borrower.Email ?? string.Empty,
                BorrowerTrustScore = l.LoanRequest.Borrower.TrustScore,
                DueDate = l.DueDate,
                OverdueDays = overdueDays,
                EstimatedFine = calcFine
            };
        }).ToList();

        return new S21LibrarianDeskPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserRole = currentUserRole,
            TotalSystemCopies = allCopyDtos.Count,
            AvailableSystemCopies = allCopyDtos.Count(c => c.Status == "Available"),
            BorrowedSystemCopies = allCopyDtos.Count(c => c.Status == "Borrowed" || c.Status == "Reserved"),
            DamagedOrLostCopies = allCopyDtos.Count(c => c.Status == "Damaged" || c.Status == "Lost"),
            OverdueLoansCount = overdueDtos.Count,
            PendingLoansCount = pendingLoansCount,
            ActiveLoansCount = activeLoansCount,
            LoanKeyword = loanKeyword,
            LoanChannelFilter = loanChannel,
            LoanStatusFilter = loanStatus,
            LoanPage = safeLoanPage,
            LoanPageSize = loanPageSize,
            TotalFilteredLoans = filteredReqList.Count,
            CopyKeyword = copyKeyword,
            CopyStatusFilter = copyStatus,
            CopyLibraryFilter = copyLibraryId,
            CopyPage = safeCopyPage,
            CopyPageSize = copyPageSize,
            TotalFilteredCopies = filteredCopyDtos.Count,
            Books = books,
            Libraries = libraries,
            Borrowers = borrowers,
            AvailableLendableCopies = availableLendable,
            UnifiedLoans = unifiedLoans,
            SystemCopies = pagedCopies,
            SampleQuickBarcodes = sampleQuickBarcodes,
            OverdueLoans = overdueDtos
        };
    }

    public async Task<(bool Success, string Message)> ProcessUnifiedLoanActionAsync(
        string staffUserId,
        string staffUserRole,
        S21ProcessLoanActionDto request)
    {
        if (staffUserRole is not ("Librarian" or "Admin"))
        {
            throw new UnauthorizedAccessException("Chỉ Thủ thư hoặc Quản trị viên mới có quyền điều phối yêu cầu mượn sách tại Quầy Thủ thư.");
        }

        var loanReq = await _db.LoanRequests
            .Include(lr => lr.BookCopy).ThenInclude(bc => bc.Book)
            .Include(lr => lr.BookCopy).ThenInclude(bc => bc.OwnerUser)
            .Include(lr => lr.Borrower)
            .Include(lr => lr.Loan)
            .FirstOrDefaultAsync(lr => lr.Id == request.LoanRequestId);

        if (loanReq == null)
        {
            return (false, "Không tìm thấy phiếu yêu cầu mượn sách cần xử lý.");
        }

        var now = DateTime.UtcNow;
        var copy = loanReq.BookCopy;
        string action = (request.Action ?? string.Empty).Trim();

        if (action.Equals("Approve", StringComparison.OrdinalIgnoreCase))
        {
            if (loanReq.Status != "Pending")
            {
                return (false, $"Phiếu #{loanReq.Id} hiện đang ở trạng thái '{loanReq.Status}', không thể duyệt lại.");
            }

            var (canApprove, reason) = await _loanCoordinator.ValidateCopyApprovalAsync(loanReq);
            if (!canApprove)
            {
                return (false, reason);
            }

            loanReq.Status = "Approved";
            loanReq.ApproverId = staffUserId;
            copy.Status = "Reserved";

            _db.Notifications.Add(new Notification
            {
                UserId = loanReq.BorrowerId,
                Title = $"Phiếu mượn #{loanReq.Id} đã được Thủ thư phê duyệt",
                Content = $"Bản sao [{copy.Barcode}] - \"{copy.Book.Title}\" đã được giữ chỗ. Mời bạn tới quầy hoặc điểm trạm để nhận sách.",
                Type = "LoanApproved",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            return (true, $"Đã duyệt phiếu mượn #{loanReq.Id} (\"{copy.Book.Title}\" [{copy.Barcode}]) cho độc giả {loanReq.Borrower.FullName}!");
        }

        if (action.Equals("Reject", StringComparison.OrdinalIgnoreCase))
        {
            if (loanReq.Loan != null)
            {
                return (false, $"Phiếu #{loanReq.Id} đã bàn giao sách thực tế, không thể từ chối.");
            }
            if (loanReq.Status is "Rejected" or "Cancelled" or "Expired")
            {
                return (false, $"Phiếu #{loanReq.Id} đã ở trạng thái '{loanReq.Status}', không cần từ chối lại.");
            }

            loanReq.Status = "Rejected";
            loanReq.ApproverId = staffUserId;
            if (copy.Status == "Reserved")
            {
                bool otherHold = await _db.LoanRequests.AnyAsync(r =>
                    r.BookCopyId == copy.Id && r.Id != loanReq.Id && r.Status == "Approved" && (r.Loan == null || r.Loan.Status != "Returned"));
                if (!otherHold)
                {
                    copy.Status = "Available";
                }
            }

            _db.Notifications.Add(new Notification
            {
                UserId = loanReq.BorrowerId,
                Title = $"Phiếu mượn #{loanReq.Id} chưa được chấp thuận",
                Content = string.IsNullOrWhiteSpace(request.Note)
                    ? $"Yêu cầu mượn sách \"{copy.Book.Title}\" [{copy.Barcode}] đã bị từ chối tại Quầy Thủ thư."
                    : $"Yêu cầu mượn sách \"{copy.Book.Title}\" [{copy.Barcode}] chưa được duyệt. Lý do: {request.Note.Trim()}",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            return (true, $"Đã từ chối phiếu mượn #{loanReq.Id} (\"{copy.Book.Title}\").");
        }

        if (action.Equals("Handover", StringComparison.OrdinalIgnoreCase))
        {
            if (loanReq.Loan != null)
            {
                return (false, $"Phiếu #{loanReq.Id} đã được bàn giao và kích hoạt hợp đồng #{loanReq.Loan.Id} trước đó.");
            }
            if (loanReq.Status is "Rejected" or "Cancelled" or "Expired")
            {
                return (false, $"Phiếu #{loanReq.Id} đang ở trạng thái '{loanReq.Status}', không thể bàn giao.");
            }

            if (loanReq.Status == "Pending")
            {
                var (canApprove, reason) = await _loanCoordinator.ValidateCopyApprovalAsync(loanReq);
                if (!canApprove)
                {
                    return (false, reason);
                }
                loanReq.Status = "Approved";
                loanReq.ApproverId = staffUserId;
            }

            int days = Math.Clamp(loanReq.DesiredDays > 0 ? loanReq.DesiredDays : 14, 3, 45);
            var newLoan = new Loan
            {
                LoanRequestId = loanReq.Id,
                StartDate = now,
                DueDate = now.AddDays(days),
                RenewCount = 0,
                FineAmount = 0m,
                Status = "Active"
            };
            copy.Status = "Borrowed";
            _db.Loans.Add(newLoan);
            await _loanCoordinator.FulfillNotifiedReservationAsync(loanReq.BorrowerId, copy.BookId);

            if (loanReq.LoanType == "P2P" && copy.OwnerUser != null)
            {
                copy.OwnerUser.LendQuotaBonus += 1;
                copy.OwnerUser.TrustScore = Math.Clamp(copy.OwnerUser.TrustScore + 3, 0, 200);
            }

            await _db.SaveChangesAsync();

            _db.ConditionReports.Add(new ConditionReport
            {
                LoanId = newLoan.Id,
                ReportType = "Handover",
                ReportedByUserId = staffUserId,
                ConditionGrade = copy.Condition,
                Note = $"Thủ thư xác nhận bàn giao sách [{copy.Barcode}] cho {loanReq.Borrower.FullName} tại Quầy nghiệp vụ.",
                ImageUrl = copy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
                CreatedAt = now
            });

            _db.Notifications.Add(new Notification
            {
                UserId = loanReq.BorrowerId,
                Title = $"Đã bàn giao sách [{copy.Barcode}] — Hợp đồng #{newLoan.Id}",
                Content = $"Bạn đã nhận cuốn \"{copy.Book.Title}\" (Hạn trả: {newLoan.DueDate:dd/MM/yyyy}). Chúc bạn đọc sách vui vẻ!",
                Type = "LoanApproved",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            return (true, $"Đã xác nhận giao sách [{copy.Barcode}] (\"{copy.Book.Title}\") cho {loanReq.Borrower.FullName}! Hợp đồng #{newLoan.Id} bắt đầu (Hạn trả: {newLoan.DueDate:dd/MM/yyyy}).");
        }

        if (action.Equals("Return", StringComparison.OrdinalIgnoreCase))
        {
            if (loanReq.Loan == null || loanReq.Loan.Status == "Returned")
            {
                return (false, $"Phiếu #{loanReq.Id} chưa có hợp đồng đang mượn hoặc đã hoàn trả trước đó.");
            }

            int overdueDays = now > loanReq.Loan.DueDate
                ? (int)Math.Ceiling((now - loanReq.Loan.DueDate).TotalDays)
                : 0;
            decimal fine = overdueDays > 0 ? overdueDays * DailyFineRateVnd : 0m;

            loanReq.Loan.ReturnDate = now;
            loanReq.Loan.Status = "Returned";
            loanReq.Loan.FineAmount = Math.Max(loanReq.Loan.FineAmount, fine);

            bool isDamaged = string.Equals(request.ReturnCondition, "Damaged", StringComparison.OrdinalIgnoreCase);
            var allowedConds = new[] { "New", "Good", "Fair", "Worn" };
            string queueNotice = string.Empty;

            if (isDamaged)
            {
                copy.Status = "Damaged";
                copy.Condition = "Worn";
                copy.IsLendable = false;
                loanReq.Borrower.TrustScore = Math.Clamp(loanReq.Borrower.TrustScore - 10, 0, 200);
            }
            else
            {
                if (allowedConds.Contains(request.ReturnCondition))
                {
                    copy.Condition = request.ReturnCondition;
                }
                copy.IsLendable = true;
                loanReq.Borrower.TrustScore = Math.Clamp(loanReq.Borrower.TrustScore + 2, 0, 200);
                var (notifiedNext, nextUser) = await _loanCoordinator.HandleCopyReturnedAndNotifyQueueAsync(copy, now);
                if (notifiedNext)
                {
                    queueNotice = $" Đã tự động thông báo cho bạn đọc đứng đầu hàng chờ ({nextUser}).";
                }
            }

            _db.ConditionReports.Add(new ConditionReport
            {
                LoanId = loanReq.Loan.Id,
                ReportType = "Return",
                ReportedByUserId = staffUserId,
                ConditionGrade = isDamaged ? "Damaged" : (allowedConds.Contains(request.ReturnCondition) ? request.ReturnCondition : "Good"),
                Note = $"Thủ thư xác nhận thu hồi sách [{copy.Barcode}] tại Quầy nghiệp vụ ({request.ReturnCondition}).",
                ImageUrl = copy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
                CreatedAt = now
            });

            _db.Notifications.Add(new Notification
            {
                UserId = loanReq.BorrowerId,
                Title = $"Đã nhận trả sách [{copy.Barcode}] tại Quầy Thủ thư",
                Content = fine > 0
                    ? $"Cuốn \"{copy.Book.Title}\" đã được thu hồi (Phát sinh phí trễ hạn {fine:N0} VNĐ)."
                    : $"Cảm ơn bạn đã hoàn trả cuốn \"{copy.Book.Title}\" đúng quy định!",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            return (true, $"Đã xác nhận thu hồi bản sao [{copy.Barcode}] (\"{copy.Book.Title}\") từ {loanReq.Borrower.FullName}!{queueNotice}");
        }

        return (false, "Hành động điều phối phiếu mượn không hợp lệ.");
    }

    public async Task<(bool Success, string Message)> DirectLendBookAtDeskAsync(
        string staffUserId,
        string staffUserRole,
        S21DirectLendBookDto request)
    {
        if (staffUserRole is not ("Librarian" or "Admin"))
        {
            throw new UnauthorizedAccessException("Chỉ Thủ thư hoặc Quản trị viên mới có quyền cho mượn sách trực tiếp tại quầy.");
        }

        if (request.BookCopyId <= 0 || string.IsNullOrWhiteSpace(request.BorrowerUserId))
        {
            return (false, "Vui lòng chọn đầy đủ Bản sao sách sẵn sàng và Độc giả nhận sách.");
        }

        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .Include(bc => bc.OwnerUser)
            .FirstOrDefaultAsync(bc => bc.Id == request.BookCopyId);
        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách cần cho mượn.");
        }

        var borrowerId = request.BorrowerUserId.Trim();
        var borrower = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == borrowerId);
        if (borrower == null)
        {
            return (false, "Không tìm thấy tài khoản độc giả được chọn.");
        }

        var (isEligible, reason) = await _loanCoordinator.ValidateBorrowEligibilityAsync(borrowerId, copy);
        if (!isEligible)
        {
            return (false, reason);
        }

        int days = Math.Clamp(request.DesiredDays > 0 ? request.DesiredDays : 14, 3, 45);
        var now = DateTime.UtcNow;

        var loanReq = new LoanRequest
        {
            BookCopyId = copy.Id,
            BorrowerId = borrowerId,
            ApproverId = staffUserId,
            LoanType = copy.OwnerType == "User" ? "P2P" : "Library",
            Status = "Approved",
            RequestedAt = now,
            DesiredDays = days,
            PickupHubId = copy.LibraryId,
            RequestNote = string.IsNullOrWhiteSpace(request.Note)
                ? $"Cho mượn trực tiếp tại Quầy Thủ thư ({days} ngày)."
                : request.Note.Trim()
        };

        _db.LoanRequests.Add(loanReq);
        await _loanCoordinator.FulfillNotifiedReservationAsync(borrowerId, copy.BookId);

        if (!request.ImmediateHandover)
        {
            copy.Status = "Reserved";
            _db.Notifications.Add(new Notification
            {
                UserId = borrowerId,
                Title = $"Đã giữ chỗ sách [{copy.Barcode}] tại Quầy Thủ thư",
                Content = $"Thủ thư đã tạo phiếu giữ chỗ cuốn \"{copy.Book.Title}\" cho bạn ({days} ngày). Mời bạn đến nhận sách.",
                Type = "LoanApproved",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });
            await _db.SaveChangesAsync();
            return (true, $"Đã lập phiếu duyệt & giữ chỗ [{copy.Barcode}] (\"{copy.Book.Title}\") cho {borrower.FullName}!");
        }

        await _db.SaveChangesAsync();

        var loan = new Loan
        {
            LoanRequestId = loanReq.Id,
            StartDate = now,
            DueDate = now.AddDays(days),
            RenewCount = 0,
            FineAmount = 0m,
            Status = "Active"
        };
        copy.Status = "Borrowed";
        _db.Loans.Add(loan);

        if (loanReq.LoanType == "P2P" && copy.OwnerUser != null)
        {
            copy.OwnerUser.LendQuotaBonus += 1;
            copy.OwnerUser.TrustScore = Math.Clamp(copy.OwnerUser.TrustScore + 3, 0, 200);
        }

        await _db.SaveChangesAsync();

        _db.ConditionReports.Add(new ConditionReport
        {
            LoanId = loan.Id,
            ReportType = "Handover",
            ReportedByUserId = staffUserId,
            ConditionGrade = copy.Condition,
            Note = $"Cho mượn & bàn giao trực tiếp bản sao [{copy.Barcode}] tại Quầy Thủ thư.",
            ImageUrl = copy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
            CreatedAt = now
        });

        _db.Notifications.Add(new Notification
        {
            UserId = borrowerId,
            Title = $"Đã mượn sách [{copy.Barcode}] tại Quầy Thủ thư",
            Content = $"Thủ thư đã bàn giao cuốn \"{copy.Book.Title}\" cho bạn (Hạn trả: {loan.DueDate:dd/MM/yyyy}).",
            Type = "LoanApproved",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();
        return (true, $"Đã cho {borrower.FullName} mượn trực tiếp cuốn \"{copy.Book.Title}\" ([{copy.Barcode}]) trong {days} ngày! Hợp đồng #{loan.Id} (Hạn trả: {loan.DueDate:dd/MM/yyyy}).");
    }

    public async Task<(bool Success, string Message, string ActionPerformed)> ProcessQuickBarcodeScanAsync(
        string librarianUserId,
        S21QuickScanBarcodeDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Barcode))
        {
            return (false, "Vui lòng nhập hoặc chọn mã vạch Barcode của bản sao sách.", string.Empty);
        }

        string cleanBarcode = request.Barcode.Trim().ToUpperInvariant();
        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
                .ThenInclude(b => b.Reservations)
            .Include(bc => bc.LoanRequests)
                .ThenInclude(lr => lr.Loan)
            .Include(bc => bc.LoanRequests)
                .ThenInclude(lr => lr.Borrower)
            .FirstOrDefaultAsync(bc => bc.Barcode == cleanBarcode);

        if (copy == null)
        {
            return (false, $"MÃ 404: Không tìm thấy bản sao sách nào có mã Barcode [{cleanBarcode}] trong kho.", string.Empty);
        }

        var now = DateTime.UtcNow;
        var activeLoan = copy.LoanRequests
            .Select(r => r.Loan)
            .FirstOrDefault(l => l != null && (l.Status == "Active" || l.Status == "Overdue"));

        bool isCheckin = request.DeskAction == "Checkin" ||
                         (request.DeskAction == "AutoDetect" && (copy.Status == "Borrowed" || activeLoan != null));

        if (isCheckin)
        {
            if (activeLoan != null)
            {
                int overdueDays = now > activeLoan.DueDate
                    ? (int)Math.Ceiling((now - activeLoan.DueDate).TotalDays)
                    : 0;
                decimal fine = overdueDays > 0 ? overdueDays * DailyFineRateVnd : 0m;

                activeLoan.ReturnDate = now;
                activeLoan.Status = "Returned";
                activeLoan.FineAmount = Math.Max(activeLoan.FineAmount, fine);

                _db.ConditionReports.Add(new ConditionReport
                {
                    LoanId = activeLoan.Id,
                    ReportType = "Return",
                    ReportedByUserId = librarianUserId,
                    ConditionGrade = string.IsNullOrWhiteSpace(request.ReturnCondition) ? "Good" : request.ReturnCondition,
                    Note = $"Quét mã vạch [{cleanBarcode}] thu hồi nhanh tại Quầy Thủ thư.",
                    ImageUrl = "/images/book-placeholder.svg",
                    CreatedAt = now
                });

                var activeReq = copy.LoanRequests.FirstOrDefault(r => r.Loan?.Id == activeLoan.Id);
                if (activeReq != null)
                {
                    _db.Notifications.Add(new Notification
                    {
                        UserId = activeReq.BorrowerId,
                        Title = $"Đã thu hồi sách [{cleanBarcode}] tại Quầy Thủ thư",
                        Content = fine > 0
                            ? $"Sách \"{copy.Book.Title}\" đã được thu hồi (Phát sinh phí trễ hạn {fine:N0} VNĐ)."
                            : $"Cảm ơn bạn đã hoàn trả cuốn \"{copy.Book.Title}\" thành công.",
                        Type = "System",
                        IsRead = false,
                        EmailSentViaResend = false,
                        CreatedAt = now
                    });
                }
            }

            if (string.Equals(request.ReturnCondition, "Damaged", StringComparison.OrdinalIgnoreCase))
            {
                copy.Status = "Damaged";
                copy.Condition = "Worn";
                copy.IsLendable = false;
            }
            else
            {
                copy.IsLendable = true;
                await _loanCoordinator.HandleCopyReturnedAndNotifyQueueAsync(copy, now);
            }

            await _db.SaveChangesAsync();
            return (true,
                $"Đã quét mã [{cleanBarcode}] (\"{copy.Book.Title}\") — Thu hồi sách trả tại quầy thành công!",
                "Checkin");
        }

        // Luồng xuất kho bàn giao sách tại quầy (chỉ xét phiếu chưa từng tạo hợp đồng Loan)
        var pendingReq = copy.LoanRequests
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefault(r => r.Loan == null && (r.Status == "Pending" || r.Status == "Approved"));

        int desiredDays = Math.Clamp(request.DesiredDays > 0 ? request.DesiredDays : 14, 3, 30);

        if (pendingReq == null)
        {
            if (string.IsNullOrWhiteSpace(request.BorrowerUserId))
            {
                return (false, $"Bản sao [{cleanBarcode}] chưa có phiếu yêu cầu mượn chờ duyệt. Vui lòng chọn độc giả nhận sách trước khi xuất kho.", string.Empty);
            }

            var (isEligible, reason) = await _loanCoordinator.ValidateBorrowEligibilityAsync(request.BorrowerUserId.Trim(), copy);
            if (!isEligible)
            {
                return (false, reason, string.Empty);
            }

            pendingReq = new LoanRequest
            {
                BookCopyId = copy.Id,
                BorrowerId = request.BorrowerUserId.Trim(),
                ApproverId = librarianUserId,
                LoanType = copy.OwnerType == "User" ? "P2P" : "Library",
                Status = "Approved",
                RequestedAt = now,
                DesiredDays = desiredDays,
                PickupHubId = copy.LibraryId,
                RequestNote = $"Quét mã vạch [{cleanBarcode}] duyệt xuất kho trực tiếp tại Quầy Thủ thư."
            };
            _db.LoanRequests.Add(pendingReq);
            await _loanCoordinator.FulfillNotifiedReservationAsync(pendingReq.BorrowerId, copy.BookId);
            await _db.SaveChangesAsync();
        }
        else
        {
            if (pendingReq.Status == "Pending")
            {
                var (canApprove, reason) = await _loanCoordinator.ValidateCopyApprovalAsync(pendingReq);
                if (!canApprove)
                {
                    return (false, reason, string.Empty);
                }
            }
            pendingReq.Status = "Approved";
            pendingReq.ApproverId = librarianUserId;
        }

        var existingLoan = await _db.Loans.FirstOrDefaultAsync(l => l.LoanRequestId == pendingReq.Id);
        if (existingLoan == null)
        {
            var newLoan = new Loan
            {
                LoanRequestId = pendingReq.Id,
                StartDate = now,
                DueDate = now.AddDays(pendingReq.DesiredDays > 0 ? pendingReq.DesiredDays : desiredDays),
                RenewCount = 0,
                FineAmount = 0m,
                Status = "Active"
            };
            _db.Loans.Add(newLoan);
            await _db.SaveChangesAsync();

            _db.ConditionReports.Add(new ConditionReport
            {
                LoanId = newLoan.Id,
                ReportType = "Handover",
                ReportedByUserId = librarianUserId,
                ConditionGrade = copy.Condition,
                Note = $"Quét mã vạch [{cleanBarcode}] bàn giao sách tại Quầy Thủ thư.",
                ImageUrl = "/images/book-placeholder.svg",
                CreatedAt = now
            });

            _db.Notifications.Add(new Notification
            {
                UserId = pendingReq.BorrowerId,
                Title = $"Đã xuất kho giao sách [{cleanBarcode}]",
                Content = $"Bạn đã nhận cuốn \"{copy.Book.Title}\" tại quầy Thủ thư (Hạn trả: {newLoan.DueDate:dd/MM/yyyy}).",
                Type = "LoanApproved",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });
        }

        copy.Status = "Borrowed";
        await _db.SaveChangesAsync();

        return (true,
            $"Đã quét mã [{cleanBarcode}] (\"{copy.Book.Title}\") — Xác nhận xuất kho giao sách thành công!",
            "Checkout");
    }

    public async Task<(bool Success, string Message, List<string> GeneratedBarcodes)> BatchImportSystemCopiesAsync(
        string librarianUserId,
        S21BatchImportCopiesDto request)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == request.BookId);
        if (book == null)
        {
            return (false, "Không tìm thấy đầu sách cần nhập lô bản sao.", new List<string>());
        }

        int qty = Math.Clamp(request.Quantity, 1, 10);
        int maxCopyId = await _db.BookCopies.AnyAsync()
            ? await _db.BookCopies.MaxAsync(c => c.Id)
            : 50;

        var allowedCond = new[] { "New", "Good", "Fair", "Worn" };
        string cond = allowedCond.Contains(request.Condition) ? request.Condition : "New";
        string shelfLoc = string.IsNullOrWhiteSpace(request.ShelfLocation) ? "Kệ CNTT-A1" : request.ShelfLocation.Trim();

        int targetLibraryId = request.LibraryId > 0 && await _db.Libraries.AnyAsync(l => l.Id == request.LibraryId)
            ? request.LibraryId
            : await _db.Libraries.OrderBy(l => l.Id).Select(l => l.Id).FirstOrDefaultAsync();
        if (targetLibraryId <= 0)
        {
            return (false, "Chưa có chi nhánh Thư viện nào để tiếp nhận bản sao nhập kho.", new List<string>());
        }

        var generatedBarcodes = new List<string>();
        var now = DateTime.UtcNow;

        for (int i = 1; i <= qty; i++)
        {
            int seq = maxCopyId + i;
            string barcode = $"BS-SYS-{seq:D4}";
            if (await _db.BookCopies.AnyAsync(c => c.Barcode == barcode))
            {
                barcode = $"BS-SYS-{seq:D4}-{Random.Shared.Next(10, 99)}";
            }

            _db.BookCopies.Add(new BookCopy
            {
                BookId = book.Id,
                Barcode = barcode,
                OwnerType = "System",
                LibraryId = targetLibraryId,
                OwnerUserId = null,
                ShelfLocation = shelfLoc,
                Condition = cond,
                Status = "Available",
                IsLendable = true,
                CreatedAt = now
            });
            generatedBarcodes.Add(barcode);
        }

        await _db.SaveChangesAsync();

        return (true,
            $"Thủ thư đã nhập lô thành công {qty} bản sao mới cho đầu sách \"{book.Title}\" vào kho chung: [{string.Join(", ", generatedBarcodes)}]!",
            generatedBarcodes);
    }

    public async Task<(bool Success, string Message)> UpdateCopyDamageOrLostAsync(
        string librarianUserId,
        S21UpdateCopyStateDto request)
    {
        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .Include(bc => bc.LoanRequests)
                .ThenInclude(lr => lr.Loan)
            .FirstOrDefaultAsync(bc => bc.Id == request.BookCopyId && bc.OwnerType == "System");

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách Kho chung cần chuyển trạng thái.");
        }

        var allowedStatuses = new[] { "Available", "Damaged", "Lost" };
        string targetStatus = allowedStatuses.Contains(request.NewStatus) ? request.NewStatus : "Damaged";

        bool hasOpenLoan = copy.LoanRequests.Any(lr =>
            lr.Loan != null && (lr.Loan.Status == "Active" || lr.Loan.Status == "Overdue" || lr.Loan.Status == "Disputed"));
        if (targetStatus == "Available" && hasOpenLoan)
        {
            return (false, $"Bản sao [{copy.Barcode}] đang có hợp đồng mượn chưa hoàn trả. Vui lòng thực hiện Nhận trả sách tại quầy trước khi đặt về Sẵn sàng.");
        }

        var allowedConds = new[] { "New", "Good", "Fair", "Worn" };
        string targetCond = allowedConds.Contains(request.NewCondition) ? request.NewCondition : "Worn";

        copy.Status = targetStatus;
        copy.Condition = targetCond;
        copy.IsLendable = targetStatus == "Available";

        await _db.SaveChangesAsync();

        return (true,
            $"Đã cập nhật trạng thái bản sao [{copy.Barcode}] (\"{copy.Book.Title}\") sang Status = '{copy.Status}', Condition = '{copy.Condition}', IsLendable = {copy.IsLendable}.");
    }

    public async Task<(bool Success, string Message)> DeleteSystemCopyAsync(
        string librarianUserId,
        int bookCopyId)
    {
        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .Include(bc => bc.ShelfItems)
            .Include(bc => bc.LoanRequests)
                .ThenInclude(lr => lr.Loan)
            .FirstOrDefaultAsync(bc => bc.Id == bookCopyId && bc.OwnerType == "System");

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách Kho chung cần thanh lý/xóa.");
        }

        if (copy.Status == "Borrowed" || copy.LoanRequests.Any(lr => lr.Loan != null))
        {
            return (false, $"Bản sao [{copy.Barcode}] đã có lịch sử hợp đồng mượn, không thể xóa cứng. Hãy chuyển sang trạng thái Damaged hoặc Lost.");
        }

        if (copy.ShelfItems.Any())
        {
            _db.ShelfItems.RemoveRange(copy.ShelfItems);
        }
        if (copy.LoanRequests.Any())
        {
            _db.LoanRequests.RemoveRange(copy.LoanRequests);
        }

        _db.BookCopies.Remove(copy);
        await _db.SaveChangesAsync();

        return (true, $"Thủ thư đã thanh lý và xóa bản sao [{copy.Barcode}] (\"{copy.Book.Title}\") khỏi kho chung!");
    }
}
