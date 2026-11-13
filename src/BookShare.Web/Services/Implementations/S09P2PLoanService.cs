using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S09P2PLoanService : IS09P2PLoanService
{
    private readonly BookShareDbContext _db;

    public S09P2PLoanService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S09P2PLoanPageViewModel> GetP2PLoanFlowAsync(string currentUserId)
    {
        var currentUser = await _db.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

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
                OwnerUserId = bc.OwnerUserId ?? "u-user-02",
                OwnerName = bc.OwnerUser != null ? bc.OwnerUser.FullName : "Chủ sách P2P",
                OwnerTrustScore = bc.OwnerUser != null ? bc.OwnerUser.TrustScore : 100
            })
            .ToListAsync();

        var p2pRequests = await _db.LoanRequests
            .AsNoTracking()
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.Book)
            .Include(lr => lr.BookCopy)
                .ThenInclude(bc => bc.OwnerUser)
            .Include(lr => lr.Borrower)
            .Include(lr => lr.PickupHub)
            .Include(lr => lr.Loan)
            .Where(lr => lr.LoanType == "P2P")
            .OrderByDescending(lr => lr.RequestedAt)
            .ToListAsync();

        var items = p2pRequests.Select(lr => new S09P2PLoanItemDto
        {
            LoanRequestId = lr.Id,
            LoanId = lr.Loan?.Id,
            BookCopyId = lr.BookCopyId,
            Barcode = lr.BookCopy.Barcode,
            BookTitle = lr.BookCopy.Book.Title,
            BookAuthor = lr.BookCopy.Book.Author,
            CoverImageUrl = lr.BookCopy.Book.CoverImageUrl,
            OwnerUserId = lr.BookCopy.OwnerUserId ?? lr.ApproverId ?? "u-user-02",
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
            CurrentUserName = currentUser?.FullName ?? "Nguyễn Hùng (BIT250157)",
            CurrentUserTrustScore = currentUser?.TrustScore ?? 128,
            CurrentUserLendQuotaBonus = currentUser?.LendQuotaBonus ?? 1,
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
            return (false, "Không tìm thấy bản sao sách P2P hợp lệ.");
        }

        if (!copy.IsLendable || copy.Status != "Available")
        {
            return (false, $"Bản sao [{copy.Barcode}] hiện không ở trạng thái sẵn sàng cho mượn P2P.");
        }

        int days = Math.Clamp(request.DesiredDays, 3, 30);

        var newReq = new LoanRequest
        {
            BookCopyId = copy.Id,
            BorrowerId = currentUserId,
            ApproverId = copy.OwnerUserId,
            LoanType = "P2P",
            Status = "Pending",
            RequestedAt = DateTime.UtcNow,
            DesiredDays = days,
            PickupHubId = request.PickupHubId > 0 ? request.PickupHubId : 3,
            RequestNote = string.IsNullOrWhiteSpace(request.RequestNote)
                ? $"Xin phép mượn P2P sách \"{copy.Book.Title}\" ({days} ngày) và hẹn giao nhận tại Trạm CLB/Hub."
                : request.RequestNote.Trim()
        };

        _db.LoanRequests.Add(newReq);
        await _db.SaveChangesAsync();

        return (true, $"Đã gửi yêu cầu mượn P2P #{newReq.Id} tới chủ sách {copy.OwnerUser?.FullName ?? "Cộng đồng"}! Điểm uy tín của bạn đã được đính kèm để chủ sách xét duyệt.");
    }

    public async Task<(bool Success, string Message)> ApproveOrRejectP2PLoanAsync(
        string currentUserId,
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

        if (!request.Approve)
        {
            loanReq.Status = "Rejected";
            if (loanReq.BookCopy.Status == "Reserved")
            {
                loanReq.BookCopy.Status = "Available";
            }
            await _db.SaveChangesAsync();
            return (true, $"Chủ sách đã từ chối yêu cầu mượn P2P #{loanReq.Id}.");
        }

        loanReq.Status = "Approved";
        if (request.PickupHubId.HasValue && request.PickupHubId.Value > 0)
        {
            loanReq.PickupHubId = request.PickupHubId.Value;
        }
        loanReq.BookCopy.Status = "Reserved";

        await _db.SaveChangesAsync();
        return (true, $"Chủ sách đã duyệt yêu cầu P2P #{loanReq.Id} của {loanReq.Borrower.FullName} (TrustScore: {loanReq.Borrower.TrustScore})! Sách chuyển sang 'Reserved' chờ mang ra Điểm hẹn Hub.");
    }

    public async Task<(bool Success, string Message)> AdvanceP2PStageAsync(
        string currentUserId,
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

        var now = DateTime.UtcNow;

        if (request.Stage == "InTransit")
        {
            if (loanReq.Status != "Approved")
            {
                return (false, "Yêu cầu P2P cần được Duyệt (Approved) trước khi ký gửi tại Điểm trạm Hub.");
            }

            loanReq.BookCopy.Status = "InTransit";
            await _db.SaveChangesAsync();
            return (true, $"Chủ sách đã mang bản sao [{loanReq.BookCopy.Barcode}] tới điểm hẹn ({loanReq.PickupHub?.Name ?? "Trạm CLB"}). Trạng thái: 'InTransit'!");
        }

        if (request.Stage == "HandoverBorrowed")
        {
            if (loanReq.Loan != null)
            {
                return (false, "Sách P2P này đã được xác nhận bàn giao trước đó.");
            }

            loanReq.Status = "Approved";
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

            // Thưởng hạn mức mượn (+1 LendQuotaBonus) và +3 TrustScore cho chủ sách khi cho mượn P2P thành công!
            if (loanReq.BookCopy.OwnerUser != null)
            {
                loanReq.BookCopy.OwnerUser.LendQuotaBonus += 1;
                loanReq.BookCopy.OwnerUser.TrustScore += 3;
            }

            await _db.SaveChangesAsync();

            _db.ConditionReports.Add(new ConditionReport
            {
                LoanId = newLoan.Id,
                ReportType = "Handover",
                ReportedByUserId = currentUserId,
                ConditionGrade = loanReq.BookCopy.Condition,
                Note = $"Biên bản giao nhận P2P tại {loanReq.PickupHub?.Name ?? "Trạm CLB"} cho bản sao [{loanReq.BookCopy.Barcode}].",
                ImageUrl = loanReq.BookCopy.Book.CoverImageUrl ?? "https://images.unsplash.com/photo-1512820790803-83ca734da794?w=500",
                CreatedAt = now
            });
            await _db.SaveChangesAsync();

            string ownerName = loanReq.BookCopy.OwnerUser?.FullName ?? "Chủ sách";
            int newBonus = loanReq.BookCopy.OwnerUser?.LendQuotaBonus ?? 1;
            return (true, $"Hai bên đã xác nhận giao nhận sách P2P [{loanReq.BookCopy.Barcode}] -> 'Borrowed'! Chủ sách {ownerName} được cộng thưởng +1 Hạn mức mượn (LendQuotaBonus = +{newBonus}) và +3 TrustScore.");
        }

        if (request.Stage == "Returned")
        {
            if (loanReq.Loan == null || loanReq.Loan.Status == "Returned")
            {
                return (false, "Hợp đồng P2P này chưa kích hoạt hoặc đã hoàn trả.");
            }

            loanReq.Loan.ReturnDate = now;
            loanReq.Loan.Status = "Returned";
            loanReq.BookCopy.Status = "Available";
            loanReq.Borrower.TrustScore += 2;

            _db.ConditionReports.Add(new ConditionReport
            {
                LoanId = loanReq.Loan.Id,
                ReportType = "Return",
                ReportedByUserId = currentUserId,
                ConditionGrade = loanReq.BookCopy.Condition,
                Note = $"Hoàn trả sách P2P [{loanReq.BookCopy.Barcode}] nguyên vẹn tại Điểm hẹn CLB.",
                ImageUrl = loanReq.BookCopy.Book.CoverImageUrl ?? "https://images.unsplash.com/photo-1512820790803-83ca734da794?w=500",
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            return (true, $"Đã xác nhận hoàn trả sách P2P [{loanReq.BookCopy.Barcode}] về tủ chủ sách ('Available')! Người mượn {loanReq.Borrower.FullName} được cộng +2 TrustScore.");
        }

        return (false, "Trạng thái chuyển tiếp P2P không hợp lệ.");
    }
}
