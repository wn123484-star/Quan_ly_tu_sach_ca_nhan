// =========================================================================================
// DỰ ÁN: BOOKSHARE - NỀN TẢNG QUẢN LÝ TỦ SÁCH CÁ NHÂN & MƯỢN TRẢ SÁCH CỘNG ĐỒNG (.NET 10)
// FILE: src/BookShare.Web/Models/PagedResult.cs
// MÔ TẢ: Lớp generic PagedResult<T> đóng gói kết quả phân trang Server-side
//        Trả về đầy đủ TotalItems, CurrentPage, TotalPages, PageSize theo Rubric 2.8
//        Tương đương logic: .Skip((page - 1) * pageSize).Take(pageSize) ở tầng SQL
//        Thông tin metadata bổ sung: HasPreviousPage, HasNextPage, Items (danh sách dòng trang)
//
// TÁC GIẢ: Lê Hoàn Lâm - MSSV: BIT250210 - Lớp 25IT - Nhóm 5
// NHÁNH:   sprint2/lam-database-query
// =========================================================================================

namespace BookShare.Web.Models;

/// <summary>
/// Lớp generic chứa kết quả phân trang Server-side, đầy đủ metadata cho UI render
/// thanh điều khiển Trang trước | 1 | 2 | 3 | Trang sau.
/// </summary>
/// <typeparam name="T">Kiểu entity / DTO của dòng dữ liệu trang hiện tại.</typeparam>
public class PagedResult<T>
{
    /// <summary>Danh sách dòng dữ liệu của trang hiện tại (đã slice Skip/Take).</summary>
    public IReadOnlyList<T> Items { get; init } = Array.Empty<T>();

    /// <summary>Tổng số dòng thỏa điều kiện filter (không phân trang).</summary>
    public int TotalItems { get; init; }

    /// <summary>Trang hiện tại (1-indexed).</summary>
    public int CurrentPage { get; init; }

    /// <summary>Số dòng mỗi trang (page size).</summary>
    public int PageSize { get; init; }

    /// <summary>Tổng số trang = ceil(TotalItems / PageSize).</summary>
    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling(TotalItems / (double)PageSize)
        : 0;

    /// <summary>true nếu còn trang trước (CurrentPage > 1).</summary>
    public bool HasPreviousPage => CurrentPage > 1;

    /// <summary>true nếu còn trang sau (CurrentPage < TotalPages).</summary>
    public bool HasNextPage => CurrentPage < TotalPages;

    /// <summary>Trang đầu tiên luôn là 1.</summary>
    public int FirstPage => 1;

    /// <summary>Trang cuối cùng = TotalPages.</summary>
    public int LastPage => TotalPages;

    /// <summary>
    /// Danh sách số trang để hiển thị trên thanh điều khiển (windowing).
    /// Ví dụ trang 5/20, window = 3 → [4, 5, 6].
    /// </summary>
    public IReadOnlyList<int> PageWindow { get; init; } = Array.Empty<int>();

    /// <summary>Constructor mặc định cho serializer.</summary>
    public PagedResult() { }

    /// <summary>Constructor tiện lợi khi đã có danh sách và tổng.</summary>
    public PagedResult(IReadOnlyList<T> items, int totalItems, int currentPage, int pageSize)
    {
        Items = items;
        TotalItems = totalItems;
        CurrentPage = currentPage;
        PageSize = pageSize;
        PageWindow = ComputePageWindow(currentPage, TotalPages, windowSize: 5);
    }

    /// <summary>
    /// Tạo PagedResult rỗng (cho trường hợp không có dữ liệu / lỗi).
    /// </summary>
    public static PagedResult<T> Empty(int currentPage = 1, int pageSize = 12)
        => new(Array.Empty<T>(), 0, currentPage, pageSize);

    /// <summary>
    /// Áp dụng phân trang trên IQueryable (LINQ to Objects / EF Core).
    /// Tương đương: query.Skip((page - 1) * pageSize).Take(pageSize)
    /// </summary>
    public static async Task<PagedResult<T>> FromQueryAsync(
        IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 12;

        var totalItems = query.Count();
        var items = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<T>(items, totalItems, page, pageSize);
    }

    /// <summary>Map sang PagedResult&lt;TResult&gt; bằng cách transform từng phần tử.</summary>
    public PagedResult<TResult> Map<TResult>(Func<T, TResult> selector)
        => new(Items.Select(selector).ToList(), TotalItems, CurrentPage, PageSize)
        {
            PageWindow = PageWindow
        };

    /// <summary>Tính cửa sổ trang hiển thị trên UI (5 trang xung quanh trang hiện tại).</summary>
    private static IReadOnlyList<int> ComputePageWindow(int currentPage, int totalPages, int windowSize)
    {
        if (totalPages <= 0) return Array.Empty<int>();

        var half = windowSize / 2;
        var start = Math.Max(1, currentPage - half);
        var end = Math.Min(totalPages, start + windowSize - 1);
        start = Math.Max(1, end - windowSize + 1);

        return Enumerable.Range(start, end - start + 1).ToArray();
    }
}

/// <summary>
/// DTO chứa tham số phân trang + filter truyền từ Controller xuống Service.
/// Hỗ trợ binding từ query string ASP.NET Core MVC.
/// </summary>
public class PaginationRequest
{
    private int _page = 1;
    private int _pageSize = 12;

    /// <summary>Trang hiện tại (1-indexed, min = 1).</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>Số dòng mỗi trang (min = 1, max = 100, default = 12).</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value is < 1 or > 100 ? 12 : value;
    }

    /// <summary>Tính OFFSET = (Page - 1) * PageSize cho SQL Server.</summary>
    public int Offset => (Page - 1) * PageSize;
}

/// <summary>
/// DTO chứa tham số Search + Filter + Pagination đầy đủ cho S06 / S07.
/// </summary>
public class BookSearchRequest : PaginationRequest
{
    /// <summary>Từ khóa tìm theo Title/Author (LIKE %kw%).</summary>
    public string? Keyword { get; set; }

    /// <summary>Mã ISBN chính xác.</summary>
    public string? ISBN { get; set; }

    /// <summary>Lọc theo thể loại (NULL = all).</summary>
    public int? CategoryId { get; set; }

    /// <summary>Lọc theo năm xuất bản.</summary>
    public int? PublishedYear { get; set; }

    /// <summary>'System' = Kho chung, 'User' = Tủ P2P, NULL = cả hai.</summary>
    public string? OwnerType { get; set; }

    /// <summary>'Available', 'Borrowed', 'Reserved', 'Lost', 'Damaged'.</summary>
    public string? Status { get; set; }

    /// <summary>'New', 'Good', 'Fair', 'Worn'.</summary>
    public string? Condition { get; set; }

    /// <summary>Chỉ trả về sách còn Available để mượn (boolean filter).</summary>
    public bool OnlyAvailable { get; set; }

    /// <summary>Trường sắp xếp: Title | Author | Year | Rating | Newest.</summary>
    public string SortBy { get; set; } = "Title";

    /// <summary>Lọc theo thư viện cụ thể.</summary>
    public int? LibraryId { get; set; }

    /// <summary>Lọc theo chủ sở hữu P2P cụ thể.</summary>
    public string? OwnerUserId { get; set; }
}
