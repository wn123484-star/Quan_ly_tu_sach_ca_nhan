using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S20ExchangeHubClubService : IS20ExchangeHubClubService
{
    private readonly BookShareDbContext _db;

    public S20ExchangeHubClubService(BookShareDbContext db)
    {
        _db = db;
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
        S20RegisterCopyDropoffDto request)
    {
        var copy = await _db.BookCopies
            .Include(bc => bc.Book)
            .FirstOrDefaultAsync(bc => bc.Id == request.BookCopyId);

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách cần đăng ký gửi tại Trạm Hub.");
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
            $"Đã đăng ký ký gửi bản sao [{copy.Barcode}] (\"{copy.Book.Title}\") tại điểm hẹn \"{hub.Name}\"! Chủ nhiệm CLB (Curator) sẽ xác nhận kiểm định sách thật ngoài đời.");
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
        copy.LibraryId = request.HubId > 0 ? request.HubId : 3;
        copy.ShelfLocation = string.IsNullOrWhiteSpace(request.ShelfLocation)
            ? "Trạm CLB-K1 (Đã kiểm định)"
            : request.ShelfLocation.Trim();
        copy.IsLendable = true;

        if (copy.OwnerUser != null)
        {
            copy.OwnerUser.TrustScore = Math.Min(200, copy.OwnerUser.TrustScore + 2);
        }

        await _db.SaveChangesAsync();

        return (true,
            $"Curator đã xác nhận kiểm định sách thật ngoài đời cho bản sao [{copy.Barcode}] (\"{copy.Book.Title}\"): Tình trạng [{copy.Condition}], vị trí [{copy.ShelfLocation}], cộng +2 TrustScore cho chủ sách!");
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
            return (false, "Không tìm thấy đơn mượn P2P tại Trạm Hub.");
        }

        var now = DateTime.UtcNow;
        var copy = req.BookCopy;

        if (request.ActionType == "CheckInDropoff")
        {
            req.Status = "Approved";
            req.ApproverId ??= curatorUserId;
            copy.Status = "InTransit";
            await _db.SaveChangesAsync();

            return (true,
                $"Trạm CLB/Hub đã xác nhận Chủ sách ký gửi bản sao [{copy.Barcode}] tại quầy (Trạng thái: InTransit). Sẵn sàng bàn giao cho người mượn {req.Borrower.FullName}!");
        }

        if (request.ActionType == "HandoverToBorrower")
        {
            req.Status = "Approved";
            req.ApproverId ??= curatorUserId;
            copy.Status = "Borrowed";

            if (req.Loan == null)
            {
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
            }
            else
            {
                req.Loan.Status = "Active";
            }

            if (copy.OwnerUser != null)
            {
                copy.OwnerUser.LendQuotaBonus += 1;
                copy.OwnerUser.TrustScore = Math.Min(200, copy.OwnerUser.TrustScore + 3);
            }

            await _db.SaveChangesAsync();

            return (true,
                $"Trạm CLB/Hub đã bàn giao trung gian bản sao [{copy.Barcode}] cho {req.Borrower.FullName}! Đã kích hoạt hợp đồng mượn và cộng thưởng +1 LendQuotaBonus, +3 TrustScore cho chủ sách.");
        }

        // ConfirmReturnAtHub
        copy.Status = "Available";
        if (req.Loan != null)
        {
            req.Loan.Status = "Returned";
            req.Loan.ReturnDate = now;
        }
        req.Borrower.TrustScore = Math.Min(200, req.Borrower.TrustScore + 2);

        await _db.SaveChangesAsync();

        return (true,
            $"Trạm CLB/Hub đã xác nhận thu hồi bản sao [{copy.Barcode}] nguyên vẹn từ {req.Borrower.FullName}! Bản sao đã mở lại trạng thái Available và cộng +2 TrustScore cho người mượn.");
    }
}
