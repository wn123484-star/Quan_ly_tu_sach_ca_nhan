/* =========================================================================================
DỰ ÁN: BOOKSHARE - NỀN TẢNG QUẢN LÝ TỦ SÁCH CÁ NHÂN & MƯỢN TRẢ SÁCH CỘNG ĐỒNG (.NET 10)
FILE: database/06_BookShare_Functions.sql
MÔ TẢ: User-Defined Functions (Scalar + Table-Valued) tái sử dụng nhiều nơi
       - fn_CalculateOverdueFine: tính phí phạt trễ hạn dựa trên số ngày quá hạn
       - fn_IsBookAvailable: kiểm tra tình trạng sẵn có của bản sao
       - fn_GetBookAvailableCopies: TVF trả về danh sách bản sao Available của 1 sách
       - fn_SearchBooksFullText: TVF tìm kiếm full-text trên Title/Author/Summary/ISBN
       - fn_GetUserTrustScore: tính điểm tin cậy của user dựa trên lịch sử mượn/trả

       TÁC GIẢ: Lê Hoàn Lâm - MSSV: BIT250210 - Lớp 25IT - Nhóm 5
       NHÁNH:   sprint2/lam-database-query
========================================================================================= */
USE [BookShareDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =========================================================================================
1. fn_CalculateOverdueFine - Scalar function tính phí phạt trễ hạn
   Công thức: DaysOverdue × FinePerDay × ConditionMultiplier
   - FinePerDay: 5.000 VND (config qua tham số)
   - ConditionMultiplier: New=1.5, Good=1.0, Fair=0.8, Worn=0.5
========================================================================================= */
CREATE OR ALTER FUNCTION [dbo].[fn_CalculateOverdueFine]
(
    @DueDate            DATETIME2(7),
    @ReturnDate         DATETIME2(7),  -- NULL nếu chưa trả (tính đến hiện tại)
    @FinePerDay         DECIMAL(18,2),  -- VND/ngày, thường là 5000
    @BookCondition      NVARCHAR(20)    -- New / Good / Fair / Worn
)
RETURNS DECIMAL(18,2)
AS
BEGIN
    DECLARE @EffectiveReturn DATETIME2(7);
    DECLARE @DaysOverdue INT;
    DECLARE @Multiplier DECIMAL(5,2);
    DECLARE @Fine DECIMAL(18,2);

    SET @EffectiveReturn = ISNULL(@ReturnDate, SYSUTCDATETIME());

    IF @EffectiveReturn <= @DueDate
        RETURN 0;

    SET @DaysOverdue = DATEDIFF(DAY, @DueDate, @EffectiveReturn);
    SET @Multiplier = CASE
        WHEN @BookCondition = N'New'   THEN 1.5
        WHEN @BookCondition = N'Good'  THEN 1.0
        WHEN @BookCondition = N'Fair'  THEN 0.8
        WHEN @BookCondition = N'Worn'  THEN 0.5
        ELSE 1.0
    END;

    SET @Fine = @DaysOverdue * @FinePerDay * @Multiplier;
    RETURN @Fine;
END
GO

/* =========================================================================================
2. fn_IsBookAvailable - Scalar function kiểm tra BookCopy có sẵn cho mượn
   Trả về 1 nếu Available, 0 nếu không
========================================================================================= */
CREATE OR ALTER FUNCTION [dbo].[fn_IsBookAvailable]
(
    @BookCopyId INT
)
RETURNS BIT
AS
BEGIN
    DECLARE @Result BIT = 0;

    SELECT @Result = CASE
        WHEN [Status] = N'Available' AND [IsLendable] = 1 THEN 1
        ELSE 0
    END
    FROM [dbo].[BookCopies]
    WHERE [Id] = @BookCopyId;

    RETURN ISNULL(@Result, 0);
END
GO

/* =========================================================================================
3. fn_GetBookAvailableCopies - Table-Valued Function
   Trả về danh sách BookCopy Available cho 1 BookId (có JOIN Library/Owner)
========================================================================================= */
CREATE OR ALTER FUNCTION [dbo].[fn_GetBookAvailableCopies]
(
    @BookId INT
)
RETURNS TABLE
AS
RETURN
(
    SELECT
        bc.[Id]              AS [BookCopyId],
        bc.[Barcode],
        bc.[OwnerType],
        bc.[LibraryId],
        l.[Name]            AS [LibraryName],
        l.[Address]         AS [LibraryAddress],
        bc.[OwnerUserId],
        u.[FullName]        AS [OwnerFullName],
        u.[TrustScore]      AS [OwnerTrustScore],
        bc.[ShelfLocation],
        bc.[Condition],
        bc.[Status]          AS [CopyStatus],
        bc.[IsLendable]
    FROM [dbo].[BookCopies] bc
    LEFT JOIN [dbo].[Libraries]   l ON bc.[LibraryId]   = l.[Id]
    LEFT JOIN [dbo].[AspNetUsers] u ON bc.[OwnerUserId] = u.[Id]
    WHERE bc.[BookId] = @BookId
      AND bc.[Status] = N'Available'
      AND bc.[IsLendable] = 1
    ORDER BY
        CASE bc.[OwnerType] WHEN N'System' THEN 0 ELSE 1 END,
        CASE bc.[Condition] WHEN N'New' THEN 0 WHEN N'Good' THEN 1 WHEN N'Fair' THEN 2 ELSE 3 END
);
GO

/* =========================================================================================
4. fn_SearchBooksFullText - Table-Valued Function
   Tìm kiếm full-text trên Title/Author/Summary/ISBN với ranking
   Tương đương EF Core .Where(b => b.Title.Contains(kw) || b.Author.Contains(kw) || ...)
   nhưng tối ưu bằng UNION ALL + ranking score (SQL Server 2022)
========================================================================================= */
CREATE OR ALTER FUNCTION [dbo].[fn_SearchBooksFullText]
(
    @Keyword NVARCHAR(250)
)
RETURNS TABLE
AS
RETURN
(
    -- Match Title: trọng số cao nhất
    SELECT
        b.[Id] AS [BookId], b.[Title], b.[Author], b.[ISBN],
        b.[CoverImageUrl], b.[CategoryId],
        100 AS [RankScore], N'Title' AS [MatchedField]
    FROM [dbo].[Books] b
    WHERE b.[Title] LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS

    UNION ALL
    -- Match Author
    SELECT
        b.[Id] AS [BookId], b.[Title], b.[Author], b.[ISBN],
        b.[CoverImageUrl], b.[CategoryId],
        80 AS [RankScore], N'Author' AS [MatchedField]
    FROM [dbo].[Books] b
    WHERE b.[Author] LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS
      AND b.[Title] NOT LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS

    UNION ALL
    -- Match ISBN: trọng số cao vì tìm chính xác
    SELECT
        b.[Id] AS [BookId], b.[Title], b.[Author], b.[ISBN],
        b.[CoverImageUrl], b.[CategoryId],
        120 AS [RankScore], N'ISBN' AS [MatchedField]
    FROM [dbo].[Books] b
    WHERE b.[ISBN] = @Keyword COLLATE Vietnamese_CI_AS

    UNION ALL
    -- Match Summary: trọng số thấp nhất
    SELECT
        b.[Id] AS [BookId], b.[Title], b.[Author], b.[ISBN],
        b.[CoverImageUrl], b.[CategoryId],
        50 AS [RankScore], N'Summary' AS [MatchedField]
    FROM [dbo].[Books] b
    WHERE b.[Summary] LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS
      AND b.[Title]  NOT LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS
      AND b.[Author] NOT LIKE N'%' + @Keyword + N'%' COLLATE Vietnamese_CI_AS
);
GO

/* =========================================================================================
5. fn_GetUserTrustScore - Scalar function tính điểm tin cậy động của user
   Công thức:
     Base = 100 (mặc định)
     + 1 cho mỗi lượt mượn trả đúng hạn
     - 3 cho mỗi lượt trả trễ
     - 10 cho mỗi lượt làm mất/hư hỏng
     - 5 cho mỗi khiếu nại bị Admin resolve không có lợi
   Giới hạn trong [0, 200]
========================================================================================= */
CREATE OR ALTER FUNCTION [dbo].[fn_GetUserTrustScore]
(
    @UserId NVARCHAR(450)
)
RETURNS INT
AS
BEGIN
    DECLARE @BaseScore INT = 100;
    DECLARE @OnTimeReturns INT = 0;
    DECLARE @LateReturns INT = 0;
    DECLARE @LostOrDamaged INT = 0;
    DECLARE @ResolvedDisputes INT = 0;
    DECLARE @FinalScore INT;

    SELECT
        @OnTimeReturns   = SUM(CASE WHEN l.[Status] = N'Returned' AND l.[ReturnDate] <= l.[DueDate] THEN 1 ELSE 0 END),
        @LateReturns     = SUM(CASE WHEN l.[Status] IN (N'Returned', N'Overdue') AND (l.[ReturnDate] > l.[DueDate] OR l.[Status] = N'Overdue') THEN 1 ELSE 0 END),
        @LostOrDamaged   = SUM(CASE WHEN l.[Status] IN (N'Lost') THEN 1 ELSE 0 END)
    FROM [dbo].[Loans] l
    INNER JOIN [dbo].[LoanRequests] lr ON l.[LoanRequestId] = lr.[Id]
    WHERE lr.[BorrowerId] = @UserId;

    SELECT @ResolvedDisputes = COUNT(*)
    FROM [dbo].[Disputes] d
    INNER JOIN [dbo].[Loans] l ON d.[LoanId] = l.[Id]
    INNER JOIN [dbo].[LoanRequests] lr ON l.[LoanRequestId] = lr.[Id]
    WHERE lr.[BorrowerId] = @UserId
      AND d.[Status] = N'Resolved';

    SET @FinalScore = @BaseScore
                    + ISNULL(@OnTimeReturns, 0)
                    - ISNULL(@LateReturns, 0)   * 3
                    - ISNULL(@LostOrDamaged, 0) * 10
                    - @ResolvedDisputes * 5;

    SET @FinalScore = CASE
        WHEN @FinalScore < 0   THEN 0
        WHEN @FinalScore > 200 THEN 200
        ELSE @FinalScore
    END;

    RETURN @FinalScore;
END
GO

/* =========================================================================================
6. fn_GetLoanDuration - Scalar function tính số ngày thực tế của một loan
   Trả về số ngày từ StartDate đến ReturnDate (hoặc hiện tại nếu chưa trả)
========================================================================================= */
CREATE OR ALTER FUNCTION [dbo].[fn_GetLoanDuration]
(
    @LoanId INT
)
RETURNS INT
AS
BEGIN
    DECLARE @StartDate DATETIME2(7);
    DECLARE @ReturnDate DATETIME2(7);

    SELECT
        @StartDate  = l.[StartDate],
        @ReturnDate = l.[ReturnDate]
    FROM [dbo].[Loans] l
    WHERE l.[Id] = @LoanId;

    RETURN DATEDIFF(DAY, @StartDate, ISNULL(@ReturnDate, SYSUTCDATETIME()));
END
GO

/* =========================================================================================
7. fn_IsBookCopyEligibleForP2PLoan - Kiểm tra BookCopy có hợp lệ cho P2P loan
   Trả về 1 nếu: OwnerType = User, Status = Available, IsLendable = 1, Owner có TrustScore >= 50
========================================================================================= */
CREATE OR ALTER FUNCTION [dbo].[fn_IsBookCopyEligibleForP2PLoan]
(
    @BookCopyId INT
)
RETURNS BIT
AS
BEGIN
    DECLARE @Result BIT = 0;
    DECLARE @OwnerTrustScore INT = 0;
    DECLARE @OwnerType NVARCHAR(20);
    DECLARE @Status    NVARCHAR(20);
    DECLARE @IsLendable BIT;

    SELECT
        @OwnerType       = bc.[OwnerType],
        @Status          = bc.[Status],
        @IsLendable      = bc.[IsLendable],
        @OwnerTrustScore = ISNULL(u.[TrustScore], 0)
    FROM [dbo].[BookCopies] bc
    LEFT JOIN [dbo].[AspNetUsers] u ON bc.[OwnerUserId] = u.[Id]
    WHERE bc.[Id] = @BookCopyId;

    IF @OwnerType = N'User' AND @Status = N'Available' AND @IsLendable = 1 AND @OwnerTrustScore >= 50
        SET @Result = 1;

    RETURN @Result;
END
GO

PRINT N'== [06_BookShare_Functions.sql] Đã tạo 7 Functions ==';
PRINT N'   - fn_CalculateOverdueFine (Scalar)';
PRINT N'   - fn_IsBookAvailable (Scalar)';
PRINT N'   - fn_GetBookAvailableCopies (TVF)';
PRINT N'   - fn_SearchBooksFullText (TVF - ranking)';
PRINT N'   - fn_GetUserTrustScore (Scalar - dynamic)';
PRINT N'   - fn_GetLoanDuration (Scalar)';
PRINT N'   - fn_IsBookCopyEligibleForP2PLoan (Scalar)';
GO
