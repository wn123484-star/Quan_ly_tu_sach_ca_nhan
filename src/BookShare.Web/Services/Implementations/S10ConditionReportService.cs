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
    private readonly BookShareDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public S10ConditionReportService(BookShareDbContext db, IWebHostEnvironment env, IConfiguration config)
    {
        _db = db;
        _env = env;
        _config = config;
    }

    public async Task<S10ConditionReportPageViewModel> GetConditionReportsPageAsync(string currentUserId, int? selectedLoanId = null)
    {
        var loans = await _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.BookCopy)
                    .ThenInclude(bc => bc.Book)
            .Include(l => l.LoanRequest)
                .ThenInclude(lr => lr.Borrower)
            .Include(l => l.ConditionReports)
                .ThenInclude(cr => cr.ReportedByUser)
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

        string cleanReportType = reportType == "Return" ? "Return" : "Handover";
        var allowedGrades = new[] { "New", "Good", "Fair", "Worn", "Damaged" };
        string cleanGrade = allowedGrades.Contains(conditionGrade) ? conditionGrade : "Good";

        string resolvedImageUrl = string.Empty;

        // 1. Nếu có upload file ảnh thật từ máy người dùng -> Lưu vào wwwroot/uploads/conditions/ (hoặc Cloudinary)
        if (uploadedFile != null && uploadedFile.Length > 0)
        {
            var ext = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
            var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowedExts.Contains(ext))
            {
                return (false, "Chỉ chấp nhận tệp hình ảnh chụp tình trạng sách (.jpg, .png, .webp). Tuyệt đối không upload PDF/Ebook!", string.Empty);
            }

            var uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "conditions");
            Directory.CreateDirectory(uploadFolder);

            var fileName = $"loan-{loanId}-{cleanReportType.ToLower()}-{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
            var fullPath = Path.Combine(uploadFolder, fileName);

            await using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await uploadedFile.CopyToAsync(stream);
            }

            resolvedImageUrl = $"/uploads/conditions/{fileName}";
        }
        else if (!string.IsNullOrWhiteSpace(fallbackImageUrl))
        {
            resolvedImageUrl = fallbackImageUrl.Trim();
        }
        else
        {
            resolvedImageUrl = cleanGrade == "Damaged"
                ? "https://images.unsplash.com/photo-1589829085413-56de8ae18c73?w=600"
                : (loan.LoanRequest.BookCopy.Book.CoverImageUrl ?? "https://images.unsplash.com/photo-1544947950-fa07a98d237f?w=600");
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
}
