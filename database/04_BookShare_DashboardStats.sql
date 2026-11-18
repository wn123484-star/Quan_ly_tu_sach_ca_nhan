/* =========================================================================================
DỰ ÁN: BOOKSHARE - NỀN TẢNG QUẢN LÝ TỦ SÁCH CÁ NHÂN & MƯỢN TRẢ SÁCH CỘNG ĐỒNG (.NET 10)
FILE: database/04_BookShare_DashboardStats.sql
MÔ TẢ: Stored Procedures cho truy vấn tổng hợp thống kê Dashboard
       Phục vụ Dịch vụ S03 (Reading Tracker) và S22 (Admin Analytics)
       Dùng GROUP BY, SUM, COUNT, AVG, CTE và Window Functions

       TÁC GIẢ: Lê Hoàn Lâm - MSSV: BIT210210 - Lớp 25IT - Nhóm 5
       NHÁNH:   sprint2/lam-database-query
       ISSUE:   #8 Sprint2 - Truy vấn thống kê & Biểu đồ
========================================================================================= */
USE [BookShareDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =========================================================================================
1. sp_GetReadingTrackerStats - Thống kê Reading Progress (S03) cho user
   Số liệu cho Chart.js:
     - Doughnut: Số sách theo trạng thái WantToRead / Reading / Completed
     - Bar:     Top 5 sách đang đọc (tỷ lệ % tiến độ)
     - KPI:     Tổng giờ đọc ước tính, số sách đã đọc xong, trung điểm đánh giá
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_GetReadingTrackerStats]
    @UserId NVARCHAR(450)
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. KPI tổng quan
    SELECT
        COUNT(*) AS [TotalBooks],
        SUM(CASE WHEN [Status] = N'WantToRead' THEN 1 ELSE 0 END) AS [WantToReadCount],
        SUM(CASE WHEN [Status] = N'Reading'    THEN 1 ELSE 0 END) AS [ReadingCount],
        SUM(CASE WHEN [Status] = N'Completed'  THEN 1 ELSE 0 END) AS [CompletedCount],
        SUM([CurrentPage]) AS [TotalPagesRead],
        AVG(CASE WHEN [Status] = N'Completed' THEN CAST([PersonalRating] AS FLOAT) ELSE NULL END) AS [AvgPersonalRating],
        SUM(CASE WHEN [Priority] = N'High'   THEN 1 ELSE 0 END) AS [HighPriorityCount]
    FROM [dbo].[ReadingProgresses]
    WHERE [UserId] = @UserId;

    -- 2. Doughnut chart - phân bố theo trạng thái
    SELECT
        [Status] AS [StatusName],
        COUNT(*) AS [Count]
    FROM [dbo].[ReadingProgresses]
    WHERE [UserId] = @UserId
    GROUP BY [Status]
    ORDER BY
        CASE [Status]
            WHEN N'WantToRead' THEN 1
            WHEN N'Reading'    THEN 2
            WHEN N'Completed'  THEN 3
            ELSE 4
        END;

    -- 3. Bar chart - Top 5 sách đang đọc (tỷ lệ % tiến độ)
    SELECT TOP 5
        rp.[BookId],
        b.[Title],
        b.[Author],
        b.[CoverImageUrl],
        rp.[CurrentPage],
        rp.[TotalPages],
        CAST(rp.[CurrentPage] * 100.0 / rp.[TotalPages] AS DECIMAL(5,2)) AS [ProgressPercent],
        rp.[Priority],
        rp.[TargetDeadline]
    FROM [dbo].[ReadingProgresses] rp
    INNER JOIN [dbo].[Books] b ON rp.[BookId] = b.[Id]
    WHERE rp.[UserId] = @UserId AND rp.[Status] = N'Reading'
    ORDER BY
        CASE rp.[Priority] WHEN N'High' THEN 1 WHEN N'Medium' THEN 2 ELSE 3 END,
        rp.[UpdatedAt] DESC;

    -- 4. Line chart - Số sách hoàn thành theo tháng (12 tháng gần nhất)
    SELECT
        FORMAT([FinishedAt], N'yyyy-MM') AS [Month],
        COUNT(*) AS [CompletedCount]
    FROM [dbo].[ReadingProgresses]
    WHERE [UserId] = @UserId
      AND [Status] = N'Completed'
      AND [FinishedAt] IS NOT NULL
      AND [FinishedAt] >= DATEADD(MONTH, -12, SYSUTCDATETIME())
    GROUP BY FORMAT([FinishedAt], N'yyyy-MM')
    ORDER BY [Month] ASC;
