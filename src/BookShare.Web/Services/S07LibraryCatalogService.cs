// =========================================================================================
// DỰ ÁN: BOOKSHARE - NỀN TẢNG QUẢN LÝ TỦ SÁCH CÁ NHÂN & MƯỢN TRẢ SÁCH CỘNG ĐỒNG (.NET 10)
// FILE: src/BookShare.Web/Services/S07LibraryCatalogService.cs
// MÔ TẢ: Service Dịch vụ S07 - Library Catalog (Kho sách chung ĐH CMC + Tủ sách P2P)
//        - Lọc đa tiêu chí: OwnerType (System/User), Status (Available/Borrowed),
//          Condition, CategoryId, LibraryId, OwnerUserId, IsLendable
//        - Phân trang Server-side bằng stored procedure sp_SearchBookCopiesPaged
//        - Trả về PagedResult<BookCopyCatalogDto> đầy đủ TotalItems / CurrentPage / TotalPages
//        - Phục vụ UI Library Catalog hiển thị Empty State khi không có dữ liệu
//
// TÁC GIẢ: Lê Hoàn Lâm - MSSV: BIT250210 - Lớp 25IT - Nhóm 5
// NHÁNH:   sprint2/lam-database-query
// =========================================================================================

using BookShare.Web.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace BookShare.Web.Services;

