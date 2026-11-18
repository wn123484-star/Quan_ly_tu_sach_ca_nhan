// =========================================================================================
// DỰ ÁN: BOOKSHARE - NỀN TẢNG QUẢN LÝ TỦ SÁCH CÁ NHÂN & MƯỢN TRẢ SÁCH CỘNG ĐỒNG (.NET 10)
// FILE: src/BookShare.Web/Services/S06SearchDiscoveryService.cs
// MÔ TẢ: Service Dịch vụ S06 - Tìm kiếm & Khám phá sách
//        - Tìm theo keyword (Title/Author) + ISBN chính xác
//        - Lọc theo Category, PublishedYear, OwnerType, OnlyAvailable
//        - Sắp xếp: Title/Author/Year/Rating/Newest
//        - Phân trang Server-side bằng stored procedure sp_SearchBooks / sp_AdvancedFilterBooks
//        - Trả về PagedResult<BookSearchDto> với đầy đủ TotalItems / CurrentPage / TotalPages
//
// TÁC GIẢ: Lê Hoàn Lâm - MSSV: BIT250210 - Lớp 25IT - Nhóm 5
// NHÁNH:   sprint2/lam-database-query
// =========================================================================================

using BookShare.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace BookShare.Web.Services;

/// <summary>
/// DTO kết quả tìm kiếm đầu sách (mapping thủ công từ SqlDataReader).
/// </summary>
public class BookSearchDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public int? PublishedYear { get; set; }
    public int PageCount { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string? Summary { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AvailableCopyCount { get; set; }
    public double AverageRating { get; set; }
}

/// <summary>
/// Dịch vụ S06 - Search Discovery: tìm kiếm + lọc + phân trang đầu sách.
/// Triển khai bằng cách gọi stored procedure sp_SearchBooks / sp_AdvancedFilterBooks
/// qua ADO.NET (SqlConnection + SqlCommand) để tận dụng index và covering index.
/// </summary>
public interface IS06SearchDiscoveryService
{
    /// <summary>Tìm kiếm cơ bản theo keyword + ISBN + Category.</summary>
    Task<PagedResult<BookSearchDto>> SearchBooksAsync(
        string? keyword, string? isbn, int? categoryId, int? publishedYear,
        int page, int pageSize, CancellationToken ct = default);

    /// <summary>Tìm kiếm nâng cao với sort + OnlyAvailable + OwnerType filter.</summary>
    Task<PagedResult<BookSearchDto>> AdvancedFilterAsync(
        BookSearchRequest request, CancellationToken ct = default);

    /// <summary>Tìm chính xác theo ISBN (phục vụ quét ISBN S05).</summary>
    Task<BookSearchDto?> SearchByIsbnAsync(string isbn, CancellationToken ct = default);
}

public class S06SearchDiscoveryService : IS06SearchDiscoveryService
{
    private readonly string _connectionString;
    private readonly ILogger<S06SearchDiscoveryService> _logger;

    public S06SearchDiscoveryService(
        IConfiguration configuration,
        ILogger<S06SearchDiscoveryService> logger)
    {
        // Lấy connection string từ appsettings.json ("ConnectionStrings:DefaultConnection")
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection chưa cấu hình");
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PagedResult<BookSearchDto>> SearchBooksAsync(
        string? keyword, string? isbn, int? categoryId, int? publishedYear,
        int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 12;

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand("dbo.sp_SearchBooks", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@Keyword", (object?)keyword ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ISBN", (object?)isbn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CategoryId", (object?)categoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PublishedYear", (object?)publishedYear ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Page", page);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);

            var items = new List<BookSearchDto>();
            int totalItems = 0;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (totalItems == 0)
                {
                    // Cột metadata xuất hiện ở mỗi dòng (do SP trả về cùng resultset)
                    totalItems = reader.GetInt32(reader.GetOrdinal("TotalItems"));
                }

                items.Add(MapBookSearchDto(reader));
            }

            return new PagedResult<BookSearchDto>(items, totalItems, page, pageSize);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_SearchBooks (Keyword={Keyword}, Page={Page})",
                keyword, page);
            return PagedResult<BookSearchDto>.Empty(page, pageSize);
        }
    }

    /// <inheritdoc />
    public async Task<PagedResult<BookSearchDto>> AdvancedFilterAsync(
        BookSearchRequest request, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand("dbo.sp_AdvancedFilterBooks", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@Keyword", (object?)request.Keyword ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CategoryId", (object?)request.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MinYear", DBNull.Value);
            cmd.Parameters.AddWithValue("@MaxYear", DBNull.Value);
            cmd.Parameters.AddWithValue("@OnlyAvailable", request.OnlyAvailable ? 1 : 0);
            cmd.Parameters.AddWithValue("@OwnerType", (object?)request.OwnerType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SortBy", string.IsNullOrWhiteSpace(request.SortBy) ? "Title" : request.SortBy);
            cmd.Parameters.AddWithValue("@Page", request.Page);
            cmd.Parameters.AddWithValue("@PageSize", request.PageSize);

            var items = new List<BookSearchDto>();
            int totalItems = 0;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (totalItems == 0)
                {
                    totalItems = reader.GetInt32(reader.GetOrdinal("TotalItems"));
                }

                items.Add(new BookSearchDto
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Title = reader.GetString(reader.GetOrdinal("Title")),
                    Author = reader.GetString(reader.GetOrdinal("Author")),
                    ISBN = reader.GetString(reader.GetOrdinal("ISBN")),
                    Publisher = reader.IsDBNull(reader.GetOrdinal("Publisher")) ? null : reader.GetString(reader.GetOrdinal("Publisher")),
                    PublishedYear = reader.IsDBNull(reader.GetOrdinal("PublishedYear")) ? null : reader.GetInt32(reader.GetOrdinal("PublishedYear")),
                    PageCount = reader.GetInt32(reader.GetOrdinal("PageCount")),
                    CategoryId = reader.GetInt32(reader.GetOrdinal("CategoryId")),
                    CategoryName = reader.GetString(reader.GetOrdinal("CategoryName")),
                    CoverImageUrl = reader.IsDBNull(reader.GetOrdinal("CoverImageUrl")) ? null : reader.GetString(reader.GetOrdinal("CoverImageUrl")),
                    Summary = reader.IsDBNull(reader.GetOrdinal("Summary")) ? null : reader.GetString(reader.GetOrdinal("Summary")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    AvailableCopyCount = reader.GetInt32(reader.GetOrdinal("AvailableCopies")),
                    AverageRating = reader.IsDBNull(reader.GetOrdinal("AverageRating")) ? 0 : Convert.ToDouble(reader["AverageRating"])
                });
            }

            return new PagedResult<BookSearchDto>(items, totalItems, request.Page, request.PageSize);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_AdvancedFilterBooks (SortBy={SortBy})",
                request.SortBy);
            return PagedResult<BookSearchDto>.Empty(request.Page, request.PageSize);
        }
    }

    /// <inheritdoc />
    public async Task<BookSearchDto?> SearchByIsbnAsync(string isbn, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand("dbo.sp_SearchByISBN", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@ISBN", isbn);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return MapBookSearchDto(reader);
            }
            return null;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_SearchByISBN (ISBN={ISBN})", isbn);
            return null;
        }
    }

    /// <summary>Hàm nội bộ mapping SqlDataReader → BookSearchDto (dùng cho sp_SearchBooks).</summary>
    private static BookSearchDto MapBookSearchDto(SqlDataReader reader)
    {
        return new BookSearchDto
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Author = reader.GetString(reader.GetOrdinal("Author")),
            ISBN = reader.GetString(reader.GetOrdinal("ISBN")),
            Publisher = reader.IsDBNull(reader.GetOrdinal("Publisher")) ? null : reader.GetString(reader.GetOrdinal("Publisher")),
            PublishedYear = reader.IsDBNull(reader.GetOrdinal("PublishedYear")) ? null : reader.GetInt32(reader.GetOrdinal("PublishedYear")),
            PageCount = reader.GetInt32(reader.GetOrdinal("PageCount")),
            CategoryId = reader.GetInt32(reader.GetOrdinal("CategoryId")),
            CategoryName = reader.GetString(reader.GetOrdinal("CategoryName")),
            CoverImageUrl = reader.IsDBNull(reader.GetOrdinal("CoverImageUrl")) ? null : reader.GetString(reader.GetOrdinal("CoverImageUrl")),
            Summary = reader.IsDBNull(reader.GetOrdinal("Summary")) ? null : reader.GetString(reader.GetOrdinal("Summary")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            AvailableCopyCount = reader.GetInt32(reader.GetOrdinal("AvailableCopyCount")),
            AverageRating = reader.IsDBNull(reader.GetOrdinal("AverageRating")) ? 0 : Convert.ToDouble(reader["AverageRating"])
        };
    }
}
