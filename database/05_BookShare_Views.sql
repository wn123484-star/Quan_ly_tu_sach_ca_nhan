/* =========================================================================================
DỰ ÁN: BOOKSHARE - NỀN TẢNG QUẢN LÝ TỦ SÁCH CÁ NHÂN & MƯỢN TRẢ SÁCH CỘNG ĐỒNG (.NET 10)
FILE: database/05_BookShare_Views.sql
MÔ TẢ: Tập hợp Views tổng hợp phục vụ truy vấn nhanh và Dashboard (S03/S22)
       - View đóng gói JOIN phức tạp + tính toán cột dẫn xuất (AvailableCopies, AvgRating)
       - Code C# EF Core có thể scaffold View thành entity chỉ-đọc để LINQ truy vấn
       - Dùng Indexed View + NOEXPAND cho một số view khi cần tối ưu cao

       TÁC GIẢ: Lê Hoàn Lâm - MSSV: BIT250210 - Lớp 25IT - Nhóm 5
       NHÁNH:   sprint2/lam-database-query
========================================================================================= */
USE [BookShareDb];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =========================================================================================
1. vw_BooksForSearch - View tổng hợp đầu sách + thống kê bản sao + đánh giá
   Phục vụ trang Search Discovery (S06) và Library Catalog (S07)
   EF Core: Scaffold thành entity BooksForSearch (NoKey - read-only)
========================================================================================= */
CREATE OR ALTER VIEW [dbo].[vw_BooksForSearch]
AS
SELECT
    b.[Id]                AS [BookId],
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
    -- Số bản sao theo tình trạng (subquery dùng IX_BookCopies_BookId_Status)
    ISNULL((
        SELECT COUNT(*)
        FROM [dbo].[BookCopies] bc
        WHERE bc.[BookId] = b.[Id]
    ), 0) AS [TotalCopies],
    ISNULL((
        SELECT COUNT(*)
        FROM [dbo].[BookCopies] bc
        WHERE bc.[BookId] = b.[Id] AND bc.[Status] = N'Available' AND bc.[IsLendable] = 1
    ), 0) AS [AvailableCopies],
    ISNULL((
        SELECT COUNT(*)
        FROM [dbo].[BookCopies] bc
        WHERE bc.[BookId] = b.[Id] AND bc.[Status] = N'Borrowed'
    ), 0) AS [BorrowedCopies],
    ISNULL((
        SELECT COUNT(*)
        FROM [dbo].[BookCopies] bc
        WHERE bc.[BookId] = b.[Id] AND bc.[OwnerType] = N'System'
    ), 0) AS [SystemCopies],
    ISNULL((
        SELECT COUNT(*)
        FROM [dbo].[BookCopies] bc
        WHERE bc.[BookId] = b.[Id] AND bc.[OwnerType] = N'User'
    ), 0) AS [P2PCopies],
    -- Đánh giá trung bình (dùng IX_Reviews_BookId_Rating)
    ISNULL((
        SELECT AVG(CAST(r.[Rating] AS FLOAT))
        FROM [dbo].[Reviews] r
        WHERE r.[BookId] = b.[Id] AND r.[IsPublic] = 1
    ), 0) AS [AverageRating],
    ISNULL((
        SELECT COUNT(*)
        FROM [dbo].[Reviews] r
        WHERE r.[BookId] = b.[Id] AND r.[IsPublic] = 1
    ), 0) AS [ReviewCount]
FROM [dbo].[Books] b
INNER JOIN [dbo].[Categories] c ON b.[CategoryId] = c.[Id];
GO

/* =========================================================================================
2. vw_BookCopiesForCatalog - View bản sao sách đầy đủ cho trang Library Catalog (S07)
   JOIN Book + Category + Library + OwnerUser
========================================================================================= */
CREATE OR ALTER VIEW [dbo].[vw_BookCopiesForCatalog]
AS
SELECT
    bc.[Id]              AS [BookCopyId],
    bc.[Barcode],
    bc.[OwnerType],
    bc.[Condition],
    bc.[Status]         AS [CopyStatus],
    bc.[IsLendable],
    bc.[ShelfLocation],
    bc.[CreatedAt]      AS [CopyCreatedAt],
    b.[Id]              AS [BookId],
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
    bc.[LibraryId],
    l.[Name]            AS [LibraryName],
    l.[Address]         AS [LibraryAddress],
    l.[HubType],
    bc.[OwnerUserId],
    u.[FullName]        AS [OwnerFullName],
    u.[AvatarUrl]       AS [OwnerAvatarUrl],
    u.[TrustScore]      AS [OwnerTrustScore]