END
GO

/* =========================================================================================
2. sp_GetAdminAnalyticsOverview - KPI Dashboard tổng quan Admin (S22)
   Trả về: tổng số user, sách, bản sao, đơn mượn, doanh thu, phí phạt
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_GetAdminAnalyticsOverview]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        (SELECT COUNT(*) FROM [dbo].[AspNetUsers])                                          AS [TotalUsers],
        (SELECT COUNT(*) FROM [dbo].[AspNetUsers]
         WHERE [CreatedAt] >= DATEADD(DAY, -30, SYSUTCDATETIME()))                          AS [NewUsers30Days],
        (SELECT COUNT(*) FROM [dbo].[Books])                                                AS [TotalBooks],
        (SELECT COUNT(*) FROM [dbo].[BookCopies])                                           AS [TotalBookCopies],
        (SELECT COUNT(*) FROM [dbo].[BookCopies] WHERE [Status] = N'Available')             AS [AvailableCopies],
        (SELECT COUNT(*) FROM [dbo].[BookCopies] WHERE [Status] = N'Borrowed')             AS [BorrowedCopies],
        (SELECT COUNT(*) FROM [dbo].[BookCopies] WHERE [Status] = N'Lost')                  AS [LostCopies],
        (SELECT COUNT(*) FROM [dbo].[Loans] WHERE [Status] = N'Active')                    AS [ActiveLoans],
        (SELECT COUNT(*) FROM [dbo].[Loans] WHERE [Status] = N'Overdue')                   AS [OverdueLoans],
        (SELECT COUNT(*) FROM [dbo].[LoanRequests] WHERE [Status] = N'Pending')            AS [PendingRequests],
        (SELECT ISNULL(SUM([Amount]), 0) FROM [dbo].[Transactions]
         WHERE [Status] = N'Completed')                                                     AS [TotalRevenue],
        (SELECT ISNULL(SUM([FineAmount]), 0) FROM [dbo].[Loans]
         WHERE [Status] IN (N'Returned', N'Overdue'))                                      AS [TotalFines],
        (SELECT COUNT(*) FROM [dbo].[Memberships] WHERE [Tier] = N'Premium' AND [IsActive] = 1) AS [ActivePremiumMembers],
        (SELECT COUNT(*) FROM [dbo].[Reviews] WHERE [IsPublic] = 1)                         AS [PublicReviews],
        (SELECT COUNT(*) FROM [dbo].[Disputes] WHERE [Status] = N'Open')                    AS [OpenDisputes];
END
GO

/* =========================================================================================
3. sp_GetLoanTypeRatio - Tỷ lệ mượn Kho chung (Library) vs P2P (User)
   Cho Doughnut Chart trên Dashboard Admin (S22)
   Trả về: LoanType, Count, Percent trong khoảng thời gian @From - @To
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_GetLoanTypeRatio]
    @FromDate DATETIME2(7) = NULL,
    @ToDate   DATETIME2(7) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @FromDate IS NULL SET @FromDate = DATEADD(MONTH, -6, SYSUTCDATETIME());
    IF @ToDate   IS NULL SET @ToDate   = SYSUTCDATETIME();

    DECLARE @GrandTotal INT;

    SELECT @GrandTotal = COUNT(*)
    FROM [dbo].[LoanRequests] lr
    WHERE lr.[RequestedAt] BETWEEN @FromDate AND @ToDate
      AND lr.[Status] IN (N'Approved', N'Pending');

    SELECT
        lr.[LoanType],
        COUNT(*) AS [Count],
        CASE WHEN @GrandTotal > 0
             THEN CAST(COUNT(*) * 100.0 / @GrandTotal AS DECIMAL(5,2))
             ELSE 0 END AS [Percent],
        @GrandTotal AS [TotalLoans]
    FROM [dbo].[LoanRequests] lr
    WHERE lr.[RequestedAt] BETWEEN @FromDate AND @ToDate
      AND lr.[Status] IN (N'Approved', N'Pending')
    GROUP BY lr.[LoanType]
    ORDER BY [Count] DESC;
END
GO

/* =========================================================================================
4. sp_GetMonthlyRevenue - Doanh thu theo tháng (Bar Chart cho S22)
   Gộp doanh thu Premium membership + phí phạt trễ hạn
   Trả về: Month, MembershipRevenue, FineRevenue, Total
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_GetMonthlyRevenue]
    @MonthsBack INT = 12
AS
BEGIN
    SET NOCOUNT ON;
    SET @MonthsBack = IIF(@MonthsBack < 1, 12, @MonthsBack);

    DECLARE @StartDate DATETIME2(7) = DATEADD(MONTH, -@MonthsBack, DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1));

    ;WITH Months AS (
        SELECT TOP (@MonthsBack)
            DATEADD(MONTH, -number, DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1)) AS [MonthStart]
        FROM master.dbo.spt_values
        WHERE type = N'P' AND number BETWEEN 0 AND @MonthsBack - 1
    ),
    MembershipRevenue AS (
        SELECT
            DATEFROMPARTS(YEAR([PaidAt]), MONTH([PaidAt]), 1) AS [MonthStart],
            SUM([Amount]) AS [Total]
        FROM [dbo].[Transactions]
        WHERE [Status] = N'Completed'
          AND [MembershipId] IS NOT NULL
          AND [PaidAt] IS NOT NULL
          AND [PaidAt] >= @StartDate
        GROUP BY DATEFROMPARTS(YEAR([PaidAt]), MONTH([PaidAt]), 1)
    ),
    FineRevenue AS (
        SELECT
            DATEFROMPARTS(YEAR(l.[ReturnDate]), MONTH(l.[ReturnDate]), 1) AS [MonthStart],
            SUM(l.[FineAmount]) AS [Total]
        FROM [dbo].[Loans] l
        WHERE l.[FineAmount] > 0
          AND l.[ReturnDate] IS NOT NULL
          AND l.[ReturnDate] >= @StartDate
        GROUP BY DATEFROMPARTS(YEAR(l.[ReturnDate]), MONTH(l.[ReturnDate]), 1)
    )
    SELECT
        FORMAT(m.[MonthStart], N'yyyy-MM') AS [Month],
        ISNULL(mr.[Total], 0)             AS [MembershipRevenue],
        ISNULL(fr.[Total], 0)              AS [FineRevenue],
        ISNULL(mr.[Total], 0) + ISNULL(fr.[Total], 0) AS [TotalRevenue]
    FROM Months m
    LEFT JOIN MembershipRevenue mr ON m.[MonthStart] = mr.[MonthStart]
    LEFT JOIN FineRevenue       fr ON m.[MonthStart] = fr.[MonthStart]
    ORDER BY m.[MonthStart] ASC;
END
GO

/* =========================================================================================
5. sp_GetTopBooksByLoans - Top sách được mượn nhiều nhất (Bar Chart cho S22)
   Trả về: BookId, Title, Author, TotalLoans, TotalCopies, AvgRating
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_GetTopBooksByLoans]
    @Top       INT = 10,
    @MonthsBack INT = 6
AS
BEGIN
    SET NOCOUNT ON;
    SET @Top       = IIF(@Top < 1, 10, @Top);
    SET @MonthsBack = IIF(@MonthsBack < 1, 6, @MonthsBack);

    SELECT TOP (@Top)
        b.[Id] AS [BookId],
        b.[Title],
        b.[Author],
        b.[CoverImageUrl],
        COUNT(DISTINCT l.[Id]) AS [TotalLoans],
        COUNT(DISTINCT bc.[Id]) AS [TotalCopies],
        AVG(CAST(r.[Rating] AS FLOAT)) AS [AvgRating]
    FROM [dbo].[Loans] l
    INNER JOIN [dbo].[LoanRequests] lr ON l.[LoanRequestId] = lr.[Id]
    INNER JOIN [dbo].[BookCopies]    bc ON lr.[BookCopyId] = bc.[Id]
    INNER JOIN [dbo].[Books]         b  ON bc.[BookId] = b.[Id]
    LEFT  JOIN [dbo].[Reviews]       r  ON r.[BookId] = b.[Id] AND r.[IsPublic] = 1
    WHERE l.[StartDate] >= DATEADD(MONTH, -@MonthsBack, SYSUTCDATETIME())
    GROUP BY b.[Id], b.[Title], b.[Author], b.[CoverImageUrl]
    ORDER BY [TotalLoans] DESC, [AvgRating] DESC;
END
GO

/* =========================================================================================
6. sp_GetCategoryStats - Thống kê theo thể loại (cho Pie/Doughnut Chart)
   Trả về: CategoryId, CategoryName, TotalBooks, TotalCopies, TotalLoans, AvgRating
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_GetCategoryStats]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.[Id] AS [CategoryId],
        c.[Name] AS [CategoryName],
        c.[Slug],
        COUNT(DISTINCT b.[Id]) AS [TotalBooks],
        COUNT(DISTINCT bc.[Id]) AS [TotalCopies],
        COUNT(DISTINCT l.[Id]) AS [TotalLoans],
        AVG(CAST(r.[Rating] AS FLOAT)) AS [AvgRating]
    FROM [dbo].[Categories] c
    LEFT JOIN [dbo].[Books]         b  ON b.[CategoryId] = c.[Id]
    LEFT JOIN [dbo].[BookCopies]    bc ON bc.[BookId] = b.[Id]
    LEFT JOIN [dbo].[LoanRequests] lr ON lr.[BookCopyId] = bc.[Id] AND lr.[Status] IN (N'Approved', N'Pending')
    LEFT JOIN [dbo].[Loans]          l ON l.[LoanRequestId] = lr.[Id]
    LEFT JOIN [dbo].[Reviews]        r  ON r.[BookId] = b.[Id] AND r.[IsPublic] = 1
    GROUP BY c.[Id], c.[Name], c.[Slug]
    ORDER BY [TotalBooks] DESC, [TotalCopies] DESC;
END
GO

/* =========================================================================================
7. sp_GetLibraryActivityStats - Thống kê hoạt động theo thư viện / Hub (S20)
   Cho Bar chart: tên thư viện, tổng bản sao, bản sao đang mượn, hoạt động 30 ngày
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_GetLibraryActivityStats]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        l.[Id] AS [LibraryId],
        l.[Name] AS [LibraryName],
        l.[HubType],
        COUNT(DISTINCT bc.[Id]) AS [TotalCopies],
        SUM(CASE WHEN bc.[Status] = N'Available' THEN 1 ELSE 0 END) AS [AvailableCopies],
        SUM(CASE WHEN bc.[Status] = N'Borrowed' THEN 1 ELSE 0 END)  AS [BorrowedCopies],
        (
            SELECT COUNT(DISTINCT lr.[Id])
            FROM [dbo].[LoanRequests] lr
            WHERE lr.[PickupHubId] = l.[Id]
              AND lr.[RequestedAt] >= DATEADD(DAY, -30, SYSUTCDATETIME())
        ) AS [PickupRequests30Days]
    FROM [dbo].[Libraries] l
    LEFT JOIN [dbo].[BookCopies] bc ON bc.[LibraryId] = l.[Id]
    WHERE l.[IsActive] = 1
    GROUP BY l.[Id], l.[Name], l.[HubType]
    ORDER BY [TotalCopies] DESC;
END
GO

/* =========================================================================================
8. sp_GetOverdueLoansReport - Danh sách mượn quá hạn cho Dashboard Admin
   Trả về: LoanId, BorrowerFullName, BookTitle, DueDate, DaysOverdue, EstimatedFine
   Dùng bảng tạm + CTE để tính fine động
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_GetOverdueLoansReport]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @FinePerDay DECIMAL(18,2) = 5000;  -- 5.000 VND/ngày (config trong app)

    SELECT
        l.[Id] AS [LoanId],
        lr.[BorrowerId],
        u.[FullName] AS [BorrowerFullName],
        u.[Email] AS [BorrowerEmail],
        bc.[Barcode],
        b.[Title] AS [BookTitle],
        b.[Author],
        l.[StartDate],
        l.[DueDate],
        DATEDIFF(DAY, l.[DueDate], SYSUTCDATETIME()) AS [DaysOverdue],
        l.[FineAmount],
        ISNULL(l.[FineAmount], DATEDIFF(DAY, l.[DueDate], SYSUTCDATETIME()) * @FinePerDay) AS [EstimatedFine],
        l.[Status]
    FROM [dbo].[Loans] l
    INNER JOIN [dbo].[LoanRequests] lr ON l.[LoanRequestId] = lr.[Id]
    INNER JOIN [dbo].[BookCopies]    bc ON lr.[BookCopyId] = bc.[Id]
    INNER JOIN [dbo].[Books]          b  ON bc.[BookId] = b.[Id]
    INNER JOIN [dbo].[AspNetUsers]    u  ON lr.[BorrowerId] = u.[Id]
    WHERE l.[Status] IN (N'Active', N'Overdue')
      AND l.[DueDate] < SYSUTCDATETIME()
    ORDER BY l.[DueDate] ASC;
END
GO

/* =========================================================================================
9. sp_GetUserLoanStats - Thống kê hoạt động mượn của 1 user (cho trang Profile S01)
========================================================================================= */
CREATE OR ALTER PROCEDURE [dbo].[sp_GetUserLoanStats]
    @UserId NVARCHAR(450)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        COUNT(*) AS [TotalLoanRequests],
        SUM(CASE WHEN lr.[Status] = N'Approved'  THEN 1 ELSE 0 END) AS [ApprovedRequests],
        SUM(CASE WHEN lr.[Status] = N'Pending'   THEN 1 ELSE 0 END) AS [PendingRequests],
        SUM(CASE WHEN lr.[Status] = N'Rejected'  THEN 1 ELSE 0 END) AS [RejectedRequests],
        SUM(CASE WHEN lr.[LoanType] = N'Library' THEN 1 ELSE 0 END) AS [LibraryLoans],
        SUM(CASE WHEN lr.[LoanType] = N'P2P'     THEN 1 ELSE 0 END) AS [P2PLoans],
        (SELECT COUNT(*) FROM [dbo].[Loans] l
         INNER JOIN [dbo].[LoanRequests] lr2 ON l.[LoanRequestId] = lr2.[Id]
         WHERE lr2.[BorrowerId] = @UserId AND l.[Status] = N'Active') AS [ActiveLoans],
        (SELECT COUNT(*) FROM [dbo].[Loans] l
         INNER JOIN [dbo].[LoanRequests] lr3 ON l.[LoanRequestId] = lr3.[Id]
         WHERE lr3.[BorrowerId] = @UserId AND l.[Status] = N'Overdue') AS [OverdueLoans],
        (SELECT ISNULL(SUM(l2.[FineAmount]), 0) FROM [dbo].[Loans] l2
         INNER JOIN [dbo].[LoanRequests] lr4 ON l2.[LoanRequestId] = lr4.[Id]
         WHERE lr4.[BorrowerId] = @UserId) AS [TotalFinesPaid],
        (SELECT COUNT(*) FROM [dbo].[Reviews] r WHERE r.[UserId] = @UserId AND r.[IsPublic] = 1) AS [PublicReviews]
    FROM [dbo].[LoanRequests] lr
    WHERE lr.[BorrowerId] = @UserId;
END
GO

PRINT N'== [04_BookShare_DashboardStats.sql] Đã tạo 9 Stored Procedures ==';
PRINT N'   - sp_GetReadingTrackerStats (S03 - 4 resultsets: KPI/Doughnut/Bar/Line)';
PRINT N'   - sp_GetAdminAnalyticsOverview (S22 - 15 KPIs)';
PRINT N'   - sp_GetLoanTypeRatio (Library vs P2P)';
PRINT N'   - sp_GetMonthlyRevenue (Membership + Fine revenue)';
PRINT N'   - sp_GetTopBooksByLoans (Top sách mượn nhiều)';
PRINT N'   - sp_GetCategoryStats (Pie chart thể loại)';
PRINT N'   - sp_GetLibraryActivityStats (Bar chart Hub)';
PRINT N'   - sp_GetOverdueLoansReport (Danh sách quá hạn + fine)';
PRINT N'   - sp_GetUserLoanStats (Profile S01)';
GO
