using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S09P2PLoanService : IS09P2PLoanService
{
    private readonly BookShareDbContext _db;
    private readonly ILoanFlowCoordinatorService _loanCoordinator;

    public S09P2PLoanService(BookShareDbContext db, ILoanFlowCoordinatorService loanCoordinator)
    {
        _db = db;
        _loanCoordinator = loanCoordinator;
    }

    public async Task<S09P2PLoanPageViewModel> GetP2PLoanFlowAsync(string currentUserId)
    {
        var currentUser = !string.IsNullOrWhiteSpace(currentUserId)
            ? await _db.AspNetUsers
                .Include(u => u.Roles)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == currentUserId)
            : null;

        bool isStaff = currentUser != null &&
                       currentUser.Roles.Any(r => r.Name == "Curator" || r.Name == "Librarian" || r.Name == "Admin");

        var hubs = await _db.Libraries
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderByDescending(l => l.HubType != "Library")
            .ThenBy(l => l.Id)
            .Select(l => new LibraryOptionDto
            {
                Id = l.Id,
                Name = l.Name,
                HubType = l.HubType
            })
            .ToListAsync();

        var availableP2P = await _db.BookCopies
            .AsNoTracking()
            .Include(bc => bc.Book)
            .Include(bc => bc.OwnerUser)
            .Where(bc => bc.OwnerType == "User" && bc.IsLendable && bc.Status == "Available")
            .OrderByDescending(bc => bc.OwnerUser != null ? bc.OwnerUser.TrustScore : 100)
            .Select(bc => new S09AvailableP2PCopyDto
            {
                BookCopyId = bc.Id,
                Barcode = bc.Barcode,
                Title = bc.Book.Title,
                Author = bc.Book.Author,
                OwnerUserId = bc.OwnerUserId ?? string.Empty,
                OwnerName = bc.OwnerUser != null ? bc.OwnerUser.FullName : "Chủ sách P2P",
                OwnerTrustScore = bc.OwnerUser != null ? bc.OwnerUser.TrustScore : 100
            })
            .ToListAsync();

        var p2pRequests = currentUser == null
            ? new List<BookShare.Web.Models.Entities.LoanRequest>()
            : await _db.LoanRequests
                .AsNoTracking()
                .Include(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
                .Include(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.OwnerUser)
                .Include(lr => lr.Borrower)
                .Include(lr => lr.PickupHub)
                .Include(lr => lr.Loan)
                .Where(lr => lr.LoanType == "P2P" &&
                             (isStaff || lr.BorrowerId == currentUserId || lr.BookCopy.OwnerUserId == currentUserId || lr.ApproverId == currentUserId))
                .OrderByDescending(lr => lr.RequestedAt)
                .ToListAsync();

        var items = p2pRequests.Select(lr => new S09P2PLoanItemDto
        {
            LoanRequestId = lr.Id,
            LoanId = lr.Loan?.Id,
            BookId = lr.BookCopy.BookId,
            BookCopyId = lr.BookCopyId,
            Barcode = lr.BookCopy.Barcode,
            BookTitle = lr.BookCopy.Book.Title,
            BookAuthor = lr.BookCopy.Book.Author,
            CoverImageUrl = lr.BookCopy.Book.CoverImageUrl,
            OwnerUserId = lr.BookCopy.OwnerUserId ?? lr.ApproverId ?? string.Empty,
            OwnerName = lr.BookCopy.OwnerUser?.FullName ?? "Chủ tủ sách P2P",
            OwnerTrustScore = lr.BookCopy.OwnerUser?.TrustScore ?? 115,
            OwnerQuotaBonus = lr.BookCopy.OwnerUser?.LendQuotaBonus ?? 1,
            BorrowerId = lr.BorrowerId,
            BorrowerName = lr.Borrower.FullName,
            BorrowerTrustScore = lr.Borrower.TrustScore,
            PickupHubId = lr.PickupHubId,
            PickupHubName = lr.PickupHub?.Name ?? "Trạm Sách CLB Sinh Viên Cầu Giấy (Hub P2P)",
            PickupHubAddress = lr.PickupHub?.Address ?? "Cầu Giấy, Hà Nội",
            DesiredDays = lr.DesiredDays,
            RequestedAt = lr.RequestedAt,
            RequestStatus = lr.Status,
            CopyStatus = lr.BookCopy.Status,
            LoanStatus = lr.Loan?.Status,
            StartDate = lr.Loan?.StartDate,
            DueDate = lr.Loan?.DueDate,
            ReturnDate = lr.Loan?.ReturnDate,
            RequestNote = lr.RequestNote
        }).ToList();

        return new S09P2PLoanPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserName = currentUser?.FullName ?? "Khách tham quan",
            CurrentUserTrustScore = currentUser?.TrustScore ?? 100,
            CurrentUserLendQuotaBonus = currentUser?.LendQuotaBonus ?? 0,
            PickupHubs = hubs,
            AvailableP2PCopies = availableP2P,
            P2PItems = items
        };
    }

    public async Task<(bool Success, string Message)> CreateP2PLoanRequestAsync(
        string currentUserId,
        CreateP2PLoanRequestDto request)
    {
        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .Include(bc => bc.OwnerUser)
            .FirstOrDefaultAsync(bc => bc.Id == request.BookCopyId && bc.OwnerType == "User");

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách chia sẻ cộng đồng hợp lệ.");
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
            return (false, "Bạn đã gửi yêu cầu mượn bản sao này và đang chờ chủ sách duyệt.");
        }

        var borrowerMemberships = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.UserId == currentUserId && m.IsActive)
            .ToListAsync();
        var tierPlan = MembershipTierCatalog.GetEffectivePolicy(borrowerMemberships, DateTime.UtcNow);
        int days = Math.Clamp(request.DesiredDays, 3, tierPlan.MaxBorrowDays);
        int? resolvedHubId = await _db.Libraries.AnyAsync(l => l.Id == request.PickupHubId && l.IsActive)
            ? request.PickupHubId
            : (copy.LibraryId ?? await _db.Libraries.Where(l => l.IsActive).OrderBy(l => l.Id).Select(l => (int?)l.Id).FirstOrDefaultAsync());

        var newReq = new LoanRequest
        {
            BookCopyId = copy.Id,
            BorrowerId = currentUserId,
            ApproverId = copy.OwnerUserId,
            LoanType = "P2P",
            Status = "Pending",
            RequestedAt = DateTime.UtcNow,
            DesiredDays = days,
            PickupHubId = resolvedHubId,
            RequestNote = string.IsNullOrWhiteSpace(request.RequestNote)
                ? $"Xin phép mượn sách \"{copy.Book.Title}\" ({days} ngày) và hẹn giao nhận tại điểm trạm."
                : request.RequestNote.Trim()
        };

        _db.LoanRequests.Add(newReq);
        await _loanCoordinator.FulfillNotifiedReservationAsync(currentUserId, copy.BookId);

        if (!string.IsNullOrWhiteSpace(copy.OwnerUserId))
        {
            _db.Notifications.Add(new Notification
            {
                UserId = copy.OwnerUserId,
                Title = $"Yêu cầu mượn sách P2P mới: {copy.Book.Title}",
                Content = $"Một thành viên vừa gửi đề nghị mượn bản sao [{copy.Barcode}] trong {days} ngày.",
                Type = "LoanApproved",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        return (true, $"Đã gửi yêu cầu mượn P2P #{newReq.Id} tới chủ sách {copy.OwnerUser?.FullName ?? "Cộng đồng"}!");
    }

    public async Task<(bool Success, string Message)> ApproveOrRejectP2PLoanAsync(
        string currentUserId,
        string currentUserRole,
        ApproveP2PLoanDto request)
    {
        var loanReq = await _db.LoanRequests
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Book)
            .Include(lr => lr.Borrower)
            .FirstOrDefaultAsync(lr => lr.Id == request.LoanRequestId && lr.LoanType == "P2P");

        if (loanReq == null)
        {
            return (false, "Không tìm thấy yêu cầu mượn P2P.");
        }

        if (loanReq.BookCopy.OwnerUserId != currentUserId && currentUserRole != "Admin" && currentUserRole != "Librarian")
        {
            throw new UnauthorizedAccessException("Chỉ chủ sở hữu sách, Thủ thư hoặc Quản trị viên mới được duyệt hoặc từ chối yêu cầu mượn này.");
        }

        if (loanReq.Status != "Pending")
        {
            return (false, $"Yêu cầu P2P #{loanReq.Id} đã ở trạng thái '{loanReq.Status}'.");
        }

        var now = DateTime.UtcNow;

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
                Title = $"Yêu cầu mượn P2P #{loanReq.Id} chưa được duyệt",
                Content = $"Chủ sách đã từ chối yêu cầu mượn bản sao [{loanReq.BookCopy.Barcode}] - \"{loanReq.BookCopy.Book.Title}\".",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });
            await _db.SaveChangesAsync();
            return (true, $"Chủ sách đã từ chối yêu cầu mượn P2P #{loanReq.Id}.");
        }

        var (canApprove, reason) = await _loanCoordinator.ValidateCopyApprovalAsync(loanReq);
        if (!canApprove)
        {
            return (false, reason);
        }

        loanReq.Status = "Approved";
        loanReq.ApproverId = currentUserId;
        if (request.PickupHubId.HasValue && request.PickupHubId.Value > 0 &&
            await _db.Libraries.AnyAsync(l => l.Id == request.PickupHubId.Value && l.IsActive))
        {
            loanReq.PickupHubId = request.PickupHubId.Value;
        }
        loanReq.BookCopy.Status = "Reserved";

        _db.Notifications.Add(new Notification
        {
            UserId = loanReq.BorrowerId,
            Title = $"Yêu cầu mượn P2P #{loanReq.Id} đã được duyệt",
            Content = $"Chủ sách đã chấp thuận cho bạn mượn bản sao [{loanReq.BookCopy.Barcode}] - \"{loanReq.BookCopy.Book.Title}\".",
            Type = "LoanApproved",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();
        return (true, $"Chủ sách đã duyệt yêu cầu P2P #{loanReq.Id} của {loanReq.Borrower.FullName}! Sách chuyển sang trạng thái giữ chỗ chờ giao nhận.");
    }

    public async Task<(bool Success, string Message)> AdvanceP2PStageAsync(
        string currentUserId,
        string currentUserRole,
        P2PStageTransitionDto request)
    {
        var loanReq = await _db.LoanRequests
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Book)
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.OwnerUser)
            .Include(lr => lr.Borrower)
            .Include(lr => lr.PickupHub)
            .Include(lr => lr.Loan)
            .FirstOrDefaultAsync(lr => lr.Id == request.LoanRequestId && lr.LoanType == "P2P");

        if (loanReq == null)
        {
            return (false, "Không tìm thấy giao dịch mượn P2P.");
        }

        bool isOwner = loanReq.BookCopy.OwnerUserId == currentUserId;
        bool isBorrower = loanReq.BorrowerId == currentUserId;
        bool isStaff = currentUserRole is "Curator" or "Librarian" or "Admin";
        var now = DateTime.UtcNow;

        if (request.Stage == "InTransit")
        {
            if (!isOwner && !isStaff)
            {
                throw new UnauthorizedAccessException("Chỉ chủ sách, Chủ nhiệm CLB hoặc Quản trị viên mới được xác nhận ký gửi sách tại điểm trạm.");
            }

            if (loanReq.Status != "Approved")
            {
                return (false, "Yêu cầu P2P cần được chủ sách duyệt trước khi ký gửi tại điểm trạm.");
            }

            loanReq.BookCopy.Status = "InTransit";
            _db.Notifications.Add(new Notification
            {
                UserId = loanReq.BorrowerId,
                Title = $"Sách \"{loanReq.BookCopy.Book.Title}\" đã tới điểm hẹn",
                Content = $"Bản sao [{loanReq.BookCopy.Barcode}] đang chờ bạn nhận tại {loanReq.PickupHub?.Name ?? "Trạm CLB"}.",
                Type = "LoanApproved",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });
            await _db.SaveChangesAsync();
            return (true, $"Chủ sách đã mang bản sao [{loanReq.BookCopy.Barcode}] tới điểm hẹn ({loanReq.PickupHub?.Name ?? "Trạm CLB"}).");
        }

        if (request.Stage == "HandoverBorrowed")
        {
            if (!isOwner && !isBorrower && !isStaff)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền xác nhận bàn giao đối với giao dịch mượn này.");
            }

            if (loanReq.Status != "Approved")
            {
                return (false, "Yêu cầu P2P phải được chủ sách duyệt trước khi bàn giao sách.");
            }

            if (loanReq.Loan != null)
            {
                return (false, "Sách P2P này đã được xác nhận bàn giao trước đó.");
            }

            loanReq.BookCopy.Status = "Borrowed";

            var newLoan = new Loan
            {
                LoanRequestId = loanReq.Id,
                StartDate = now,
                DueDate = now.AddDays(loanReq.DesiredDays),
                RenewCount = 0,
                FineAmount = 0,
                Status = "Active"
            };
            _db.Loans.Add(newLoan);

            if (loanReq.BookCopy.OwnerUser != null)
            {
                loanReq.BookCopy.OwnerUser.LendQuotaBonus += 1;
                loanReq.BookCopy.OwnerUser.TrustScore = Math.Clamp(loanReq.BookCopy.OwnerUser.TrustScore + 3, 0, 200);
            }

            await _db.SaveChangesAsync();

            _db.ConditionReports.Add(new ConditionReport
            {
                LoanId = newLoan.Id,
                ReportType = "Handover",
                ReportedByUserId = currentUserId,
                ConditionGrade = loanReq.BookCopy.Condition,
                Note = $"Biên bản giao nhận P2P tại {loanReq.PickupHub?.Name ?? "Trạm CLB"} cho bản sao [{loanReq.BookCopy.Barcode}].",
                ImageUrl = loanReq.BookCopy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
                CreatedAt = now
            });

            if (loanReq.BookCopy.OwnerUserId != null)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = loanReq.BookCopy.OwnerUserId,
                    Title = $"Đã bàn giao sách [{loanReq.BookCopy.Barcode}] thành công",
                    Content = $"Bạn được cộng +1 suất mượn thưởng và +3 điểm uy tín khi chia sẻ sách \"{loanReq.BookCopy.Book.Title}\".",
                    Type = "LoanApproved",
                    IsRead = false,
                    EmailSentViaResend = false,
                    CreatedAt = now
                });
            }

            await _db.SaveChangesAsync();

            string ownerName = loanReq.BookCopy.OwnerUser?.FullName ?? "Chủ sách";
            int newBonus = loanReq.BookCopy.OwnerUser?.LendQuotaBonus ?? 1;
            return (true, $"Hai bên đã xác nhận giao nhận sách P2P [{loanReq.BookCopy.Barcode}]! Chủ sách {ownerName} được cộng +1 hạn mức mượn thưởng (Tổng thưởng: +{newBonus}) và +3 điểm uy tín.");
        }

        if (request.Stage == "Returned")
        {
            if (!isOwner && !isStaff)
            {
                throw new UnauthorizedAccessException("Chỉ chủ sách, Chủ nhiệm CLB hoặc Quản trị viên mới được xác nhận đã thu hồi sách hoàn trả.");
            }

            if (loanReq.Loan == null || loanReq.Loan.Status == "Returned")
            {
                return (false, "Hợp đồng P2P này chưa kích hoạt hoặc đã hoàn trả.");
            }

            loanReq.Loan.ReturnDate = now;
            loanReq.Loan.Status = "Returned";
            loanReq.Borrower.TrustScore = Math.Clamp(loanReq.Borrower.TrustScore + 2, 0, 200);

            _db.ConditionReports.Add(new ConditionReport
            {
                LoanId = loanReq.Loan.Id,
                ReportType = "Return",
                ReportedByUserId = currentUserId,
                ConditionGrade = loanReq.BookCopy.Condition,
                Note = $"Hoàn trả sách P2P [{loanReq.BookCopy.Barcode}] nguyên vẹn tại Điểm hẹn CLB.",
                ImageUrl = loanReq.BookCopy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
                CreatedAt = now
            });

            var (notifiedNext, notifiedUserName) = await _loanCoordinator.HandleCopyReturnedAndNotifyQueueAsync(loanReq.BookCopy, now);

            _db.Notifications.Add(new Notification
            {
                UserId = loanReq.BorrowerId,
                Title = $"Hoàn trả sách [{loanReq.BookCopy.Barcode}] thành công",
                Content = $"Cảm ơn bạn đã trả cuốn \"{loanReq.BookCopy.Book.Title}\" đúng hẹn. Bạn được cộng +2 điểm uy tín.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();

            string queueMsg = notifiedNext ? $" Đã tự động thông báo cho bạn đọc đứng đầu hàng chờ ({notifiedUserName})." : string.Empty;
            return (true, $"Đã xác nhận hoàn trả sách P2P [{loanReq.BookCopy.Barcode}]! Người mượn {loanReq.Borrower.FullName} được cộng +2 điểm uy tín.{queueMsg}");
        }

        return (false, "Trạng thái chuyển tiếp P2P không hợp lệ.");
    }
}