FROM [dbo].[BookCopies] bc
INNER JOIN [dbo].[Books] b        ON bc.[BookId]     = b.[Id]
INNER JOIN [dbo].[Categories] c  ON b.[CategoryId]   = c.[Id]
LEFT  JOIN [dbo].[Libraries] l    ON bc.[LibraryId]   = l.[Id]
LEFT  JOIN [dbo].[AspNetUsers] u  ON bc.[OwnerUserId] = u.[Id];
GO

/* =========================================================================================
3. vw_ActiveLoans - View các hợp đồng mượn đang hiệu lực + thông tin đầy đủ
   Phục vụ trang quản lý mượn/trả (S08, S09) và Dashboard (S22)
========================================================================================= */
CREATE OR ALTER VIEW [dbo].[vw_ActiveLoans]
AS
SELECT
    l.[Id]               AS [LoanId],
    l.[LoanRequestId],
    l.[StartDate],
    l.[DueDate],
    l.[ReturnDate],
    l.[RenewCount],
    l.[FineAmount],
    l.[Status]          AS [LoanStatus],
    CASE
        WHEN l.[ReturnDate] IS NULL AND l.[DueDate] < SYSUTCDATETIME() THEN N'Overdue'
        ELSE l.[Status]
    END                  AS [EffectiveStatus],
    DATEDIFF(DAY, l.[StartDate], ISNULL(l.[ReturnDate], SYSUTCDATETIME())) AS [ElapsedDays],
    CASE
        WHEN l.[ReturnDate] IS NULL AND l.[DueDate] < SYSUTCDATETIME()
            THEN DATEDIFF(DAY, l.[DueDate], SYSUTCDATETIME())
        ELSE 0
    END                  AS [DaysOverdue],
    lr.[BorrowerId],
    ub.[FullName]        AS [BorrowerFullName],
    ub.[Email]           AS [BorrowerEmail],
    lr.[ApproverId],
    ua.[FullName]        AS [ApproverFullName],
    lr.[LoanType],
    lr.[PickupHubId],
    lr.[DesiredDays],
    bc.[Id]              AS [BookCopyId],
    bc.[Barcode],
    b.[Id]               AS [BookId],
    b.[Title],
    b.[Author],
    b.[CoverImageUrl],
    bc.[Condition]       AS [CopyCondition],
    bc.[OwnerType]
FROM [dbo].[Loans] l
INNER JOIN [dbo].[LoanRequests] lr ON l.[LoanRequestId] = lr.[Id]
INNER JOIN [dbo].[BookCopies]    bc ON lr.[BookCopyId] = bc.[Id]
INNER JOIN [dbo].[Books]         b  ON bc.[BookId] = b.[Id]
LEFT  JOIN [dbo].[AspNetUsers]   ub ON lr.[BorrowerId] = ub.[Id]
LEFT  JOIN [dbo].[AspNetUsers]   ua ON lr.[ApproverId] = ua.[Id];
GO

/* =========================================================================================
4. vw_UserReadingProgress - View tiến độ đọc của user (Dịch vụ S03)
   JOIN với Books + Categories + Reviews
========================================================================================= */
CREATE OR ALTER VIEW [dbo].[vw_UserReadingProgress]
AS
SELECT
    rp.[Id]              AS [ReadingProgressId],
    rp.[UserId],
    u.[FullName]         AS [UserFullName],
    rp.[BookId],
    b.[Title],
    b.[Author],
    b.[CoverImageUrl],
    b.[ISBN],
    b.[PageCount]        AS [BookPageCount],
    b.[CategoryId],
    c.[Name]            AS [CategoryName],
    rp.[CurrentPage],
    rp.[TotalPages],
    CAST(rp.[CurrentPage] * 100.0 / rp.[TotalPages] AS DECIMAL(5,2)) AS [ProgressPercent],
    rp.[Priority],
    rp.[TargetDeadline],
    rp.[Status]          AS [ReadingStatus],
    rp.[StartedAt],
    rp.[FinishedAt],
    rp.[PersonalRating],
    rp.[UpdatedAt],
    CASE
        WHEN rp.[Status] = N'Completed' THEN 100
        WHEN rp.[TargetDeadline] IS NOT NULL AND rp.[TargetDeadline] < CAST(SYSUTCDATETIME() AS DATE) THEN -1
        ELSE CAST(rp.[CurrentPage] * 100.0 / rp.[TotalPages] AS INT)
    END                  AS [ProgressPercentInt]
