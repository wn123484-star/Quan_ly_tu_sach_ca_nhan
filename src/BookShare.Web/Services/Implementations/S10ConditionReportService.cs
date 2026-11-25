using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BookShare.Web.Services.Implementations;

public class S10ConditionReportService : IS10ConditionReportService
{
    private const long MaxConditionImageBytes = 5 * 1024 * 1024; // 5 MB

    private readonly BookShareDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly IAntiSpamRateLimiterService _rateLimiter;

    public S10ConditionReportService(
        BookShareDbContext db,
        IWebHostEnvironment env,
        IConfiguration config,
        IAntiSpamRateLimiterService rateLimiter)
    {
        _db = db;
        _env = env;
        _config = config;
        _rateLimiter = rateLimiter;
    }

    public async Task<S10ConditionReportPageViewModel> GetConditionReportsPageAsync(
        string currentUserId,
        int? selectedLoanId = null,
        string currentUserRole = "User")
    {
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return new S10ConditionReportPageViewModel
            {
                CurrentUserId = string.Empty,
                SelectedLoanId = null,
                TotalReportsCount = 0,
                DiscrepancyCount = 0,
                LoanComparisons = new List<S10LoanComparisonDto>()
            };
        }

        bool isPrivileged = currentUserRole is "Librarian" or "Curator" or "Admin";
        var loansQuery = _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.Borrower)
            .Include(l => l.ConditionReports)
                .ThenInclude(cr => cr.ReportedByUser)
            .AsQueryable();

        if (!isPrivileged)
        {
            loansQuery = loansQuery.Where(l =>
                l.LoanRequest.BorrowerId == currentUserId ||
                l.LoanRequest.BookCopy.OwnerUserId == currentUserId);
        }

        var loans = await loansQuery
            .OrderByDescending(l => l.Id)
            .ToListAsync();

        var comparisons = loans.Select(l =>
        {
            var handover = l.ConditionReports
                .Where(cr => cr.ReportType == "Handover")
                .OrderByDescending(cr => cr.CreatedAt)
                .FirstOrDefault();

            var returned = l.ConditionReports
                .Where(cr => cr.ReportType == "Return")
                .OrderByDescending(cr => cr.CreatedAt)
                .FirstOrDefault();

            bool damageDetected = returned != null &&
                (returned.ConditionGrade == "Damaged" ||
                 returned.ConditionGrade == "Worn" && handover != null && (handover.ConditionGrade == "New" || handover.ConditionGrade == "Good"));

            return new S10LoanComparisonDto
            {
                LoanId = l.Id,
                Barcode = l.LoanRequest.BookCopy.Barcode,
                BookTitle = l.LoanRequest.BookCopy.Book.Title,
                BorrowerName = l.LoanRequest.Borrower.FullName,
                LoanType = l.LoanRequest.LoanType,
                LoanStatus = l.Status,
                HasDamageDetected = damageDetected,
                HandoverReport = handover == null ? null : new S10ReportDetailDto
                {
                    ReportId = handover.Id,
                    ReportType = handover.ReportType,
                    ConditionGrade = handover.ConditionGrade,
                    Note = handover.Note,
                    ImageUrl = handover.ImageUrl,
                    ReportedByName = handover.ReportedByUser?.FullName ?? "Thủ thư / Chủ sách",
                    CreatedAt = handover.CreatedAt
                },
                ReturnReport = returned == null ? null : new S10ReportDetailDto
                {
                    ReportId = returned.Id,
                    ReportType = returned.ReportType,
                    ConditionGrade = returned.ConditionGrade,
                    Note = returned.Note,
                    ImageUrl = returned.ImageUrl,
                    ReportedByName = returned.ReportedByUser?.FullName ?? "Thủ thư / Chủ sách",
                    CreatedAt = returned.CreatedAt
                }
            };
        }).ToList();

        return new S10ConditionReportPageViewModel
        {
            CurrentUserId = currentUserId,
            SelectedLoanId = selectedLoanId ?? comparisons.FirstOrDefault()?.LoanId,
            TotalReportsCount = loans.Sum(l => l.ConditionReports.Count),
            DiscrepancyCount = comparisons.Count(c => c.HasDamageDetected),
            LoanComparisons = comparisons
        };
    }

    public async Task<(bool Success, string Message, string ImageUrl)> SaveConditionReportAsync(
        string currentUserId,
        string currentUserRole,
        int loanId,
        string reportType,
        string conditionGrade,
        string? note,
        IFormFile? uploadedFile,
        string? fallbackImageUrl)
    {
        var loan = await _db.Loans
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .FirstOrDefaultAsync(l => l.Id == loanId);

        if (loan == null)
        {
            return (false, "Không tìm thấy hợp đồng mượn sách để lập biên bản ảnh.", string.Empty);
        }

        bool isParticipant = loan.LoanRequest.BorrowerId == currentUserId ||
                             loan.LoanRequest.BookCopy.OwnerUserId == currentUserId;
        bool isPrivileged = currentUserRole is "Librarian" or "Curator" or "Admin";
        if (!isParticipant && !isPrivileged)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền lập biên bản tình trạng cho lượt mượn của người khác.");
        }

        var (allowed, retrySec, rlMsg) = _rateLimiter.TryConsume(
            "s10-upload-report",
            currentUserId,
            maxRequests: 12,
            window: TimeSpan.FromMinutes(5),
            customMessage: "Bạn đã gửi quá nhiều biên bản kiểm định trong 5 phút. Vui lòng đợi giây lát để chống spam.");
        if (!allowed)
        {
            throw new RateLimitExceededException(rlMsg, retrySec);
        }

        string cleanReportType = reportType == "Return" ? "Return" : "Handover";
        var allowedGrades = new[] { "New", "Good", "Fair", "Worn", "Damaged" };
        string cleanGrade = allowedGrades.Contains(conditionGrade) ? conditionGrade : "Good";
        string? trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote != null && trimmedNote.Length > 1000)
        {
            return (false, "Ghi chú tình trạng sách không được vượt quá 1.000 ký tự.", string.Empty);
        }

        string resolvedImageUrl = string.Empty;

        // 1. Nếu có upload file ảnh thật từ máy người dùng -> Kiểm tra dung lượng + chữ ký nhị phân (Magic Bytes) rồi lưu wwwroot/uploads/conditions/
        if (uploadedFile != null && uploadedFile.Length > 0)
        {
            if (uploadedFile.Length > MaxConditionImageBytes)
            {
                return (false, "Dung lượng ảnh chụp tình trạng sách không được vượt quá 5MB.", string.Empty);
            }

            var ext = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
            var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowedExts.Contains(ext))
            {
                return (false, "Chỉ chấp nhận tệp hình ảnh chụp tình trạng sách (.jpg, .png, .webp). Tuyệt đối không upload PDF/Ebook!", string.Empty);
            }

            byte[] fileBytes;
            await using (var ms = new MemoryStream())
            {
                await uploadedFile.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            bool isValidMagic =
                (fileBytes.Length >= 8 && fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47) || // PNG
                (fileBytes.Length >= 3 && fileBytes[0] == 0xFF && fileBytes[1] == 0xD8 && fileBytes[2] == 0xFF) || // JPEG
                (fileBytes.Length >= 12 && fileBytes[0] == 0x52 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46 && fileBytes[3] == 0x46 &&
                 fileBytes[8] == 0x57 && fileBytes[9] == 0x45 && fileBytes[10] == 0x42 && fileBytes[11] == 0x50); // WEBP
            if (!isValidMagic)
            {
                return (false, "Nội dung tệp tải lên không phải ảnh PNG/JPEG/WebP hợp lệ (bị chặn bởi lớp kiểm tra chữ ký tệp).", string.Empty);
            }

            var uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "conditions");
            Directory.CreateDirectory(uploadFolder);

            var fileName = $"loan-{loanId}-{cleanReportType.ToLower()}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}{ext}";
            var fullPath = Path.Combine(uploadFolder, fileName);
            await File.WriteAllBytesAsync(fullPath, fileBytes);

            resolvedImageUrl = $"/uploads/conditions/{fileName}";
        }
        else if (!string.IsNullOrWhiteSpace(fallbackImageUrl))
        {
            string cleanUrl = fallbackImageUrl.Trim();
            bool safeUrl = cleanUrl.StartsWith("/", StringComparison.Ordinal) && !cleanUrl.StartsWith("//", StringComparison.Ordinal)
                || cleanUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || cleanUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            resolvedImageUrl = safeUrl ? cleanUrl : "/images/book-placeholder.svg";
        }
        else
        {
            resolvedImageUrl = cleanGrade == "Damaged"
                ? "/images/book-placeholder.svg"
                : (loan.LoanRequest.BookCopy.Book.CoverImageUrl ?? "/images/book-placeholder.svg");
        }

        var report = new ConditionReport
        {
            LoanId = loanId,
            ReportType = cleanReportType,
            ReportedByUserId = currentUserId,
            ConditionGrade = cleanGrade,
            Note = string.IsNullOrWhiteSpace(note)
                ? $"Biên bản ảnh {(cleanReportType == "Handover" ? "Lúc Giao (Handover)" : "Lúc Trả (Return)")} cho bản sao [{loan.LoanRequest.BookCopy.Barcode}] - Tình trạng: {cleanGrade}."
                : note.Trim(),
            ImageUrl = resolvedImageUrl,
            CreatedAt = DateTime.UtcNow
        };

        _db.ConditionReports.Add(report);

        // Nếu biên bản lúc trả ghi nhận Damaged thì tự động cập nhật trạng thái bản sao
        if (cleanReportType == "Return" && cleanGrade == "Damaged")
        {
            loan.LoanRequest.BookCopy.Status = "Damaged";
            loan.LoanRequest.BookCopy.Condition = "Worn";
        }

        await _db.SaveChangesAsync();

                string stageVi = cleanReportType == "Handover" ? "Lúc Giao (Handover)" : "Lúc Trả (Return)";
                return (true, $"Đã lưu biên bản ảnh {stageVi} cho hợp đồng Loan #{loanId} ([{loan.LoanRequest.BookCopy.Barcode}]) - Phân loại: {cleanGrade}!", resolvedImageUrl);
            }

            public async Task<(bool Success, string Message)> UpdateConditionReportAsync(
                string currentUserId,
                string currentUserRole,
                UpdateConditionReportRequestDto request)
            {
                var report = await _db.ConditionReports
                    .Include(cr => cr.Loan)
                        .ThenInclude(l => l.LoanRequest)
                            .ThenInclude(lr => lr.BookCopy)
                    .FirstOrDefaultAsync(cr => cr.Id == request.ReportId);

                if (report == null)
                {
                    return (false, "Không tìm thấy biên bản kiểm định tình trạng sách cần cập nhật.");
                }

                bool isPrivileged = currentUserRole == "Librarian" || currentUserRole == "Admin" || currentUserRole == "Curator";
                if (report.ReportedByUserId != currentUserId && !isPrivileged)
                {
                    throw new UnauthorizedAccessException("Bạn không có quyền chỉnh sửa biên bản kiểm định do người khác lập.");
                }

                var allowedGrades = new[] { "New", "Good", "Fair", "Worn", "Damaged" };
                if (allowedGrades.Contains(request.ConditionGrade))
                {
                    report.ConditionGrade = request.ConditionGrade;
                }

                if (request.Note != null)
                {
                    report.Note = request.Note.Trim();
                }

                if (report.ReportType == "Return" && report.Loan?.LoanRequest?.BookCopy != null)
                {
                    if (report.ConditionGrade == "Damaged")
                    {
                        report.Loan.LoanRequest.BookCopy.Status = "Damaged";
                        report.Loan.LoanRequest.BookCopy.Condition = "Worn";
                    }
                    else
                    {
                        report.Loan.LoanRequest.BookCopy.Condition = report.ConditionGrade;
                    }
                }

                await _db.SaveChangesAsync();
                return (true, $"Đã cập nhật biên bản kiểm định #{report.Id} sang độ mới [{report.ConditionGrade}]!");
            }

            public async Task<(bool Success, string Message)> DeleteConditionReportAsync(
                string currentUserId,
                string currentUserRole,
                int reportId)
            {
                var report = await _db.ConditionReports.FirstOrDefaultAsync(cr => cr.Id == reportId);
                if (report == null)
                {
                    return (false, "Không tìm thấy biên bản kiểm định cần xóa.");
                }

                bool isPrivileged = currentUserRole == "Librarian" || currentUserRole == "Admin";
                if (report.ReportedByUserId != currentUserId && !isPrivileged)
                {
                    throw new UnauthorizedAccessException("Chỉ người lập biên bản hoặc Thủ thư / Quản trị viên mới có quyền xóa biên bản kiểm định.");
                }

                _db.ConditionReports.Remove(report);
                await _db.SaveChangesAsync();
                return (true, $"Đã xóa biên bản kiểm định #{reportId} khỏi hệ thống!");
            }
        }
