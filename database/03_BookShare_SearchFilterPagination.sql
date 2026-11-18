/* =========================================================================================
DỰ ÁN: BOOKSHARE - NỀN TẢNG QUẢN LÝ TỦ SÁCH CÁ NHÂN & MƯỢN TRẢ SÁCH CỘNG ĐỒNG (.NET 10)
FILE: database/03_BookShare_SearchFilterPagination.sql
MÔ TẢ: Stored Procedures & Functions tối ưu truy vấn Search/Filter/Pagination
       Phục vụ Dịch vụ S06 (Search Discovery) và S07 (Library Catalog)
       Tương thích SQL Server 2022, dùng OFFSET / FETCH NEXT cho phân trang Server-side
       (tương đương .Skip((page-1)*pageSize).Take(pageSize) của EF Core)

       TÁC GIẢ: Lê Hoàn Lâm - MSSV: BIT210210 - Lớp 25IT - Nhóm 5
       NHÁNH:   sprint2/lam-database-query
       ISSUE:   #8 Sprint2 - Tối ưu Truy vấn Tìm kiếm/Phân trang
========================================================================================= */
USE [BookShareDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =========================================================================================
1. sp_SearchBooks - Tìm kiếm đầu sách theo Keyword (Title/Author) + ISBN + Phân trang
   Tương đương EF Core:
     context.Books
       .Where(b => b.Title.Contains(keyword) || b.Author.Contains(keyword) || b.ISBN == isbn)
       .OrderBy(b => b.Title)
       .Skip((page - 1) * pageSize)
       .Take(pageSize)
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_SearchBooks]
    @Keyword       NVARCHAR(250) = NULL,    -- tìm theo Title hoặc Author (LIKE %kw%)
    @ISBN          NVARCHAR(20)  = NULL,    -- tìm chính xác theo ISBN
    @CategoryId    INT           = NULL,    -- lọc theo thể loại
    @PublishedYear INT           = NULL,    -- lọc theo năm XB
    @Page          INT           = 1,       -- trang hiện tại (1-indexed)
    @PageSize      INT           = 12       -- số dòng/trang (mặc định 12)
AS
BEGIN
    SET NOCOUNT ON;
    SET @Page     = IIF(@Page < 1, 1, @Page);
    SET @PageSize = IIF(@PageSize < 1, 12, @PageSize);

    DECLARE @Offset INT = (@Page - 1) * @PageSize;
    DECLARE @TotalItems INT;

    -- Đếm tổng số dòng thỏa điều kiện (dùng index IX_Books_Title_Author và IX_Books_ISBN)
    SELECT @TotalItems = COUNT(*)
    FROM [dbo].[Books] b
    WHERE (@Keyword IS NULL OR b.[Title] LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS
                                    OR b.[Author] LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS)
      AND (@ISBN IS NULL OR b.[ISBN] = @ISBN)
      AND (@CategoryId IS NULL OR b.[CategoryId] = @CategoryId)
      AND (@PublishedYear IS NULL OR b.[PublishedYear] = @PublishedYear);

    -- Truy vấn phân trang với OFFSET/FETCH NEXT (SQL Server 2022)
    -- Sử dụng covering index IX_Books_Title_Author + JOIN Categories để tránh Key Lookup
    SELECT
        b.[Id],
        b.[Title],
        b.[Author],
        b.[ISBN],
        b.[Publisher],
        b.[PublishedYear],
        b.[PageCount],
        b.[CategoryId],
        c.[Name]              AS [CategoryName],
        c.[Slug]              AS [CategorySlug],
        b.[CoverImageUrl],
        b.[Summary],
        b.[CreatedAt],
        -- Số bản sao khả dụng (dùng index IX_BookCopies_BookId_Status)
        (
            SELECT COUNT(*)
            FROM [dbo].[BookCopies] bc
            WHERE bc.[BookId] = b.[Id]
              AND bc.[Status] = N'Available'
              AND bc.[IsLendable] = 1
        ) AS [AvailableCopyCount],
        -- Đánh giá trung bình (dùng index IX_Reviews_BookId_Rating)
        (
            SELECT AVG(CAST(r.[Rating] AS FLOAT))
            FROM [dbo].[Reviews] r
            WHERE r.[BookId] = b.[Id] AND r.[IsPublic] = 1
        ) AS [AverageRating],
        @TotalItems AS [TotalItems],
        @Page AS [CurrentPage],
        @PageSize AS [PageSize]
    FROM [dbo].[Books] b
    INNER JOIN [dbo].[Categories] c ON b.[CategoryId] = c.[Id]
    WHERE (@Keyword IS NULL OR b.[Title] LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS
                                    OR b.[Author] LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS)
      AND (@ISBN IS NULL OR b.[ISBN] = @ISBN)
      AND (@CategoryId IS NULL OR b.[CategoryId] = @CategoryId)
      AND (@PublishedYear IS NULL OR b.[PublishedYear] = @PublishedYear)
    ORDER BY b.[Title] ASC, b.[Author] ASC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