/// <summary>
/// DTO kết quả catalog bản sao sách (mapping từ sp_SearchBookCopiesPaged).
/// </summary>
public class BookCopyCatalogDto
{
    public int BookCopyId { get; set; }
    public int BookId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;  // System / User
    public int? LibraryId { get; set; }
    public string? OwnerUserId { get; set; }
    public string? ShelfLocation { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string CopyStatus { get; set; } = string.Empty;
    public bool IsLendable { get; set; }
    public DateTime CopyCreatedAt { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public int? PublishedYear { get; set; }
    public int PageCount { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Summary { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? LibraryName { get; set; }
    public string? LibraryAddress { get; set; }
    public string? OwnerFullName { get; set; }
    public string? OwnerAvatarUrl { get; set; }
}

/// <summary>
/// Dịch vụ S07 - Library Catalog.
/// Lọc đa tiêu chí bản sao sách + phân trang qua stored procedure.
/// </summary>
public interface IS07LibraryCatalogService
{
    /// <summary>
    /// Lọc đa tiêu chí BookCopies với phân trang.
    /// Tương đương EF Core:
    ///   context.BookCopies.Include(...).Where(...).Skip((page-1)*pageSize).Take(pageSize)
    /// </summary>
    Task<PagedResult<BookCopyCatalogDto>> SearchBookCopiesPagedAsync(
        BookSearchRequest request, CancellationToken ct = default);

    /// <summary>Lấy chi tiết đầu sách + tất cả bản sao + đánh giá.</summary>
    Task<BookDetailDto?> GetBookDetailAsync(int bookId, CancellationToken ct = default);
}

/// <summary>DTO chi tiết đầu sách (kết quả sp_GetBookDetail - 4 resultsets).</summary>
public class BookDetailDto
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
    public List<BookCopyCatalogDto> Copies { get; set; } = new();
    public List<ReviewDto> Reviews { get; set; } = new();
    public BookStatsDto Stats { get; set; } = new();
}

public class ReviewDto
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public string? ReviewerAvatar { get; set; }
    public int Rating { get; set; }
    public string? ReviewTitle { get; set; }
    public string? ReviewContent { get; set; }
    public int Likes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BookStatsDto
{
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public int BorrowedCopies { get; set; }
    public int ReservedCopies { get; set; }
    public int SystemCopies { get; set; }
    public int P2PCopies { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
}

public class S07LibraryCatalogService : IS07LibraryCatalogService
{
    private readonly string _connectionString;
    private readonly ILogger<S07LibraryCatalogService> _logger;

    public S07LibraryCatalogService(
        IConfiguration configuration,
        ILogger<S07LibraryCatalogService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection chưa cấu hình");
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PagedResult<BookCopyCatalogDto>> SearchBookCopiesPagedAsync(
        BookSearchRequest request, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand("dbo.sp_SearchBookCopiesPaged", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@Keyword", (object?)request.Keyword ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ISBN", (object?)request.ISBN ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OwnerType", (object?)request.OwnerType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", (object?)request.Status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Condition", (object?)request.Condition ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CategoryId", (object?)request.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LibraryId", (object?)request.LibraryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OwnerUserId", (object?)request.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsLendable", (object?)null ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Page", request.Page);
            cmd.Parameters.AddWithValue("@PageSize", request.PageSize);

            var items = new List<BookCopyCatalogDto>();
            int totalItems = 0;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (totalItems == 0)
                {
                    totalItems = reader.GetInt32(reader.GetOrdinal("TotalItems"));
                }
                items.Add(MapBookCopyDto(reader));
            }

            return new PagedResult<BookCopyCatalogDto>(items, totalItems, request.Page, request.PageSize);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_SearchBookCopiesPaged (OwnerType={OwnerType}, Status={Status})",
                request.OwnerType, request.Status);
            return PagedResult<BookCopyCatalogDto>.Empty(request.Page, request.PageSize);
        }
    }

    /// <inheritdoc />
    public async Task<BookDetailDto?> GetBookDetailAsync(int bookId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand("dbo.sp_GetBookDetail", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@BookId", bookId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            // Resultset 1: Book info
            if (!await reader.ReadAsync(ct)) return null;

            var detail = new BookDetailDto
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
                Summary = reader.IsDBNull(reader.GetOrdinal("Summary")) ? null : reader.GetString(reader.GetOrdinal("Summary"))
            };

            // Resultset 2: Book copies
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                detail.Copies.Add(new BookCopyCatalogDto
                {
                    BookCopyId = reader.GetInt32(reader.GetOrdinal("BookCopyId")),
                    Barcode = reader.GetString(reader.GetOrdinal("Barcode")),
                    OwnerType = reader.GetString(reader.GetOrdinal("OwnerType")),
                    LibraryId = reader.IsDBNull(reader.GetOrdinal("LibraryId")) ? null : reader.GetInt32(reader.GetOrdinal("LibraryId")),
                    LibraryName = reader.IsDBNull(reader.GetOrdinal("LibraryName")) ? null : reader.GetString(reader.GetOrdinal("LibraryName")),
                    LibraryAddress = reader.IsDBNull(reader.GetOrdinal("LibraryAddress")) ? null : reader.GetString(reader.GetOrdinal("LibraryAddress")),
                    OwnerUserId = reader.IsDBNull(reader.GetOrdinal("OwnerUserId")) ? null : reader.GetString(reader.GetOrdinal("OwnerUserId")),
                    OwnerFullName = reader.IsDBNull(reader.GetOrdinal("OwnerFullName")) ? null : reader.GetString(reader.GetOrdinal("OwnerFullName")),
                    OwnerAvatarUrl = reader.IsDBNull(reader.GetOrdinal("OwnerAvatarUrl")) ? null : reader.GetString(reader.GetOrdinal("OwnerAvatarUrl")),
                    ShelfLocation = reader.IsDBNull(reader.GetOrdinal("ShelfLocation")) ? null : reader.GetString(reader.GetOrdinal("ShelfLocation")),
                    Condition = reader.GetString(reader.GetOrdinal("Condition")),
                    CopyStatus = reader.GetString(reader.GetOrdinal("Status")),
                    IsLendable = reader.GetBoolean(reader.GetOrdinal("IsLendable"))
                });
            }

            // Resultset 3: Reviews
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                detail.Reviews.Add(new ReviewDto
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    UserId = reader.GetString(reader.GetOrdinal("UserId")),
                    ReviewerName = reader.GetString(reader.GetOrdinal("ReviewerName")),
                    ReviewerAvatar = reader.IsDBNull(reader.GetOrdinal("ReviewerAvatar")) ? null : reader.GetString(reader.GetOrdinal("ReviewerAvatar")),
                    Rating = reader.GetInt32(reader.GetOrdinal("Rating")),
                    ReviewTitle = reader.IsDBNull(reader.GetOrdinal("ReviewTitle")) ? null : reader.GetString(reader.GetOrdinal("ReviewTitle")),
                    ReviewContent = reader.IsDBNull(reader.GetOrdinal("ReviewContent")) ? null : reader.GetString(reader.GetOrdinal("ReviewContent")),
                    Likes = reader.GetInt32(reader.GetOrdinal("Likes")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
                });
            }

            // Resultset 4: Stats
            await reader.NextResultAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                detail.Stats = new BookStatsDto
                {
                    TotalCopies = reader.GetInt32(reader.GetOrdinal("TotalCopies")),
                    AvailableCopies = reader.GetInt32(reader.GetOrdinal("AvailableCopies")),
                    BorrowedCopies = reader.GetInt32(reader.GetOrdinal("BorrowedCopies")),
                    ReservedCopies = reader.GetInt32(reader.GetOrdinal("ReservedCopies")),
                    SystemCopies = reader.GetInt32(reader.GetOrdinal("SystemCopies")),
                    P2PCopies = reader.GetInt32(reader.GetOrdinal("P2PCopies")),
                    AverageRating = reader.IsDBNull(reader.GetOrdinal("AverageRating")) ? 0 : Convert.ToDouble(reader["AverageRating"]),
                    ReviewCount = reader.GetInt32(reader.GetOrdinal("ReviewCount"))
                };
            }

