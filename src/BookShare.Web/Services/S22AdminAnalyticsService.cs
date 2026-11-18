// =========================================================================================
// DỰ ÁN: BOOKSHARE - NỀN TẢNG QUẢN LÝ TỦ SÁCH CÁ NHÂN & MƯỢN TRẢ SÁCH CỘNG ĐỒNG (.NET 10)
// FILE: src/BookShare.Web/Services/S22AdminAnalyticsService.cs
// MÔ TẢ: Service Dịch vụ S22 - Admin Analytics Dashboard
//        - Truy vấn tổng hợp thống kê bằng GROUP BY / SUM / COUNT / AVG
//        - Trả về dữ liệu cho Chart.js vẽ Doughnut + Bar chart
//        - Các SP được gọi:
//          + sp_GetAdminAnalyticsOverview (KPI cards)
//          + sp_GetLoanTypeRatio (Doughnut: Library vs P2P)
//          + sp_GetMonthlyRevenue (Bar: Membership + Fine revenue)
//          + sp_GetTopBooksByLoans (Bar: top sách mượn nhiều)
//          + sp_GetCategoryStats (Pie: phân bố thể loại)
//          + sp_GetLibraryActivityStats (Bar: hoạt động theo Hub)
//          + sp_GetOverdueLoansReport (Bảng: danh sách quá hạn + fine)
//
// TÁC GIẢ: Lê Hoàn Lâm - MSSV: BIT250210 - Lớp 25IT - Nhóm 5
// NHÁNH:   sprint2/lam-database-query
// =========================================================================================

using BookShare.Web.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace BookShare.Web.Services;

/// <summary>DTO KPI cards trên Dashboard Admin.</summary>
public class AdminOverviewDto
{
    public int TotalUsers { get; set; }
    public int NewUsers30Days { get; set; }
    public int TotalBooks { get; set; }
    public int TotalBookCopies { get; set; }
    public int AvailableCopies { get; set; }
    public int BorrowedCopies { get; set; }
    public int LostCopies { get; set; }
    public int ActiveLoans { get; set; }
    public int OverdueLoans { get; set; }
    public int PendingRequests { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalFines { get; set; }
    public int ActivePremiumMembers { get; set; }
    public int PublicReviews { get; set; }
    public int OpenDisputes { get; set; }
}

/// <summary>DTO tỷ lệ mượn Library vs P2P (Doughnut chart).</summary>
public class LoanTypeRatioDto
{
    public string LoanType { get; set; } = string.Empty;  // Library / P2P
    public int Count { get; set; }
    public decimal Percent { get; set; }
    public int TotalLoans { get; set; }
}

/// <summary>DTO doanh thu theo tháng (Bar chart).</summary>
public class MonthlyRevenueDto
{
    public string Month { get; set; } = string.Empty;  // yyyy-MM
    public decimal MembershipRevenue { get; set; }
    public decimal FineRevenue { get; set; }
    public decimal TotalRevenue { get; set; }
}

/// <summary>DTO top sách mượn nhiều (Bar chart).</summary>
public class TopBookDto
{
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public int TotalLoans { get; set; }
    public int TotalCopies { get; set; }
    public double AvgRating { get; set; }
}

/// <summary>DTO thống kê theo thể loại (Pie chart).</summary>
public class CategoryStatDto
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int TotalBooks { get; set; }
    public int TotalCopies { get; set; }
    public int TotalLoans { get; set; }
    public double AvgRating { get; set; }
}

/// <summary>DTO thống kê hoạt động theo Hub (Bar chart).</summary>
public class LibraryActivityDto
{
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public string HubType { get; set; } = string.Empty;
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public int BorrowedCopies { get; set; }
    public int PickupRequests30Days { get; set; }
}