FROM [dbo].[ReadingProgresses] rp
INNER JOIN [dbo].[AspNetUsers] u ON rp.[UserId] = u.[Id]
INNER JOIN [dbo].[Books]        b ON rp.[BookId] = b.[Id]
INNER JOIN [dbo].[Categories]  c ON b.[CategoryId] = c.[Id];
GO

/* =========================================================================================
5. vw_AdminDashboardSummary - View tổng hợp các KPI Dashboard Admin (S22)
   Trả về 1 dòng duy nhất: TotalUsers, TotalBooks, ActiveLoans, OverdueLoans, Revenue...
   Phục vụ cho trang Admin Dashboard KPI cards
========================================================================================= */
CREATE OR ALTER VIEW [dbo].[vw_AdminDashboardSummary]
AS
SELECT
    (SELECT COUNT(*) FROM [dbo].[AspNetUsers])                                                              AS [TotalUsers],
    (SELECT COUNT(*) FROM [dbo].[AspNetUsers] WHERE [CreatedAt] >= DATEADD(DAY, -30, SYSUTCDATETIME()))     AS [NewUsers30Days],
    (SELECT COUNT(*) FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'ADMIN')                            AS [AdminCount],
    (SELECT COUNT(*) FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'LIBRARIAN')                          AS [LibrarianCount],
    (SELECT COUNT(*) FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'CURATOR')                            AS [CuratorCount],
    (SELECT COUNT(*) FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'USER')                                AS [UserCount],
    (SELECT COUNT(*) FROM [dbo].[Books])                                                                       AS [TotalBooks],
    (SELECT COUNT(*) FROM [dbo].[BookCopies])                                                                  AS [TotalBookCopies],
    (SELECT COUNT(*) FROM [dbo].[BookCopies] WHERE [Status] = N'Available')                                   AS [AvailableCopies],
    (SELECT COUNT(*) FROM [dbo].[BookCopies] WHERE [Status] = N'Borrowed')                                   AS [BorrowedCopies],
    (SELECT COUNT(*) FROM [dbo].[BookCopies] WHERE [Status] = N'Lost')                                        AS [LostCopies],
    (SELECT COUNT(*) FROM [dbo].[Loans] WHERE [Status] = N'Active')                                           AS [ActiveLoans],
    (SELECT COUNT(*) FROM [dbo].[Loans] WHERE [Status] = N'Overdue'])                                        AS [OverdueLoans],
    (SELECT COUNT(*) FROM [dbo].[Loans] WHERE [Status] = N'Returned'])                                       AS [ReturnedLoans],
    (SELECT COUNT(*) FROM [dbo].[LoanRequests] WHERE [Status] = N'Pending')                                  AS [PendingRequests],
    (SELECT COUNT(*) FROM [dbo].[Memberships] WHERE [Tier] = N'Premium' AND [IsActive] = 1)                  AS [PremiumMembers],
    (SELECT ISNULL(SUM([Amount]), 0) FROM [dbo].[Transactions] WHERE [Status] = N'Completed')               AS [TotalRevenue],
    (SELECT ISNULL(SUM([FineAmount]), 0) FROM [dbo].[Loans] WHERE [Status] IN (N'Returned', N'Overdue'))   AS [TotalFines],
    (SELECT COUNT(*) FROM [dbo].[Reviews] WHERE [IsPublic] = 1)                                               AS [PublicReviews],
    (SELECT COUNT(*) FROM [dbo].[Disputes] WHERE [Status] = N'Open')                                          AS [OpenDisputes],
    (SELECT COUNT(*) FROM [dbo].[Reservations] WHERE [Status] = N'Waiting')                                  AS [WaitingReservations];