            return detail;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Lỗi SQL khi gọi sp_GetBookDetail (BookId={BookId})", bookId);
            return null;
        }
    }

    private static BookCopyCatalogDto MapBookCopyDto(SqlDataReader reader)
    {
        return new BookCopyCatalogDto
        {
            BookCopyId = reader.GetInt32(reader.GetOrdinal("BookCopyId")),
            BookId = reader.GetInt32(reader.GetOrdinal("BookId")),
            Barcode = reader.GetString(reader.GetOrdinal("Barcode")),
            OwnerType = reader.GetString(reader.GetOrdinal("OwnerType")),
            LibraryId = reader.IsDBNull(reader.GetOrdinal("LibraryId")) ? null : reader.GetInt32(reader.GetOrdinal("LibraryId")),
            OwnerUserId = reader.IsDBNull(reader.GetOrdinal("OwnerUserId")) ? null : reader.GetString(reader.GetOrdinal("OwnerUserId")),
            ShelfLocation = reader.IsDBNull(reader.GetOrdinal("ShelfLocation")) ? null : reader.GetString(reader.GetOrdinal("ShelfLocation")),
            Condition = reader.GetString(reader.GetOrdinal("Condition")),
            CopyStatus = reader.GetString(reader.GetOrdinal("CopyStatus")),
            IsLendable = reader.GetBoolean(reader.GetOrdinal("IsLendable")),
            CopyCreatedAt = reader.GetDateTime(reader.GetOrdinal("CopyCreatedAt")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Author = reader.GetString(reader.GetOrdinal("Author")),
            ISBN = reader.GetString(reader.GetOrdinal("ISBN")),
            Publisher = reader.IsDBNull(reader.GetOrdinal("Publisher")) ? null : reader.GetString(reader.GetOrdinal("Publisher")),
            PublishedYear = reader.IsDBNull(reader.GetOrdinal("PublishedYear")) ? null : reader.GetInt32(reader.GetOrdinal("PublishedYear")),
            PageCount = reader.GetInt32(reader.GetOrdinal("PageCount")),
            CoverImageUrl = reader.IsDBNull(reader.GetOrdinal("CoverImageUrl")) ? null : reader.GetString(reader.GetOrdinal("CoverImageUrl")),
            Summary = reader.IsDBNull(reader.GetOrdinal("Summary")) ? null : reader.GetString(reader.GetOrdinal("Summary")),
            CategoryId = reader.GetInt32(reader.GetOrdinal("CategoryId")),
            CategoryName = reader.GetString(reader.GetOrdinal("CategoryName")),
            LibraryName = reader.IsDBNull(reader.GetOrdinal("LibraryName")) ? null : reader.GetString(reader.GetOrdinal("LibraryName")),
            LibraryAddress = reader.IsDBNull(reader.GetOrdinal("LibraryAddress")) ? null : reader.GetString(reader.GetOrdinal("LibraryAddress")),
            OwnerFullName = reader.IsDBNull(reader.GetOrdinal("OwnerFullName")) ? null : reader.GetString(reader.GetOrdinal("OwnerFullName")),
            OwnerAvatarUrl = reader.IsDBNull(reader.GetOrdinal("OwnerAvatarUrl")) ? null : reader.GetString(reader.GetOrdinal("OwnerAvatarUrl"))
        };
    }
}