/* =========================================================================================
2. sp_SearchBookCopiesPaged - Tìm kiếm + Lọc đa tiêu chí trên BookCopies + Phân trang
   Lọc: Library (Kho chung ĐH CMC) vs P2P (Tủ sách cá nhân)
        Trạng thái: Available / Borrowed / Reserved / Lost / Damaged
        Condition: New / Good / Fair / Worn
        Category, LibraryId, OwnerUserId
   Tương đương EF Core:
     context.BookCopies
       .Include(bc => bc.Book).ThenInclude(b => b.Category)
       .Include(bc => bc.Library)
       .Include(bc => bc.OwnerUser)
       .Where(bc => bc.OwnerType == ownerType && bc.Status == status && ...)
       .OrderBy(bc => bc.Book.Title)
       .Skip((page - 1) * pageSize).Take(pageSize)
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_SearchBookCopiesPaged]
    @Keyword      NVARCHAR(250) = NULL,
    @ISBN         NVARCHAR(20)  = NULL,
    @OwnerType    NVARCHAR(20)  = NULL,   -- 'System' | 'User' | NULL (all)
    @Status       NVARCHAR(20)  = NULL,   -- 'Available' | 'Borrowed' | 'Reserved' | 'Lost' | 'Damaged' | NULL
    @Condition    NVARCHAR(20)  = NULL,
    @CategoryId   INT           = NULL,
    @LibraryId    INT           = NULL,
    @OwnerUserId  NVARCHAR(450) = NULL,
    @IsLendable   BIT           = NULL,
    @Page         INT           = 1,
    @PageSize     INT           = 12
AS
BEGIN
    SET NOCOUNT ON;
    SET @Page     = IIF(@Page < 1, 1, @Page);
    SET @PageSize = IIF(@PageSize < 1, 12, @PageSize);

    DECLARE @Offset INT = (@Page - 1) * @PageSize;
    DECLARE @TotalItems INT;

    -- Sử dụng CTE để đếm 1 lần và select phân trang 1 lần (tránh truy vấn 2 lần)
    ;WITH FilteredCopies AS (
        SELECT
            bc.[Id]            AS [BookCopyId],
            bc.[BookId],
            bc.[Barcode],
            bc.[OwnerType],
            bc.[LibraryId],
            bc.[OwnerUserId],
            bc.[ShelfLocation],
            bc.[Condition],
            bc.[Status]         AS [CopyStatus],
            bc.[IsLendable],
            bc.[CreatedAt]      AS [CopyCreatedAt],
            b.[Title],
            b.[Author],
            b.[ISBN],
            b.[Publisher],
            b.[PublishedYear],
            b.[PageCount],
            b.[CoverImageUrl],
            b.[Summary],
            b.[CategoryId],
            c.[Name]            AS [CategoryName],
            l.[Name]            AS [LibraryName],
            l.[Address]         AS [LibraryAddress],
            u.[FullName]        AS [OwnerFullName],
            u.[AvatarUrl]        AS [OwnerAvatarUrl],
            ROW_NUMBER() OVER (ORDER BY b.[Title] ASC, bc.[Id] ASC) AS [RowNum]
        FROM [dbo].[BookCopies] bc
        INNER JOIN [dbo].[Books] b        ON bc.[BookId]   = b.[Id]
        INNER JOIN [dbo].[Categories] c  ON b.[CategoryId] = c.[Id]
        LEFT  JOIN [dbo].[Libraries] l    ON bc.[LibraryId] = l.[Id]
        LEFT  JOIN [dbo].[AspNetUsers] u  ON bc.[OwnerUserId] = u.[Id]
        WHERE (@Keyword IS NULL OR b.[Title]  LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS
                                OR b.[Author] LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS)
          AND (@ISBN IS NULL OR b.[ISBN] = @ISBN)
          AND (@OwnerType IS NULL OR bc.[OwnerType] = @OwnerType)
          AND (@Status IS NULL OR bc.[Status] = @Status)
          AND (@Condition IS NULL OR bc.[Condition] = @Condition)
          AND (@CategoryId IS NULL OR b.[CategoryId] = @CategoryId)
          AND (@LibraryId IS NULL OR bc.[LibraryId] = @LibraryId)
          AND (@OwnerUserId IS NULL OR bc.[OwnerUserId] = @OwnerUserId)
          AND (@IsLendable IS NULL OR bc.[IsLendable] = @IsLendable)
    )
    SELECT @TotalItems = COUNT(*) FROM FilteredCopies;

    SELECT
        fc.[BookCopyId],
        fc.[BookId],
        fc.[Barcode],
        fc.[OwnerType],
        fc.[LibraryId],
        fc.[OwnerUserId],
        fc.[ShelfLocation],
        fc.[Condition],
        fc.[CopyStatus],
        fc.[IsLendable],
        fc.[CopyCreatedAt],
        fc.[Title],
        fc.[Author],
        fc.[ISBN],
        fc.[Publisher],
        fc.[PublishedYear],
        fc.[PageCount],
        fc.[CoverImageUrl],
        fc.[Summary],
        fc.[CategoryId],
        fc.[CategoryName],
        fc.[LibraryName],
        fc.[LibraryAddress],
        fc.[OwnerFullName],
        fc.[OwnerAvatarUrl],
        @TotalItems  AS [TotalItems],
        @Page        AS [CurrentPage],
        @PageSize    AS [PageSize],
        CEILING(CAST(@TotalItems AS FLOAT) / @PageSize) AS [TotalPages]
    FROM FilteredCopies fc
    WHERE fc.[RowNum] BETWEEN (@Offset + 1) AND (@Offset + @PageSize)
    ORDER BY fc.[RowNum];
END
GO

/* =========================================================================================
3. sp_GetBookDetail - Lấy chi tiết đầu sách + tất cả bản sao + đánh giá + rating trung bình
   Dùng cho trang chi tiết sách S06 / S07
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_GetBookDetail]
    @BookId INT
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Thông tin đầu sách
    SELECT
        b.[Id], b.[Title], b.[Author], b.[ISBN], b.[Publisher], b.[PublishedYear],
        b.[PageCount], b.[CategoryId], c.[Name] AS [CategoryName], c.[Slug] AS [CategorySlug],
        b.[CoverImageUrl], b.[Summary], b.[CreatedAt]
    FROM [dbo].[Books] b
    INNER JOIN [dbo].[Categories] c ON b.[CategoryId] = c.[Id]
    WHERE b.[Id] = @BookId;

    -- 2. Danh sách bản sao (System + User P2P) theo tình trạng
    SELECT
        bc.[Id] AS [BookCopyId], bc.[Barcode], bc.[OwnerType], bc.[LibraryId],
        l.[Name] AS [LibraryName], l.[Address] AS [LibraryAddress],
        bc.[OwnerUserId], u.[FullName] AS [OwnerFullName], u.[AvatarUrl] AS [OwnerAvatarUrl],
        bc.[ShelfLocation], bc.[Condition], bc.[Status], bc.[IsLendable], bc.[CreatedAt]
    FROM [dbo].[BookCopies] bc
    LEFT  JOIN [dbo].[Libraries] l   ON bc.[LibraryId]   = l.[Id]
    LEFT  JOIN [dbo].[AspNetUsers] u ON bc.[OwnerUserId] = u.[Id]
    WHERE bc.[BookId] = @BookId
    ORDER BY
        CASE WHEN bc.[Status] = N'Available' AND bc.[IsLendable] = 1 THEN 0 ELSE 1 END,
        bc.[OwnerType] ASC, bc.[Condition] ASC;

    -- 3. Đánh giá công khai (top 10 mới nhất, dùng IX_Reviews_BookId_Rating)
    SELECT TOP 10
        r.[Id], r.[UserId], u.[FullName] AS [ReviewerName], u.[AvatarUrl] AS [ReviewerAvatar],
        r.[Rating], r.[Title] AS [ReviewTitle], r.[Content] AS [ReviewContent],
        r.[Likes], r.[CreatedAt]
    FROM [dbo].[Reviews] r
    INNER JOIN [dbo].[AspNetUsers] u ON r.[UserId] = u.[Id]
    WHERE r.[BookId] = @BookId AND r.[IsPublic] = 1
    ORDER BY r.[Likes] DESC, r.[CreatedAt] DESC;

    -- 4. Thống kê tổng quan
    SELECT
        COUNT(bc.[Id]) AS [TotalCopies],
        SUM(CASE WHEN bc.[Status] = N'Available'   AND bc.[IsLendable] = 1 THEN 1 ELSE 0 END) AS [AvailableCopies],
        SUM(CASE WHEN bc.[Status] = N'Borrowed'   THEN 1 ELSE 0 END) AS [BorrowedCopies],
        SUM(CASE WHEN bc.[Status] = N'Reserved'   THEN 1 ELSE 0 END) AS [ReservedCopies],
        SUM(CASE WHEN bc.[OwnerType] = N'System' THEN 1 ELSE 0 END) AS [SystemCopies],
        SUM(CASE WHEN bc.[OwnerType] = N'User'    THEN 1 ELSE 0 END) AS [P2PCopies],
        AVG(CAST(r2.[Rating] AS FLOAT)) AS [AverageRating],
        COUNT(r2.[Id]) AS [ReviewCount]
    FROM [dbo].[BookCopies] bc
    LEFT JOIN [dbo].[Reviews] r2 ON r2.[BookId] = bc.[BookId] AND r2.[IsPublic] = 1
    WHERE bc.[BookId] = @BookId;
END
GO

/* =========================================================================================
4. sp_SearchByISBN - Tìm chính xác theo ISBN (Dịch vụ S05 ISBN Auto Catalog)
   Dùng cho tính năng quét ISBN bằng camera điện thoại
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_SearchByISBN]
    @ISBN NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        b.[Id], b.[Title], b.[Author], b.[ISBN], b.[Publisher], b.[PublishedYear],
        b.[PageCount], b.[CategoryId], c.[Name] AS [CategoryName],
        b.[CoverImageUrl], b.[Summary], b.[CreatedAt],
        COUNT(bc.[Id]) AS [TotalCopies],
        SUM(CASE WHEN bc.[Status] = N'Available' AND bc.[IsLendable] = 1 THEN 1 ELSE 0 END) AS [AvailableCopies]
    FROM [dbo].[Books] b
    INNER JOIN [dbo].[Categories] c ON b.[CategoryId] = c.[Id]
    LEFT  JOIN [dbo].[BookCopies] bc ON bc.[BookId] = b.[Id]
    WHERE b.[ISBN] = @ISBN COLLATE Vietnamese_CI_AS
    GROUP BY b.[Id], b.[Title], b.[Author], b.[ISBN], b.[Publisher], b.[PublishedYear],
             b.[PageCount], b.[CategoryId], c.[Name], b.[CoverImageUrl], b.[Summary], b.[CreatedAt];
END
GO

/* =========================================================================================
5. sp_AdvancedFilterBooks - Lọc nâng cao kết hợp Search + Filter + Pagination
   Cho phép filter đa tiêu chí: CategoryId, PublishedYear range, có sẵn bản sao hay không
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_AdvancedFilterBooks]
    @Keyword        NVARCHAR(250) = NULL,
    @CategoryId     INT           = NULL,
    @MinYear        INT           = NULL,
    @MaxYear        INT           = NULL,
    @OnlyAvailable  BIT           = 0,    -- 1: chỉ trả về sách có bản sao Available
    @OwnerType      NVARCHAR(20)  = NULL, -- 'System' | 'User' (lọc theo loại bản sao)
    @SortBy         NVARCHAR(20)  = N'Title',  -- Title | Author | Year | Rating | Newest
    @Page           INT           = 1,
    @PageSize       INT           = 12
AS
BEGIN
    SET NOCOUNT ON;
    SET @Page     = IIF(@Page < 1, 1, @Page);
    SET @PageSize = IIF(@PageSize < 1, 12, @PageSize);

    DECLARE @Offset INT = (@Page - 1) * @PageSize;
    DECLARE @TotalItems INT;

    ;WITH BookList AS (
        SELECT
            b.[Id], b.[Title], b.[Author], b.[ISBN], b.[Publisher], b.[PublishedYear],
            b.[PageCount], b.[CategoryId], c.[Name] AS [CategoryName],
            b.[CoverImageUrl], b.[Summary], b.[CreatedAt],
            AVG(CAST(r.[Rating] AS FLOAT)) AS [AverageRating],
            COUNT(bc.[Id]) AS [TotalCopies],
            SUM(CASE WHEN bc.[Status] = N'Available' AND bc.[IsLendable] = 1 THEN 1 ELSE 0 END) AS [AvailableCopies],
            ROW_NUMBER() OVER (
                ORDER BY
                    CASE WHEN @SortBy = N'Title'  THEN b.[Title] END ASC,
                    CASE WHEN @SortBy = N'Author' THEN b.[Author] END ASC,
                    CASE WHEN @SortBy = N'Year'    THEN b.[PublishedYear] END DESC,
                    CASE WHEN @SortBy = N'Newest' THEN b.[CreatedAt] END DESC
            ) AS [RowNum]
        FROM [dbo].[Books] b
        INNER JOIN [dbo].[Categories] c ON b.[CategoryId] = c.[Id]
        LEFT  JOIN [dbo].[Reviews] r     ON r.[BookId] = b.[Id] AND r.[IsPublic] = 1
        LEFT  JOIN [dbo].[BookCopies] bc ON bc.[BookId] = b.[Id]
        WHERE (@Keyword IS NULL OR b.[Title]  LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS
                                OR b.[Author] LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS)
          AND (@CategoryId IS NULL OR b.[CategoryId] = @CategoryId)
          AND (@MinYear IS NULL OR b.[PublishedYear] >= @MinYear)
          AND (@MaxYear IS NULL OR b.[PublishedYear] <= @MaxYear)
          AND (@OwnerType IS NULL OR EXISTS (
                SELECT 1 FROM [dbo].[BookCopies] bc2
                WHERE bc2.[BookId] = b.[Id] AND bc2.[OwnerType] = @OwnerType
                  AND bc2.[Status] = N'Available' AND bc2.[IsLendable] = 1))
        GROUP BY b.[Id], b.[Title], b.[Author], b.[ISBN], b.[Publisher], b.[PublishedYear],
                 b.[PageCount], b.[CategoryId], c.[Name], b.[CoverImageUrl], b.[Summary], b.[CreatedAt]
        HAVING (@OnlyAvailable = 0 OR SUM(CASE WHEN bc.[Status] = N'Available' AND bc.[IsLendable] = 1 THEN 1 ELSE 0 END) > 0)
    )
    SELECT @TotalItems = COUNT(*) FROM BookList;

    SELECT
        bl.[Id], bl.[Title], bl.[Author], bl.[ISBN], bl.[Publisher], bl.[PublishedYear],
        bl.[PageCount], bl.[CategoryId], bl.[CategoryName], bl.[CoverImageUrl], bl.[Summary],
        bl.[CreatedAt], bl.[AverageRating], bl.[TotalCopies], bl.[AvailableCopies],
        @TotalItems AS [TotalItems],
        @Page AS [CurrentPage],
        @PageSize AS [PageSize],
        CEILING(CAST(@TotalItems AS FLOAT) / @PageSize) AS [TotalPages]
    FROM BookList bl
    WHERE bl.[RowNum] BETWEEN (@Offset + 1) AND (@Offset + @PageSize)
    ORDER BY bl.[RowNum];
END
GO

PRINT N'== [03_BookShare_SearchFilterPagination.sql] Đã tạo 5 Stored Procedures ==';
PRINT N'   - sp_SearchBooks';
PRINT N'   - sp_SearchBookCopiesPaged (lọc đa tiêu chí Library vs P2P + Status + Condition)';
PRINT N'   - sp_GetBookDetail';
PRINT N'   - sp_SearchByISBN';
PRINT N'   - sp_AdvancedFilterBooks (sort: Title/Author/Year/Rating/Newest)';
GO
