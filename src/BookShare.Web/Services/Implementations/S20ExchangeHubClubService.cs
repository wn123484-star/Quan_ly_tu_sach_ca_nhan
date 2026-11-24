using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S20ExchangeHubClubService : IS20ExchangeHubClubService
{
    private readonly BookShareDbContext _db;
    private readonly ILoanFlowCoordinatorService _loanCoordinator;

    public S20ExchangeHubClubService(BookShareDbContext db, ILoanFlowCoordinatorService loanCoordinator)
    {
        _db = db;
        _loanCoordinator = loanCoordinator;
    }

    public async Task<S20ExchangeHubClubPageViewModel> GetExchangeHubPageAsync(
        string currentUserId,
        string currentUserRole)
    {
        var hubs = await _db.Libraries
            .AsNoTracking()
            .Include(l => l.ManagerUser)
            .Include(l => l.BookCopies)
            .Include(l => l.LoanRequests)
            .OrderByDescending(l => l.HubType == "ClubStation" || l.HubType == "DropoffHub")
            .ThenBy(l => l.Id)
            .Select(l => new S20HubStationDto
            {
                HubId = l.Id,
                Name = l.Name,
                Address = l.Address,
                ManagerName = l.ManagerUser.FullName,
                Phone = l.Phone ?? "0901-000-003",
                HubType = l.HubType,
                OpenHours = l.OpenHours ?? "08:00 - 18:00",
                IsActive = l.IsActive,
                CopiesStoredCount = l.BookCopies.Count,
                ActiveLoanRequestsCount = l.LoanRequests.Count(r => r.Status == "Pending" || r.Status == "Approved")
            })
            .ToListAsync();

        var p2pCopies = await _db.BookCopies
            .AsNoTracking()
            .Include(bc => bc.Book)
            .Include(bc => bc.OwnerUser)
            .Include(bc => bc.Library)
            .Where(bc => bc.OwnerType == "User" || bc.LibraryId == 3)
            .OrderByDescending(bc => bc.Id)
            .Select(bc => new S20HubBookCopyDto
            {
                BookCopyId = bc.Id,
                Barcode = bc.Barcode,
                BookTitle = bc.Book.Title,
                OwnerName = bc.OwnerUser != null ? bc.OwnerUser.FullName : "CLB BookShare Hub",
                OwnerType = bc.OwnerType,
                HubId = bc.LibraryId,
                HubName = bc.Library != null ? bc.Library.Name : "Chưa gán trạm Hub",
                ShelfLocation = bc.ShelfLocation ?? "Kệ Ký Gửi CLB",
                Condition = bc.Condition,
                Status = bc.Status,
                IsLendable = bc.IsLendable
            })
            .ToListAsync();

        var hubOrders = await _db.LoanRequests
            .AsNoTracking()
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Book)
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.OwnerUser)
            .Include(lr => lr.Borrower)
            .Include(lr => lr.PickupHub)
            .Include(lr => lr.Loan)
            .Where(lr => lr.LoanType == "P2P" || lr.PickupHubId == 3)
            .OrderByDescending(lr => lr.RequestedAt)
            .Select(lr => new S20HubLoanOrderDto
            {
                LoanRequestId = lr.Id,
                LoanId = lr.Loan != null ? lr.Loan.Id : null,
                Barcode = lr.BookCopy.Barcode,
                BookTitle = lr.BookCopy.Book.Title,
                OwnerName = lr.BookCopy.OwnerUser != null ? lr.BookCopy.OwnerUser.FullName : "CLB Sách ĐH CMC",
                BorrowerName = lr.Borrower.FullName,
                BorrowerTrustScore = lr.Borrower.TrustScore,
                HubName = lr.PickupHub != null ? lr.PickupHub.Name : "Trạm CLB BookShare Cầu Giấy",
                RequestStatus = lr.Status,
                CopyStatus = lr.BookCopy.Status,
                RequestedAt = lr.RequestedAt
            })
            .ToListAsync();

        return new S20ExchangeHubClubPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserRole = currentUserRole,
            TotalHubsCount = hubs.Count,
            TotalCopiesAtHubs = p2pCopies.Count,
            PendingP2PHubOrdersCount = hubOrders.Count(o => o.RequestStatus == "Pending" || o.RequestStatus == "Approved"),
            Hubs = hubs,
            HubCopies = p2pCopies,
            HubP2POrders = hubOrders
        };
    }

    public async Task<(bool Success, string Message)> RegisterCopyDropoffAtHubAsync(
        string currentUserId,
        string currentUserRole,
        S20RegisterCopyDropoffDto request)
    {
        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .FirstOrDefaultAsync(bc => bc.Id == request.BookCopyId);

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách cần đăng ký gửi tại Trạm Hub.");
        }

        bool isPrivileged = currentUserRole is "Curator" or "Admin";
        if (copy.OwnerUserId != currentUserId && !isPrivileged)
        {
            throw new UnauthorizedAccessException("Bạn chỉ được phép đăng ký ký gửi những bản sao sách thuộc sở hữu của mình.");
        }

        var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId);
        if (hub == null)
        {
            return (false, "Không tìm thấy Điểm hẹn CLB / Hub đã chọn.");
        }

        copy.LibraryId = hub.Id;
        copy.ShelfLocation = $"Ký gửi tại {hub.Name[..Math.Min(22, hub.Name.Length)]}";
        copy.IsLendable = true;

        await _db.SaveChangesAsync();

        return (true,
            $"Đã đăng ký ký gửi bản sao [{copy.Barcode}] (\"{copy.Book.Title}\") tại điểm hẹn \"{hub.Name}\"! Chủ nhiệm CLB sẽ xác nhận kiểm định sách thực tế.");
    }

    public async Task<(bool Success, string Message)> CuratorVerifyPhysicalCopyAsync(
        string curatorUserId,
        S20CuratorVerifyCopyDto request)
    {
        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .Include(bc => bc.OwnerUser)
            .FirstOrDefaultAsync(bc => bc.Id == request.BookCopyId);

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách cần kiểm định.");
        }

        var allowedConditions = new[] { "New", "Good", "Fair", "Worn" };
        copy.Condition = allowedConditions.Contains(request.VerifiedCondition) ? request.VerifiedCondition : "Good";
        copy.LibraryId = request.HubId > 0 ? request.HubId : copy.LibraryId;
        copy.ShelfLocation = string.IsNullOrWhiteSpace(request.ShelfLocation)
            ? "Trạm CLB-K1 (Đã kiểm định)"
            : request.ShelfLocation.Trim();
        copy.IsLendable = true;

        string verifyMarker = $"[{copy.Barcode}]";
        bool alreadyVerified = await _db.Notifications.AnyAsync(n =>
            n.UserId == copy.OwnerUserId &&
            n.Title.Contains("Kiểm định sách tại Trạm CLB") &&
            n.Title.Contains(verifyMarker));

        string trustNotice = "Đã cập nhật trạng thái kiểm định (điểm thưởng kiểm định chỉ cộng 1 lần/bản sao).";
        if (!alreadyVerified && copy.OwnerUser != null)
        {
            copy.OwnerUser.TrustScore = Math.Clamp(copy.OwnerUser.TrustScore + 2, 0, 200);
            _db.Notifications.Add(new Notification
            {
                UserId = copy.OwnerUserId!,
                Title = $"Kiểm định sách tại Trạm CLB {verifyMarker}",
                Content = $"Chủ nhiệm CLB đã kiểm định bản sao {verifyMarker} (\"{copy.Book.Title}\") và cộng +2 điểm uy tín.",
                Type = "System",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = DateTime.UtcNow
            });
            trustNotice = "cộng +2 điểm uy tín cho chủ sách!";
        }

        await _db.SaveChangesAsync();

        return (true,
            $"Đã xác nhận kiểm định sách thực tế cho bản sao [{copy.Barcode}] (\"{copy.Book.Title}\"): Tình trạng [{copy.Condition}], vị trí [{copy.ShelfLocation}], {trustNotice}");
    }

    public async Task<(bool Success, string Message)> CuratorProcessHubHandoverAsync(
        string curatorUserId,
        S20CuratorHandoverActionDto request)
    {
        var req = await _db.LoanRequests
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Book)
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.OwnerUser)
            .Include(lr => lr.Borrower)
            .Include(lr => lr.Loan)
            .FirstOrDefaultAsync(lr => lr.Id == request.LoanRequestId);

        if (req == null)
        {
            return (false, "Không tìm thấy đơn mượn tại Trạm CLB.");
        }

        var now = DateTime.UtcNow;
        var copy = req.BookCopy;

        if (request.ActionType == "CheckInDropoff")
        {
            if (req.Status is "Rejected" or "Cancelled" or "Expired")
            {
                return (false, $"Đơn mượn #{req.Id} đã ở trạng thái '{req.Status}', không thể ký gửi.");
            }
            req.Status = "Approved";
            req.ApproverId ??= curatorUserId;
            copy.Status = "InTransit";

            _db.Notifications.Add(new Notification
            {
                UserId = req.BorrowerId,
                Title = $"Sách [{copy.Barcode}] đã sẵn sàng nhận tại Trạm CLB",
                Content = $"Chủ sách đã ký gửi \"{copy.Book.Title}\" tại điểm trạm. Mời bạn tới nhận sách.",
                Type = "LoanApproved",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();

            return (true,
                $"Trạm CLB đã xác nhận chủ sách ký gửi bản sao [{copy.Barcode}] tại quầy. Sẵn sàng bàn giao cho {req.Borrower.FullName}!");
        }

        if (request.ActionType == "HandoverToBorrower")
        {
            if (req.Loan != null)
            {
                return (false, $"Đơn mượn #{req.Id} đã được bàn giao và kích hoạt hợp đồng trước đó.");
            }

            if (req.Status is "Rejected" or "Cancelled" or "Expired")
            {
                return (false, $"Đơn mượn #{req.Id} ở trạng thái '{req.Status}', không thể bàn giao.");
            }

            req.Status = "Approved";
            req.ApproverId ??= curatorUserId;
            copy.Status = "Borrowed";

            var loan = new Loan
            {
                LoanRequestId = req.Id,
                StartDate = now,
                DueDate = now.AddDays(req.DesiredDays > 0 ? req.DesiredDays : 14),
                RenewCount = 0,
                FineAmount = 0m,
                Status = "Active"
            };
            _db.Loans.Add(loan);

            if (copy.OwnerUser != null)
            {
                copy.OwnerUser.LendQuotaBonus += 1;
                copy.OwnerUser.TrustScore = Math.Clamp(copy.OwnerUser.TrustScore + 3, 0, 200);
            }

            await _db.SaveChangesAsync();

            _db.ConditionReports.Add(new ConditionReport
            {
                LoanId = loan.Id,
                ReportType = "Handover",
                ReportedByUserId = curatorUserId,
                ConditionGrade = copy.Condition,
                Note = $"Biên bản giao sách trung gian tại Trạm CLB cho bản sao [{copy.Barcode}].",
                ImageUrl = copy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
                CreatedAt = now
            });

            _db.Notifications.Add(new Notification
            {
                UserId = req.BorrowerId,
                Title = $"Đã nhận sách [{copy.Barcode}] tại Trạm CLB",
                Content = $"Hợp đồng mượn cuốn \"{copy.Book.Title}\" đã bắt đầu (Hạn trả: {loan.DueDate:dd/MM/yyyy}).",
                Type = "LoanApproved",
                IsRead = false,
                EmailSentViaResend = false,
                CreatedAt = now
            });

            if (copy.OwnerUserId != null)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = copy.OwnerUserId,
                    Title = $"Trạm CLB đã giao sách [{copy.Barcode}] cho người mượn",
                    Content = $"Bạn được cộng +1 suất mượn thưởng và +3 điểm uy tín.",
                    Type = "LoanApproved",
                    IsRead = false,
                    EmailSentViaResend = false,
                    CreatedAt = now
                });
            }

            await _db.SaveChangesAsync();

            return (true,
                $"Trạm CLB đã bàn giao bản sao [{copy.Barcode}] cho {req.Borrower.FullName}! Đã tạo biên bản bàn giao và cộng +1 suất mượn thưởng, +3 điểm uy tín cho chủ sách.");
        }

        // ConfirmReturnAtHub
        if (req.Loan == null || req.Loan.Status == "Returned")
        {
            return (false, "Đơn mượn này chưa có hợp đồng đang hoạt động hoặc đã hoàn trả trước đó.");
        }

        req.Loan.Status = "Returned";
        req.Loan.ReturnDate = now;
        req.Borrower.TrustScore = Math.Clamp(req.Borrower.TrustScore + 2, 0, 200);

        _db.ConditionReports.Add(new ConditionReport
        {
            LoanId = req.Loan.Id,
            ReportType = "Return",
            ReportedByUserId = curatorUserId,
            ConditionGrade = copy.Condition,
            Note = $"Biên bản thu hồi sách nguyên vẹn tại Trạm CLB cho bản sao [{copy.Barcode}].",
            ImageUrl = copy.Book.CoverImageUrl ?? "/images/book-placeholder.svg",
            CreatedAt = now
        });

        var (notifiedNext, notifiedUserName) = await _loanCoordinator.HandleCopyReturnedAndNotifyQueueAsync(copy, now);

        _db.Notifications.Add(new Notification
        {
            UserId = req.BorrowerId,
            Title = $"Đã trả sách [{copy.Barcode}] tại Trạm CLB",
            Content = $"Cảm ơn bạn đã hoàn trả cuốn \"{copy.Book.Title}\" nguyên vẹn (+2 điểm uy tín).",
            Type = "System",
            IsRead = false,
            EmailSentViaResend = false,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();

        string queueNotice = notifiedNext ? $" Đã tự động thông báo cho bạn đọc đứng đầu hàng chờ ({notifiedUserName})." : string.Empty;
        return (true,
            $"Trạm CLB đã thu hồi bản sao [{copy.Barcode}] từ {req.Borrower.FullName} và cộng +2 điểm uy tín!{queueNotice}");
    }

            public async Task<(bool Success, string Message)> UpdateHubStationInfoAsync(
                string curatorUserId,
                S20UpdateHubStationDto request)
            {
                if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Address))
                {
                    return (false, "Tên điểm trạm và Địa chỉ không được để trống.");
                }

                var hub = await _db.Libraries.FirstOrDefaultAsync(l => l.Id == request.HubId);
                if (hub == null)
                {
                    return (false, "Không tìm thấy Điểm trạm CLB / Thư viện cần cập nhật.");
                }

                hub.Name = request.Name.Trim();
                hub.Address = request.Address.Trim();
                if (!string.IsNullOrWhiteSpace(request.OpenHours))
                {
                    hub.OpenHours = request.OpenHours.Trim();
                }
                if (!string.IsNullOrWhiteSpace(request.Phone))
                {
                    hub.Phone = request.Phone.Trim();
                }

                await _db.SaveChangesAsync();
                return (true, $"Đã cập nhật thông tin vận hành cho điểm trạm \"{hub.Name}\" thành công!");
            }
        }
