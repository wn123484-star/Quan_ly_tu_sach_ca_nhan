using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S21LibrarianDeskService : IS21LibrarianDeskService
{
    private readonly BookShareDbContext _db;
    private const decimal DailyFineRateVnd = 5000m;

    public S21LibrarianDeskService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S21LibrarianDeskPageViewModel> GetLibrarianDeskPageAsync(
        string currentUserId,
        string currentUserRole)
    {
        var now = DateTime.UtcNow;

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

        var systemCopies = await _db.BookCopies
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

        var copyDtos = systemCopies.Select(bc =>
        {
            var activeReq = bc.LoanRequests
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefault(r => r.Loan != null && (r.Loan.Status == "Active" || r.Loan.Status == "Overdue"));

            var pendingReq = bc.LoanRequests
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefault(r => r.Status == "Pending" || r.Status == "Approved");

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
            TotalSystemCopies = copyDtos.Count,
            AvailableSystemCopies = copyDtos.Count(c => c.Status == "Available"),
            BorrowedSystemCopies = copyDtos.Count(c => c.Status == "Borrowed" || c.Status == "Reserved"),
            DamagedOrLostCopies = copyDtos.Count(c => c.Status == "Damaged" || c.Status == "Lost"),
            OverdueLoansCount = overdueDtos.Count,
            Books = books,
            Libraries = libraries,
            SystemCopies = copyDtos,
            OverdueLoans = overdueDtos
        };
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
                    Note = $"Quét Barcode [{cleanBarcode}] thu hồi nhanh tại Bàn Thủ thư S21.",
                    ImageUrl = "https://images.unsplash.com/photo-1544716278-ca5e3f4abd8c?w=600",
                    CreatedAt = now
                });
            }

            // Kiểm tra hàng chờ S11
            var nextQueue = copy.Book.Reservations
                .Where(r => r.Status == "Waiting")
                .OrderBy(r => r.QueuePosition)
                .FirstOrDefault();

            if (nextQueue != null)
            {
                copy.Status = "Reserved";
                nextQueue.Status = "Notified";
                nextQueue.NotifiedAt = now;
                nextQueue.ExpiresAt = now.AddHours(48);
            }
            else
            {
                copy.Status = "Available";
            }

            await _db.SaveChangesAsync();
            return (true,
                $"Đã quét Barcode [{cleanBarcode}] (\"{copy.Book.Title}\") — Xác nhận THU HỒI SÁCH TRẢ tại quầy thành công! Trạng thái bản sao hiện tại: {copy.Status}.",
                "Checkin");
        }

        // Luồng Checkout (Xuất kho bàn giao sách tại quầy)
        string borrowerId = string.IsNullOrWhiteSpace(request.BorrowerUserId) ? "u-user-01" : request.BorrowerUserId;
        var pendingReq = copy.LoanRequests
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefault(r => r.Status == "Pending" || r.Status == "Approved");

        if (pendingReq == null)
        {
            pendingReq = new LoanRequest
            {
                BookCopyId = copy.Id,
                BorrowerId = borrowerId,
                ApproverId = librarianUserId,
                LoanType = "Library",
                Status = "Approved",
                RequestedAt = now,
                DesiredDays = 14,
                PickupHubId = copy.LibraryId ?? 1,
                RequestNote = $"Quét Barcode [{cleanBarcode}] duyệt xuất kho trực tiếp tại Bàn Thủ thư S21."
            };
            _db.LoanRequests.Add(pendingReq);
            await _db.SaveChangesAsync();
        }
        else
        {
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
                DueDate = now.AddDays(14),
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
                Note = $"Quét Barcode [{cleanBarcode}] bàn giao sách tại quầy Thủ thư S21.",
                ImageUrl = "https://images.unsplash.com/photo-1532012197267-da84d127e765?w=600",
                CreatedAt = now
            });
        }

        copy.Status = "Borrowed";
        await _db.SaveChangesAsync();

        return (true,
            $"Đã quét Barcode [{cleanBarcode}] (\"{copy.Book.Title}\") — Xác nhận XUẤT KHO GIAO SÁCH thành công (Hạn trả: {now.AddDays(14):dd/MM/yyyy})!",
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
                LibraryId = request.LibraryId > 0 ? request.LibraryId : 1,
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
            .FirstOrDefaultAsync(bc => bc.Id == request.BookCopyId);

        if (copy == null)
        {
            return (false, "Không tìm thấy bản sao sách cần chuyển trạng thái.");
        }

        var allowedStatuses = new[] { "Available", "Damaged", "Lost" };
        string targetStatus = allowedStatuses.Contains(request.NewStatus) ? request.NewStatus : "Damaged";

        var allowedConds = new[] { "New", "Good", "Fair", "Worn" };
        string targetCond = allowedConds.Contains(request.NewCondition) ? request.NewCondition : "Worn";

        copy.Status = targetStatus;
        copy.Condition = targetCond;
        copy.IsLendable = targetStatus == "Available";

        await _db.SaveChangesAsync();

        return (true,
            $"Đã cập nhật trạng thái bản sao [{copy.Barcode}] (\"{copy.Book.Title}\") sang Status = '{copy.Status}', Condition = '{copy.Condition}', IsLendable = {copy.IsLendable}.");
    }
}
