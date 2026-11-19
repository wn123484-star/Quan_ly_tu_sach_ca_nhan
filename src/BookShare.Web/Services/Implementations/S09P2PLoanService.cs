using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S09P2PLoanService : IS09P2PLoanService
{
    private readonly BookShareDbContext _db;
    private readonly IWalletService _walletService;

    public S09P2PLoanService(BookShareDbContext db, IWalletService walletService)
    {
        _db = db;
        _walletService = walletService;
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
                OwnerTrustScore = bc.OwnerUser != null ? bc.OwnerUser.TrustScore : 100,
                DailyRentalFee = bc.DailyRentalFee,
                DepositAmount = bc.DepositAmount
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
            BookId = lr.BookCopy.BookId,
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
            RequestNote = lr.RequestNote,
            DailyRentalFee = lr.BookCopy.DailyRentalFee,
            DepositAmount = lr.BookCopy.DepositAmount,
            RentalFeeTotal = lr.Loan?.RentalFeeTotal ?? (lr.BookCopy.DailyRentalFee * lr.DesiredDays),
            DepositHeld = lr.Loan?.DepositHeld ?? lr.BookCopy.DepositAmount,
            EscrowStatus = lr.Loan?.EscrowStatus ?? "None"
        }).ToList();

        return new S09P2PLoanPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserName = currentUser?.FullName ?? "Độc giả BookShare",
            CurrentUserTrustScore = currentUser?.TrustScore ?? 100,
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

        if (copy.OwnerUserId == currentUserId)
        {
            return (false, "Bạn không thể tự tạo yêu cầu mượn sách thuộc tủ sách cá nhân của chính mình.");
        }

        // [FEAT-LOGIC] Luật chặn mượn thông minh P2P: TrustScore < 50, đang có hợp đồng quá hạn (Overdue) hoặc tranh chấp mở (Disputed)
        var borrower = await _db.AspNetUsers
            .Include(u => u.Memberships)
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        if (borrower != null && borrower.TrustScore < 50)
        {
            return (false, $"Tạm khóa quyền mượn P2P mới: Điểm uy tín TrustScore của bạn hiện là {borrower.TrustScore}/100 (yêu cầu tối thiểu 50 điểm để bảo vệ chủ sách cộng đồng).");
        }

        var nowUtc = DateTime.UtcNow;
        int overdueCount = await _db.Loans.CountAsync(l =>
            l.LoanRequest.BorrowerId == currentUserId &&
            (l.Status == "Overdue" || (l.Status == "Active" && l.DueDate < nowUtc)));

        if (overdueCount > 0)
        {
            return (false, $"Tạm khóa quyền mượn P2P mới: Bạn đang có {overdueCount} hợp đồng mượn quá hạn (Overdue) chưa hoàn trả. Vui lòng hoàn trả sách đúng hạn trước khi gửi yêu cầu mượn mới.");
        }

        int openDisputeCount = await _db.Disputes.CountAsync(d =>
            d.Loan.LoanRequest.BorrowerId == currentUserId &&
            (d.Status == "Open" || d.Status == "UnderReview"));
        bool hasDisputedLoan = await _db.Loans.AnyAsync(l =>
            l.LoanRequest.BorrowerId == currentUserId && l.Status == "Disputed");

        if (openDisputeCount > 0 || hasDisputedLoan)
        {
            return (false, "Tạm khóa quyền mượn P2P mới: Tài khoản của bạn đang có hồ sơ tranh chấp / khiếu nại (Disputed) chưa được giải quyết xong. Vui lòng hoàn tất giải quyết tranh chấp trước khi mượn sách mới.");
        }

        int maxQuota = (borrower?.Memberships.Where(m => m.IsActive).Select(m => m.MaxConcurrentLoans).DefaultIfEmpty(2).Max() ?? 2)
                       + (borrower?.LendQuotaBonus ?? 0);
        int currentActive = await _db.Loans
            .CountAsync(l => l.LoanRequest.BorrowerId == currentUserId && l.Status == "Active");
        if (currentActive >= maxQuota)
        {
            return (false, $"Bạn đang mượn {currentActive}/{maxQuota} cuốn (đạt trần hạn mức hiện tại). Vui lòng hoàn trả sách hoặc cho mượn sách P2P để nhận thêm +1 hạn mức.");
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

            // [BS-RENTAL-FLOW] Khóa bảo lãnh Escrow (Phí thuê + Tiền cọc) từ Ví người thuê trước khi tạo hợp đồng P2P
            decimal expectedRentalFee = loanReq.BookCopy.DailyRentalFee * loanReq.DesiredDays;
            decimal expectedDeposit = loanReq.BookCopy.DepositAmount;
            decimal totalEscrowNeeded = expectedRentalFee + expectedDeposit;
            if (totalEscrowNeeded > 0m)
            {
                var borrowerWallet = await _walletService.EnsureWalletAsync(loanReq.BorrowerId);
                if (borrowerWallet.Balance < totalEscrowNeeded)
                {
                    return (false, $"Số dư Ví người thuê ({borrowerWallet.Balance:N0} VNĐ) chưa đủ tạm giữ Escrow {totalEscrowNeeded:N0} VNĐ (Phí thuê {expectedRentalFee:N0}đ + Cọc {expectedDeposit:N0}đ). Vui lòng nạp thêm tại Ví BookShare Pay!");
                }
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
                RentalFeeTotal = expectedRentalFee,
                DepositHeld = expectedDeposit,
                EscrowStatus = "None",
                Status = "Active"
            };
            _db.Loans.Add(newLoan);

            // Thưởng hạn mức mượn (+1 LendQuotaBonus) và +3 TrustScore cho chủ sách khi cho thuê/mượn P2P thành công!
            if (loanReq.BookCopy.OwnerUser != null)
            {
                loanReq.BookCopy.OwnerUser.LendQuotaBonus += 1;
                loanReq.BookCopy.OwnerUser.TrustScore += 3;
            }

            await _db.SaveChangesAsync();

            var (_, escrowMsg) = await _walletService.HoldEscrowForLoanAsync(
                loanReq.BorrowerId,
                newLoan,
                expectedRentalFee,
                expectedDeposit);

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
            return (true, $"Hai bên đã xác nhận giao nhận sách P2P [{loanReq.BookCopy.Barcode}] -> 'Borrowed'! Chủ sách {ownerName} nhận +1 Hạn mức (LendQuotaBonus = +{newBonus}) và +3 TrustScore. {escrowMsg}");
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

            // [BS-RENTAL-FLOW] Hoàn 100% cọc Escrow cho người thuê & Giải ngân Phí thuê về Ví chủ sách P2P!
            var (_, settleMsg) = await _walletService.SettleEscrowOnReturnAsync(
                loanReq.Loan,
                loanReq.BookCopy.OwnerUserId,
                isDamaged: false);

            return (true, $"Đã xác nhận hoàn trả sách P2P [{loanReq.BookCopy.Barcode}] về tủ chủ sách ('Available')! Người thuê {loanReq.Borrower.FullName} được cộng +2 TrustScore.{settleMsg}");
        }

        return (false, "Trạng thái chuyển tiếp P2P không hợp lệ.");
    }
}