GO

/* =========================================================================================
6. vw_LoanRequestsWithDetails - View danh sách LoanRequest đầy đủ thông tin
   Dùng cho trang Admin quản lý yêu cầu mượn (S20)
========================================================================================= */
CREATE OR ALTER VIEW [dbo].[vw_LoanRequestsWithDetails]
AS
SELECT
    lr.[Id]              AS [LoanRequestId],
    lr.[BookCopyId],
    bc.[Barcode],
    bc.[Condition]      AS [CopyCondition],
    b.[Id]               AS [BookId],
    b.[Title],
    b.[Author],
    b.[CoverImageUrl],
    bc.[OwnerType],
    CASE
        WHEN bc.[OwnerType] = N'System' THEN l.[Name]
        ELSE u_owner.[FullName]
    END                  AS [SourceName],
    lr.[BorrowerId],
    u_borrower.[FullName] AS [BorrowerFullName],
    u_borrower.[Email]   AS [BorrowerEmail],
    u_borrower.[AvatarUrl] AS [BorrowerAvatarUrl],
    lr.[ApproverId],
    u_approver.[FullName] AS [ApproverFullName],
    lr.[LoanType],
    lr.[Status],
    lr.[RequestedAt],
    lr.[DesiredDays],
    lr.[PickupHubId],
    l_hub.[Name]         AS [PickupHubName],
    lr.[RequestNote]
FROM [dbo].[LoanRequests] lr
INNER JOIN [dbo].[BookCopies]    bc          ON lr.[BookCopyId] = bc.[Id]
INNER JOIN [dbo].[Books]         b           ON bc.[BookId] = b.[Id]
LEFT  JOIN [dbo].[Libraries]      l           ON bc.[LibraryId] = l.[Id]
LEFT  JOIN [dbo].[AspNetUsers]    u_owner     ON bc.[OwnerUserId] = u_owner.[Id]
LEFT  JOIN [dbo].[AspNetUsers]    u_borrower  ON lr.[BorrowerId] = u_borrower.[Id]
LEFT  JOIN [dbo].[AspNetUsers]    u_approver  ON lr.[ApproverId] = u_approver.[Id]
LEFT  JOIN [dbo].[Libraries]      l_hub       ON lr.[PickupHubId] = l_hub.[Id];
GO

/* =========================================================================================
7. vw_BookRatingSummary - View tóm tắt đánh giá đầu sách
   Dùng cho trang Book Detail và chart top rated
========================================================================================= */
CREATE OR ALTER VIEW [dbo].[vw_BookRatingSummary]
AS
SELECT
    r.[BookId],
    b.[Title],
    b.[Author],
    b.[CoverImageUrl],
    COUNT(r.[Id])           AS [ReviewCount],
    AVG(CAST(r.[Rating] AS FLOAT)) AS [AverageRating],
    SUM(CASE WHEN r.[Rating] = 5 THEN 1 ELSE 0 END) AS [FiveStars],
    SUM(CASE WHEN r.[Rating] = 4 THEN 1 ELSE 0 END) AS [FourStars],
    SUM(CASE WHEN r.[Rating] = 3 THEN 1 ELSE 0 END) AS [ThreeStars],
    SUM(CASE WHEN r.[Rating] = 2 THEN 1 ELSE 0 END) AS [TwoStars],
    SUM(CASE WHEN r.[Rating] = 1 THEN 1 ELSE 0 END) AS [OneStar],
    SUM(r.[Likes])          AS [TotalLikes]
FROM [dbo].[Reviews] r
INNER JOIN [dbo].[Books] b ON r.[BookId] = b.[Id]
WHERE r.[IsPublic] = 1
GROUP BY r.[BookId], b.[Title], b.[Author], b.[CoverImageUrl];
GO

PRINT N'== [05_BookShare_Views.sql] Đã tạo 7 Views ==';
PRINT N'   - vw_BooksForSearch';
PRINT N'   - vw_BookCopiesForCatalog';
PRINT N'   - vw_ActiveLoans';
PRINT N'   - vw_UserReadingProgress';
PRINT N'   - vw_AdminDashboardSummary';
PRINT N'   - vw_LoanRequestsWithDetails';
PRINT N'   - vw_BookRatingSummary';
GO