/// <summary>DTO danh sách loan quá hạn (Table).</summary>
public class OverdueLoanDto
{
    public int LoanId { get; set; }
    public string BorrowerId { get; set; } = string.Empty;
    public string BorrowerFullName { get; set; } = string.Empty;
    public string BorrowerEmail { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime DueDate { get; set; }
    public int DaysOverdue { get; set; }
    public decimal FineAmount { get; set; }
    public decimal EstimatedFine { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>DTO thống kê Reading Tracker của 1 user (cho S03).</summary>
public class UserReadingStatsDto
{
    public int TotalBooks { get; set; }
    public int WantToReadCount { get; set; }
    public int ReadingCount { get; set; }
    public int CompletedCount { get; set; }
    public int TotalPagesRead { get; set; }
    public double AvgPersonalRating { get; set; }
    public int HighPriorityCount { get; set; }
}

public class ReadingStatusBreakdownDto
{
    public string StatusName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ReadingProgressItemDto
{
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public decimal ProgressPercent { get; set; }
    public string Priority { get; set; } = string.Empty;
    public DateTime? TargetDeadline { get; set; }
}

public class ReadingTrackerStatsDto
{
    public UserReadingStatsDto Kpi { get; set; } = new();
    public List<ReadingStatusBreakdownDto> StatusBreakdown { get; set; } = new();
    public List<ReadingProgressItemDto> TopReading { get; set; } = new();
    public List<MonthlyCompletedDto> MonthlyCompleted { get; set; } = new();
}

public class MonthlyCompletedDto
{
    public string Month { get; set; } = string.Empty;
    public int CompletedCount { get; set; }
}

public interface IS22AdminAnalyticsService
{
    Task<AdminOverviewDto> GetOverviewAsync(CancellationToken ct = default);
    Task<List<LoanTypeRatioDto>> GetLoanTypeRatioAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<List<MonthlyRevenueDto>> GetMonthlyRevenueAsync(int monthsBack = 12, CancellationToken ct = default);
    Task<List<TopBookDto>> GetTopBooksByLoansAsync(int top = 10, int monthsBack = 6, CancellationToken ct = default);
    Task<List<CategoryStatDto>> GetCategoryStatsAsync(CancellationToken ct = default);
    Task<List<LibraryActivityDto>> GetLibraryActivityStatsAsync(CancellationToken ct = default);
    Task<List<OverdueLoanDto>> GetOverdueLoansReportAsync(CancellationToken ct = default);
    Task<ReadingTrackerStatsDto> GetUserReadingStatsAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// Triển khai S22 Admin Analytics bằng cách gọi stored procedures.
/// </summary>
public class S22AdminAnalyticsService : IS22AdminAnalyticsService
{
    private readonly string _connectionString;
    private readonly ILogger<S22AdminAnalyticsService> _logger;

    public S22AdminAnalyticsService(
        IConfiguration configuration,
        ILogger<S22AdminAnalyticsService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection chưa cấu hình");
        _logger = logger;
    }

    public async Task<AdminOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.sp_GetAdminAnalyticsOverview", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return new AdminOverviewDto
                {
                    TotalUsers = reader.GetInt32(reader.GetOrdinal("TotalUsers")),
                    NewUsers30Days = reader.GetInt32(reader.GetOrdinal("NewUsers30Days")),
                    TotalBooks = reader.GetInt32(reader.GetOrdinal("TotalBooks")),
                    TotalBookCopies = reader.GetInt32(reader.GetOrdinal("TotalBookCopies")),
                    AvailableCopies = reader.GetInt32(reader.GetOrdinal("AvailableCopies")),
                    BorrowedCopies = reader.GetInt32(reader.GetOrdinal("BorrowedCopies")),
                    LostCopies = reader.GetInt32(reader.GetOrdinal("LostCopies")),
                    ActiveLoans = reader.GetInt32(reader.GetOrdinal("ActiveLoans")),
                    OverdueLoans = reader.GetInt32(reader.GetOrdinal("OverdueLoans")),
                    PendingRequests = reader.GetInt32(reader.GetOrdinal("PendingRequests")),
                    TotalRevenue = reader.GetDecimal(reader.GetOrdinal("TotalRevenue")),
                    TotalFines = reader.GetDecimal(reader.GetOrdinal("TotalFines")),
                    ActivePremiumMembers = reader.GetInt32(reader.GetOrdinal("ActivePremiumMembers")),
                    PublicReviews = reader.GetInt32(reader.GetOrdinal("PublicReviews")),
                    OpenDisputes = reader.GetInt32(reader.GetOrdinal("OpenDisputes"))
                };
            }
            return new AdminOverviewDto();
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_GetAdminAnalyticsOverview");
            return new AdminOverviewDto();
        }
    }

    public async Task<List<LoanTypeRatioDto>> GetLoanTypeRatioAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.sp_GetLoanTypeRatio", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@FromDate", (object?)from ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToDate", (object?)to ?? DBNull.Value);

            var result = new List<LoanTypeRatioDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new LoanTypeRatioDto
                {
                    LoanType = reader.GetString(reader.GetOrdinal("LoanType")),
                    Count = reader.GetInt32(reader.GetOrdinal("Count")),
                    Percent = reader.GetDecimal(reader.GetOrdinal("Percent")),
                    TotalLoans = reader.GetInt32(reader.GetOrdinal("TotalLoans"))
                });
            }
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_GetLoanTypeRatio");
            return new List<LoanTypeRatioDto>();
        }
    }

    public async Task<List<MonthlyRevenueDto>> GetMonthlyRevenueAsync(int monthsBack = 12, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.sp_GetMonthlyRevenue", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@MonthsBack", monthsBack);

            var result = new List<MonthlyRevenueDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new MonthlyRevenueDto
                {
                    Month = reader.GetString(reader.GetOrdinal("Month")),
                    MembershipRevenue = reader.GetDecimal(reader.GetOrdinal("MembershipRevenue")),
                    FineRevenue = reader.GetDecimal(reader.GetOrdinal("FineRevenue")),
                    TotalRevenue = reader.GetDecimal(reader.GetOrdinal("TotalRevenue"))
                });
            }
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_GetMonthlyRevenue");
            return new List<MonthlyRevenueDto>();
        }
    }

    public async Task<List<TopBookDto>> GetTopBooksByLoansAsync(int top = 10, int monthsBack = 6, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.sp_GetTopBooksByLoans", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@Top", top);
            cmd.Parameters.AddWithValue("@MonthsBack", monthsBack);

            var result = new List<TopBookDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new TopBookDto
                {
                    BookId = reader.GetInt32(reader.GetOrdinal("BookId")),
                    Title = reader.GetString(reader.GetOrdinal("Title")),
                    Author = reader.GetString(reader.GetOrdinal("Author")),
                    CoverImageUrl = reader.IsDBNull(reader.GetOrdinal("CoverImageUrl")) ? null : reader.GetString(reader.GetOrdinal("CoverImageUrl")),
                    TotalLoans = reader.GetInt32(reader.GetOrdinal("TotalLoans")),
                    TotalCopies = reader.GetInt32(reader.GetOrdinal("TotalCopies")),
                    AvgRating = reader.IsDBNull(reader.GetOrdinal("AvgRating")) ? 0 : Convert.ToDouble(reader["AvgRating"])
                });
            }
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_GetTopBooksByLoans");
            return new List<TopBookDto>();
        }
    }

    public async Task<List<CategoryStatDto>> GetCategoryStatsAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.sp_GetCategoryStats", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            var result = new List<CategoryStatDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new CategoryStatDto
                {
                    CategoryId = reader.GetInt32(reader.GetOrdinal("CategoryId")),
                    CategoryName = reader.GetString(reader.GetOrdinal("CategoryName")),
                    Slug = reader.GetString(reader.GetOrdinal("Slug")),
                    TotalBooks = reader.GetInt32(reader.GetOrdinal("TotalBooks")),
                    TotalCopies = reader.GetInt32(reader.GetOrdinal("TotalCopies")),
                    TotalLoans = reader.GetInt32(reader.GetOrdinal("TotalLoans")),
                    AvgRating = reader.IsDBNull(reader.GetOrdinal("AvgRating")) ? 0 : Convert.ToDouble(reader["AvgRating"])
                });
            }
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_GetCategoryStats");
            return new List<CategoryStatDto>();
        }
    }

    public async Task<List<LibraryActivityDto>> GetLibraryActivityStatsAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.sp_GetLibraryActivityStats", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            var result = new List<LibraryActivityDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new LibraryActivityDto
                {
                    LibraryId = reader.GetInt32(reader.GetOrdinal("LibraryId")),
                    LibraryName = reader.GetString(reader.GetOrdinal("LibraryName")),
                    HubType = reader.GetString(reader.GetOrdinal("HubType")),
                    TotalCopies = reader.GetInt32(reader.GetOrdinal("TotalCopies")),
                    AvailableCopies = reader.GetInt32(reader.GetOrdinal("AvailableCopies")),
                    BorrowedCopies = reader.GetInt32(reader.GetOrdinal("BorrowedCopies")),
                    PickupRequests30Days = reader.GetInt32(reader.GetOrdinal("PickupRequests30Days"))
                });
            }
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_GetLibraryActivityStats");
            return new List<LibraryActivityDto>();
        }
    }

    public async Task<List<OverdueLoanDto>> GetOverdueLoansReportAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.sp_GetOverdueLoansReport", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            var result = new List<OverdueLoanDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new OverdueLoanDto
                {
                    LoanId = reader.GetInt32(reader.GetOrdinal("LoanId")),
                    BorrowerId = reader.GetString(reader.GetOrdinal("BorrowerId")),
                    BorrowerFullName = reader.GetString(reader.GetOrdinal("BorrowerFullName")),
                    BorrowerEmail = reader.GetString(reader.GetOrdinal("BorrowerEmail")),
                    Barcode = reader.GetString(reader.GetOrdinal("Barcode")),
                    BookTitle = reader.GetString(reader.GetOrdinal("BookTitle")),
                    Author = reader.GetString(reader.GetOrdinal("Author")),
                    StartDate = reader.GetDateTime(reader.GetOrdinal("StartDate")),
                    DueDate = reader.GetDateTime(reader.GetOrdinal("DueDate")),
                    DaysOverdue = reader.GetInt32(reader.GetOrdinal("DaysOverdue")),
                    FineAmount = reader.GetDecimal(reader.GetOrdinal("FineAmount")),
                    EstimatedFine = reader.GetDecimal(reader.GetOrdinal("EstimatedFine")),
                    Status = reader.GetString(reader.GetOrdinal("Status"))
                });
            }
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_GetOverdueLoansReport");
            return new List<OverdueLoanDto>();
        }
    }

    public async Task<ReadingTrackerStatsDto> GetUserReadingStatsAsync(string userId, CancellationToken ct = default)
    {
        var result = new ReadingTrackerStatsDto();
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("dbo.sp_GetReadingTrackerStats", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@UserId", userId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            // Resultset 1: KPI
            if (await reader.ReadAsync(ct))
            {
                result.Kpi = new UserReadingStatsDto
                {
                    TotalBooks = reader.GetInt32(reader.GetOrdinal("TotalBooks")),
                    WantToReadCount = reader.GetInt32(reader.GetOrdinal("WantToReadCount")),
                    ReadingCount = reader.GetInt32(reader.GetOrdinal("ReadingCount")),
                    CompletedCount = reader.GetInt32(reader.GetOrdinal("CompletedCount")),
                    TotalPagesRead = reader.GetInt32(reader.GetOrdinal("TotalPagesRead")),
                    AvgPersonalRating = reader.IsDBNull(reader.GetOrdinal("AvgPersonalRating")) ? 0 : Convert.ToDouble(reader["AvgPersonalRating"]),
                    HighPriorityCount = reader.GetInt32(reader.GetOrdinal("HighPriorityCount"))
                };
            }

            // Resultset 2: Status breakdown
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.StatusBreakdown.Add(new ReadingStatusBreakdownDto
                {
                    StatusName = reader.GetString(reader.GetOrdinal("StatusName")),
                    Count = reader.GetInt32(reader.GetOrdinal("Count"))
                });
            }

            // Resultset 3: Top reading
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.TopReading.Add(new ReadingProgressItemDto
                {
                    BookId = reader.GetInt32(reader.GetOrdinal("BookId")),
                    Title = reader.GetString(reader.GetOrdinal("Title")),
                    Author = reader.GetString(reader.GetOrdinal("Author")),
                    CoverImageUrl = reader.IsDBNull(reader.GetOrdinal("CoverImageUrl")) ? null : reader.GetString(reader.GetOrdinal("CoverImageUrl")),
                    CurrentPage = reader.GetInt32(reader.GetOrdinal("CurrentPage")),
                    TotalPages = reader.GetInt32(reader.GetOrdinal("TotalPages")),
                    ProgressPercent = reader.GetDecimal(reader.GetOrdinal("ProgressPercent")),
                    Priority = reader.GetString(reader.GetOrdinal("Priority")),
                    TargetDeadline = reader.IsDBNull(reader.GetOrdinal("TargetDeadline")) ? null : reader.GetDateTime(reader.GetOrdinal("TargetDeadline"))
                });
            }

            // Resultset 4: Monthly completed
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.MonthlyCompleted.Add(new MonthlyCompletedDto
                {
                    Month = reader.GetString(reader.GetOrdinal("Month")),
                    CompletedCount = reader.GetInt32(reader.GetOrdinal("CompletedCount"))
                });
            }

            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_GetReadingTrackerStats (UserId={UserId})", userId);
            return result;
        }
    }
}
