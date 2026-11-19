/* =========================================================================================
   DỰ ÁN: BOOKSHARE - NỀN TẢNG QUẢN LÝ TỦ SÁCH CÁ NHÂN & MƯỢN TRẢ SÁCH CỘNG ĐỒNG (.NET 10)
   FILE: database/01_BookShare_Schema.sql
   MÔ TẢ: Script T-SQL chuẩn Database-First tạo CSDL BookShareDb với đầy đủ 5 phân khu
          (22 bảng nghiệp vụ cốt lõi + 3 bảng phụ trợ chuẩn ASP.NET Core Identity)
          Sẵn sàng chạy lệnh: dotnet ef dbcontext scaffold
   ========================================================================================= */

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'BookShareDb')
BEGIN
    CREATE DATABASE [BookShareDb];
END
GO

USE [BookShareDb];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* =========================================================================================
   KHỐI 1: TÀI KHOẢN, PHÂN QUYỀN 4 CẤP & XÁC THỰC OTP RESEND (5 BẢNG CỐT LÕI + 3 BẢNG IDENTITY)
   Roles: User, Curator, Librarian, Admin
   ========================================================================================= */

-- 1. Bảng AspNetRoles (Lưu 4 vai trò: Admin, Librarian, Curator, User)
IF OBJECT_ID(N'[dbo].[AspNetRoles]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetRoles] (
        [Id]               NVARCHAR(450) NOT NULL,
        [Name]             NVARCHAR(256) NULL,
        [NormalizedName]   NVARCHAR(256) NULL,
        [ConcurrencyStamp] NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    CREATE UNIQUE NONCLUSTERED INDEX [RoleNameIndex]
        ON [dbo].[AspNetRoles]([NormalizedName] ASC) WHERE ([NormalizedName] IS NOT NULL);
END
GO

-- 2. Bảng AspNetUsers (Mở rộng: FullName, Address, AvatarUrl, TrustScore, LendQuotaBonus, CreatedAt)
IF OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetUsers] (
        [Id]                   NVARCHAR(450) NOT NULL,
        [UserName]             NVARCHAR(256) NULL,
        [NormalizedUserName]   NVARCHAR(256) NULL,
        [Email]                NVARCHAR(256) NULL,
        [NormalizedEmail]      NVARCHAR(256) NULL,
        [EmailConfirmed]       BIT           NOT NULL CONSTRAINT [DF_AspNetUsers_EmailConfirmed] DEFAULT (0),
        [PasswordHash]         NVARCHAR(MAX) NULL,
        [SecurityStamp]        NVARCHAR(MAX) NULL,
        [ConcurrencyStamp]     NVARCHAR(MAX) NULL,
        [PhoneNumber]          NVARCHAR(30)  NULL,
        [PhoneNumberConfirmed] BIT           NOT NULL CONSTRAINT [DF_AspNetUsers_PhoneConfirmed] DEFAULT (0),
        [TwoFactorEnabled]     BIT           NOT NULL CONSTRAINT [DF_AspNetUsers_TwoFactor] DEFAULT (0),
        [LockoutEnd]           DATETIMEOFFSET(7) NULL,
        [LockoutEnabled]       BIT           NOT NULL CONSTRAINT [DF_AspNetUsers_LockoutEnabled] DEFAULT (1),
        [AccessFailedCount]    INT           NOT NULL CONSTRAINT [DF_AspNetUsers_AccessFailedCount] DEFAULT (0),
        -- Các cột mở rộng nghiệp vụ BookShare
        [FullName]             NVARCHAR(150) NOT NULL,
        [Address]              NVARCHAR(300) NULL,
        [AvatarUrl]            NVARCHAR(500) NULL,
        [TrustScore]           INT           NOT NULL CONSTRAINT [DF_AspNetUsers_TrustScore] DEFAULT (100),
        [LendQuotaBonus]       INT           NOT NULL CONSTRAINT [DF_AspNetUsers_LendQuotaBonus] DEFAULT (0),
        [CreatedAt]            DATETIME2(7)  NOT NULL CONSTRAINT [DF_AspNetUsers_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [CK_AspNetUsers_TrustScore] CHECK ([TrustScore] >= 0 AND [TrustScore] <= 200),
        CONSTRAINT [CK_AspNetUsers_LendQuotaBonus] CHECK ([LendQuotaBonus] >= 0)
    );
    CREATE NONCLUSTERED INDEX [EmailIndex] ON [dbo].[AspNetUsers]([NormalizedEmail] ASC);
    CREATE UNIQUE NONCLUSTERED INDEX [UserNameIndex]
        ON [dbo].[AspNetUsers]([NormalizedUserName] ASC) WHERE ([NormalizedUserName] IS NOT NULL);
END
GO

-- 3. Bảng AspNetUserRoles (Ánh xạ N-N giữa User và 4 Roles)
IF OBJECT_ID(N'[dbo].[AspNetUserRoles]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetUserRoles] (
        [UserId] NVARCHAR(450) NOT NULL,
        [RoleId] NVARCHAR(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY CLUSTERED ([UserId] ASC, [RoleId] ASC),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId])
            REFERENCES [dbo].[AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX [IX_AspNetUserRoles_RoleId] ON [dbo].[AspNetUserRoles]([RoleId] ASC);
END
GO

-- 4. Bảng AspNetUserLogins (Lưu thông tin liên kết Google OAuth 2.0)
IF OBJECT_ID(N'[dbo].[AspNetUserLogins]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetUserLogins] (
        [LoginProvider]       NVARCHAR(128) NOT NULL,
        [ProviderKey]         NVARCHAR(128) NOT NULL,
        [ProviderDisplayName] NVARCHAR(MAX) NULL,
        [UserId]              NVARCHAR(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY CLUSTERED ([LoginProvider] ASC, [ProviderKey] ASC),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX [IX_AspNetUserLogins_UserId] ON [dbo].[AspNetUserLogins]([UserId] ASC);
END
GO

-- Bảng phụ trợ chuẩn ASP.NET Core Identity (Claims & Tokens)
IF OBJECT_ID(N'[dbo].[AspNetRoleClaims]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetRoleClaims] (
        [Id]         INT IDENTITY(1,1) NOT NULL,
        [RoleId]     NVARCHAR(450)     NOT NULL,
        [ClaimType]  NVARCHAR(MAX)     NULL,
        [ClaimValue] NVARCHAR(MAX)     NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId])
            REFERENCES [dbo].[AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'[dbo].[AspNetUserClaims]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetUserClaims] (
        [Id]         INT IDENTITY(1,1) NOT NULL,
        [UserId]     NVARCHAR(450)     NOT NULL,
        [ClaimType]  NVARCHAR(MAX)     NULL,
        [ClaimValue] NVARCHAR(MAX)     NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'[dbo].[AspNetUserTokens]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetUserTokens] (
        [UserId]        NVARCHAR(450) NOT NULL,
        [LoginProvider] NVARCHAR(128) NOT NULL,
        [Name]          NVARCHAR(128) NOT NULL,
        [Value]         NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY CLUSTERED ([UserId] ASC, [LoginProvider] ASC, [Name] ASC),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END
GO

-- 5. Bảng OtpVerifications (Xác thực OTP 6 số qua Resend Email API - Dịch vụ S01 & S09)
IF OBJECT_ID(N'[dbo].[OtpVerifications]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[OtpVerifications] (
        [Id]              INT IDENTITY(1,1) NOT NULL,
        [UserId]          NVARCHAR(450)     NULL,
        [Email]           NVARCHAR(256)     NOT NULL,
        [OtpCodeHash]     NVARCHAR(128)     NOT NULL, -- Băm SHA-256 của mã OTP 6 chữ số
        [Purpose]         NVARCHAR(30)      NOT NULL, -- Register / ResetPassword / VerifyLoan
        [ExpiresAt]       DATETIME2(7)      NOT NULL,
        [IsUsed]          BIT               NOT NULL CONSTRAINT [DF_OtpVerifications_IsUsed] DEFAULT (0),
        [ResendMessageId] NVARCHAR(100)     NULL,     -- Message ID trả về từ Resend API
        [CreatedAt]       DATETIME2(7)      NOT NULL CONSTRAINT [DF_OtpVerifications_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_OtpVerifications] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_OtpVerifications_AspNetUsers] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [CK_OtpVerifications_Purpose] CHECK ([Purpose] IN (N'Register', N'ResetPassword', N'VerifyLoan'))
    );
    CREATE NONCLUSTERED INDEX [IX_OtpVerifications_Email_Purpose]
        ON [dbo].[OtpVerifications]([Email] ASC, [Purpose] ASC, [IsUsed] ASC);
END
GO

/* =========================================================================================
   KHỐI 2: SÁCH, KHO CHUNG THƯ VIỆN & TỦ SÁCH CÁ NHÂN (6 BẢNG)
   TUYỆT ĐỐI KHÔNG CÓ CỘT GIÁ BÁN (PRICE) HAY GIỎ HÀNG MUA BÁN
   ========================================================================================= */

-- 6. Bảng Categories (Danh mục thể loại sách)
IF OBJECT_ID(N'[dbo].[Categories]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Categories] (
        [Id]        INT IDENTITY(1,1) NOT NULL,
        [Name]      NVARCHAR(100)     NOT NULL,
        [Slug]      NVARCHAR(120)     NOT NULL,
        [IconClass] NVARCHAR(80)      NOT NULL CONSTRAINT [DF_Categories_IconClass] DEFAULT (N'bi-book'),
        CONSTRAINT [PK_Categories] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [UQ_Categories_Slug] UNIQUE ([Slug])
    );
END
GO

-- 7. Bảng Books (Thông tin Đầu sách chuẩn - Dịch vụ S05 ISBN Auto Catalog & S06 Search)
IF OBJECT_ID(N'[dbo].[Books]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Books] (
        [Id]            INT IDENTITY(1,1) NOT NULL,
        [Title]         NVARCHAR(250)     NOT NULL,
        [Author]        NVARCHAR(200)     NOT NULL,
        [ISBN]          NVARCHAR(20)      NOT NULL,
        [Publisher]     NVARCHAR(150)     NULL,
        [PublishedYear] INT               NULL,
        [PageCount]     INT               NOT NULL CONSTRAINT [DF_Books_PageCount] DEFAULT (200),
        [CategoryId]    INT               NOT NULL,
        [CoverImageUrl] NVARCHAR(500)     NULL,
        [Summary]       NVARCHAR(MAX)     NULL,
        [CreatedAt]     DATETIME2(7)      NOT NULL CONSTRAINT [DF_Books_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_Books] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [UQ_Books_ISBN] UNIQUE ([ISBN]),
        CONSTRAINT [FK_Books_Categories] FOREIGN KEY ([CategoryId])
            REFERENCES [dbo].[Categories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [CK_Books_PageCount] CHECK ([PageCount] > 0),
        CONSTRAINT [CK_Books_PublishedYear] CHECK ([PublishedYear] IS NULL OR ([PublishedYear] >= 1800 AND [PublishedYear] <= 2100))
    );
    CREATE NONCLUSTERED INDEX [IX_Books_Title_Author] ON [dbo].[Books]([Title] ASC, [Author] ASC);
    CREATE NONCLUSTERED INDEX [IX_Books_CategoryId] ON [dbo].[Books]([CategoryId] ASC);
END
GO

-- 8. Bảng Libraries (Kho sách chung Thư viện & Điểm hẹn giao nhận CLB/Hub - S07 & S20)
IF OBJECT_ID(N'[dbo].[Libraries]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Libraries] (
        [Id]            INT IDENTITY(1,1) NOT NULL,
        [Name]          NVARCHAR(200)     NOT NULL,
        [Address]       NVARCHAR(350)     NOT NULL,
        [ManagerUserId] NVARCHAR(450)     Not NULL, -- Thủ thư hoặc Curator quản lý
        [Phone]         NVARCHAR(30)      NULL,
        [HubType]       NVARCHAR(30)      NOT NULL CONSTRAINT [DF_Libraries_HubType] DEFAULT (N'Library'), -- Library / DropoffHub / ClubStation
        [OpenHours]     NVARCHAR(120)     NULL CONSTRAINT [DF_Libraries_OpenHours] DEFAULT (N'08:00 - 17:30 (T2 - T7)'),
        [IsActive]      BIT               NOT NULL CONSTRAINT [DF_Libraries_IsActive] DEFAULT (1),
        CONSTRAINT [PK_Libraries] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Libraries_AspNetUsers_Manager] FOREIGN KEY ([ManagerUserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [CK_Libraries_HubType] CHECK ([HubType] IN (N'Library', N'DropoffHub', N'ClubStation'))
    );
END
GO

-- 9. Bảng BookCopies (Bản sao sách giấy vật lý - Tách biệt Kho chung System và Cá nhân User)
IF OBJECT_ID(N'[dbo].[BookCopies]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BookCopies] (
        [Id]          INT IDENTITY(1,1) NOT NULL,
        [BookId]      INT               NOT NULL,
        [Barcode]     NVARCHAR(50)      NOT NULL,
        [OwnerType]   NVARCHAR(20)      NOT NULL, -- System (Kho chung) / User (Tủ sách P2P)
        [LibraryId]   INT               NULL,     -- Khác NULL nếu thuộc Kho chung hoặc gửi tại Trạm CLB
        [OwnerUserId] NVARCHAR(450)     NULL,     -- Khác NULL nếu thuộc Chủ sách cá nhân (P2P)
        [ShelfLocation] NVARCHAR(80)    NULL,     -- Vị trí kệ trong thư viện (VD: Kệ A2 - Tầng 2)
        [Condition]   NVARCHAR(20)      NOT NULL CONSTRAINT [DF_BookCopies_Condition] DEFAULT (N'Good'),
        [Status]      NVARCHAR(20)      NOT NULL CONSTRAINT [DF_BookCopies_Status] DEFAULT (N'Available'),
        [IsLendable]  BIT               NOT NULL CONSTRAINT [DF_BookCopies_IsLendable] DEFAULT (1),
        [CreatedAt]   DATETIME2(7)      NOT NULL CONSTRAINT [DF_BookCopies_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_BookCopies] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [UQ_BookCopies_Barcode] UNIQUE ([Barcode]),
        CONSTRAINT [FK_BookCopies_Books] FOREIGN KEY ([BookId])
            REFERENCES [dbo].[Books] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_BookCopies_Libraries] FOREIGN KEY ([LibraryId])
            REFERENCES [dbo].[Libraries] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_BookCopies_AspNetUsers_Owner] FOREIGN KEY ([OwnerUserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [CK_BookCopies_OwnerType] CHECK ([OwnerType] IN (N'System', N'User')),
        CONSTRAINT [CK_BookCopies_Condition] CHECK ([Condition] IN (N'New', N'Good', N'Fair', N'Worn')),
        CONSTRAINT [CK_BookCopies_Status] CHECK ([Status] IN (N'Available', N'Reserved', N'Borrowed', N'InTransit', N'Lost', N'Damaged'))
    );
    CREATE NONCLUSTERED INDEX [IX_BookCopies_BookId_Status] ON [dbo].[BookCopies]([BookId] ASC, [Status] ASC, [IsLendable] ASC);
    CREATE NONCLUSTERED INDEX [IX_BookCopies_OwnerUserId] ON [dbo].[BookCopies]([OwnerUserId] ASC);
    CREATE NONCLUSTERED INDEX [IX_BookCopies_LibraryId] ON [dbo].[BookCopies]([LibraryId] ASC);
END
GO

-- 10. Bảng Shelves (Kệ sách tùy chỉnh trong Tủ sách cá nhân - Dịch vụ S02)
IF OBJECT_ID(N'[dbo].[Shelves]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Shelves] (
        [Id]           INT IDENTITY(1,1) NOT NULL,
        [UserId]       NVARCHAR(450)     NOT NULL,
        [Name]         NVARCHAR(100)     NOT NULL, -- VD: Đang đọc, Đã đọc, Yêu thích, Sách sẵn sàng cho mượn
        [Description]  NVARCHAR(300)     NULL,
        [IsPublic]     BIT               NOT NULL CONSTRAINT [DF_Shelves_IsPublic] DEFAULT (1),
        [DisplayOrder] INT               NOT NULL CONSTRAINT [DF_Shelves_DisplayOrder] DEFAULT (1),
        CONSTRAINT [PK_Shelves] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Shelves_AspNetUsers] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX [IX_Shelves_UserId] ON [dbo].[Shelves]([UserId] ASC, [DisplayOrder] ASC);
END
GO

-- 11. Bảng ShelfItems (Các bản sao sách nằm trong từng Kệ sách cá nhân - Dịch vụ S02)
IF OBJECT_ID(N'[dbo].[ShelfItems]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ShelfItems] (
        [Id]         INT IDENTITY(1,1) NOT NULL,
        [ShelfId]    INT               NOT NULL,
        [BookCopyId] INT               NOT NULL,
        [AddedAt]    DATETIME2(7)      NOT NULL CONSTRAINT [DF_ShelfItems_AddedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ShelfItems] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [UQ_ShelfItems_Shelf_BookCopy] UNIQUE ([ShelfId], [BookCopyId]),
        CONSTRAINT [FK_ShelfItems_Shelves] FOREIGN KEY ([ShelfId])
            REFERENCES [dbo].[Shelves] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ShelfItems_BookCopies] FOREIGN KEY ([BookCopyId])
            REFERENCES [dbo].[BookCopies] ([Id]) ON DELETE NO ACTION
    );
END
GO

/* =========================================================================================
   KHỐI 3: THEO DÕI TIẾN ĐỘ ĐỌC SÁCH CÁ NHÂN & GHI CHÚ - KẾ THỪA VER07 (2 BẢNG)
   Dịch vụ S03 (ReadingTracker) & S04 (BookNotesQuotes)
   ========================================================================================= */

-- 12. Bảng ReadingProgresses (Theo dõi số trang đang đọc, mức ưu tiên, mục tiêu đọc - Kế thừa Ver07)
IF OBJECT_ID(N'[dbo].[ReadingProgresses]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ReadingProgresses] (
        [Id]             INT IDENTITY(1,1) NOT NULL,
        [UserId]         NVARCHAR(450)     NOT NULL,
        [BookId]         INT               NOT NULL,
        [CurrentPage]    INT               NOT NULL CONSTRAINT [DF_ReadingProgresses_CurrentPage] DEFAULT (0),
        [TotalPages]     INT               NOT NULL CONSTRAINT [DF_ReadingProgresses_TotalPages] DEFAULT (200),
        [Priority]       NVARCHAR(20)      NOT NULL CONSTRAINT [DF_ReadingProgresses_Priority] DEFAULT (N'Medium'), -- Low / Medium / High (Kế thừa Ver07)
        [TargetDeadline] DATE              NULL, -- Ngày dự kiến đọc xong (Kế thừa Ver07)
        [Status]         NVARCHAR(20)      NOT NULL CONSTRAINT [DF_ReadingProgresses_Status] DEFAULT (N'WantToRead'),
        [StartedAt]      DATETIME2(7)      NULL,
        [FinishedAt]     DATETIME2(7)      NULL,
        [PersonalRating] INT               NULL,
        [UpdatedAt]      DATETIME2(7)      NOT NULL CONSTRAINT [DF_ReadingProgresses_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ReadingProgresses] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [UQ_ReadingProgresses_User_Book] UNIQUE ([UserId], [BookId]),
        CONSTRAINT [FK_ReadingProgresses_AspNetUsers] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ReadingProgresses_Books] FOREIGN KEY ([BookId])
            REFERENCES [dbo].[Books] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [CK_ReadingProgresses_Pages] CHECK ([CurrentPage] >= 0 AND [TotalPages] > 0 AND [CurrentPage] <= [TotalPages]),
        CONSTRAINT [CK_ReadingProgresses_Status] CHECK ([Status] IN (N'WantToRead', N'Reading', N'Completed')),
        CONSTRAINT [CK_ReadingProgresses_Priority] CHECK ([Priority] IN (N'Low', N'Medium', N'High')),
        CONSTRAINT [CK_ReadingProgresses_Rating] CHECK ([PersonalRating] IS NULL OR ([PersonalRating] >= 1 AND [PersonalRating] <= 5))
    );
    CREATE NONCLUSTERED INDEX [IX_ReadingProgresses_UserId_Status] ON [dbo].[ReadingProgresses]([UserId] ASC, [Status] ASC);
END
GO

-- 13. Bảng BookNotes (Ghi chú cá nhân & trích dẫn tâm đắc theo từng trang sách - Dịch vụ S04)
IF OBJECT_ID(N'[dbo].[BookNotes]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BookNotes] (
        [Id]                INT IDENTITY(1,1) NOT NULL,
        [ReadingProgressId] INT               NOT NULL,
        [PageNumber]        INT               NOT NULL CONSTRAINT [DF_BookNotes_PageNumber] DEFAULT (1),
        [QuoteText]         NVARCHAR(1000)    NULL,
        [NoteContent]       NVARCHAR(2000)    NOT NULL,
        [IsPublic]          BIT               NOT NULL CONSTRAINT [DF_BookNotes_IsPublic] DEFAULT (0),
        [CreatedAt]         DATETIME2(7)      NOT NULL CONSTRAINT [DF_BookNotes_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_BookNotes] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_BookNotes_ReadingProgresses] FOREIGN KEY ([ReadingProgressId])
            REFERENCES [dbo].[ReadingProgresses] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [CK_BookNotes_PageNumber] CHECK ([PageNumber] >= 1)
    );
    CREATE NONCLUSTERED INDEX [IX_BookNotes_ReadingProgressId] ON [dbo].[BookNotes]([ReadingProgressId] ASC, [PageNumber] ASC);
END
GO

/* =========================================================================================
   KHỐI 4: MƯỢN – TRẢ – BIÊN BẢN ẢNH – ĐẶT TRƯỚC – TRANH CHẤP (5 BẢNG)
   Dịch vụ S08, S09, S10, S11, S12, S19
   ========================================================================================= */

-- 14. Bảng LoanRequests (Yêu cầu đặt mượn sách Kho chung hoặc P2P - Dịch vụ S08 & S09)
IF OBJECT_ID(N'[dbo].[LoanRequests]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LoanRequests] (
        [Id]          INT IDENTITY(1,1) NOT NULL,
        [BookCopyId]  INT               NOT NULL,
        [BorrowerId]  NVARCHAR(450)     NOT NULL,
        [ApproverId]  NVARCHAR(450)     NULL,     -- Thủ thư hoặc Chủ sách P2P duyệt
        [LoanType]    NVARCHAR(20)      NOT NULL, -- Library / P2P
        [Status]      NVARCHAR(20)      NOT NULL CONSTRAINT [DF_LoanRequests_Status] DEFAULT (N'Pending'),
        [RequestedAt] DATETIME2(7)      NOT NULL CONSTRAINT [DF_LoanRequests_RequestedAt] DEFAULT (SYSUTCDATETIME()),
        [DesiredDays] INT               NOT NULL CONSTRAINT [DF_LoanRequests_DesiredDays] DEFAULT (14),
        [PickupHubId] INT               NULL,     -- Điểm hẹn giao nhận (Thư viện hoặc CLB Hub)
        [RequestNote] NVARCHAR(500)     NULL,
        CONSTRAINT [PK_LoanRequests] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_LoanRequests_BookCopies] FOREIGN KEY ([BookCopyId])
            REFERENCES [dbo].[BookCopies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LoanRequests_AspNetUsers_Borrower] FOREIGN KEY ([BorrowerId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LoanRequests_AspNetUsers_Approver] FOREIGN KEY ([ApproverId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LoanRequests_Libraries_PickupHub] FOREIGN KEY ([PickupHubId])
            REFERENCES [dbo].[Libraries] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [CK_LoanRequests_LoanType] CHECK ([LoanType] IN (N'Library', N'P2P')),
        CONSTRAINT [CK_LoanRequests_Status] CHECK ([Status] IN (N'Pending', N'Approved', N'Rejected', N'Cancelled', N'Expired')),
        CONSTRAINT [CK_LoanRequests_DesiredDays] CHECK ([DesiredDays] >= 1 AND [DesiredDays] <= 60)
    );
    CREATE NONCLUSTERED INDEX [IX_LoanRequests_BorrowerId_Status] ON [dbo].[LoanRequests]([BorrowerId] ASC, [Status] ASC);
    CREATE NONCLUSTERED INDEX [IX_LoanRequests_BookCopyId_Status] ON [dbo].[LoanRequests]([BookCopyId] ASC, [Status] ASC);
END
GO

-- 15. Bảng Loans (Hợp đồng mượn sách đang hiệu lực, gia hạn & phí phạt trễ hạn - Dịch vụ S08, S09, S12)
IF OBJECT_ID(N'[dbo].[Loans]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Loans] (
        [Id]            INT IDENTITY(1,1) NOT NULL,
        [LoanRequestId] INT               NOT NULL,
        [StartDate]     DATETIME2(7)      NOT NULL CONSTRAINT [DF_Loans_StartDate] DEFAULT (SYSUTCDATETIME()),
        [DueDate]       DATETIME2(7)      NOT NULL,
        [ReturnDate]    DATETIME2(7)      NULL,
        [RenewCount]    INT               NOT NULL CONSTRAINT [DF_Loans_RenewCount] DEFAULT (0),
        [FineAmount]    DECIMAL(18,2)     NOT NULL CONSTRAINT [DF_Loans_FineAmount] DEFAULT (0),
        [Status]        NVARCHAR(20)      NOT NULL CONSTRAINT [DF_Loans_Status] DEFAULT (N'Active'),
        CONSTRAINT [PK_Loans] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [UQ_Loans_LoanRequestId] UNIQUE ([LoanRequestId]),
        CONSTRAINT [FK_Loans_LoanRequests] FOREIGN KEY ([LoanRequestId])
            REFERENCES [dbo].[LoanRequests] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [CK_Loans_RenewCount] CHECK ([RenewCount] >= 0 AND [RenewCount] <= 3),
        CONSTRAINT [CK_Loans_FineAmount] CHECK ([FineAmount] >= 0),
        CONSTRAINT [CK_Loans_Status] CHECK ([Status] IN (N'Active', N'Returned', N'Overdue', N'Disputed', N'Lost'))
    );
    CREATE NONCLUSTERED INDEX [IX_Loans_Status_DueDate] ON [dbo].[Loans]([Status] ASC, [DueDate] ASC);
END
GO

-- 16. Bảng ConditionReports (Biên bản điện tử chụp ảnh tình trạng sách lúc giao & trả qua Cloudinary - Dịch vụ S10)
IF OBJECT_ID(N'[dbo].[ConditionReports]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ConditionReports] (
        [Id]               INT IDENTITY(1,1) NOT NULL,
        [LoanId]           INT               NOT NULL,
        [ReportType]       NVARCHAR(20)      NOT NULL, -- Handover (Lúc giao) / Return (Lúc trả)
        [ReportedByUserId] NVARCHAR(450)     NOT NULL,
        [ConditionGrade]   NVARCHAR(20)      NOT NULL, -- New / Good / Fair / Worn / Damaged
        [Note]             NVARCHAR(1000)    NULL,
        [ImageUrl]         NVARCHAR(500)     NOT NULL, -- Link ảnh Cloudinary
        [CreatedAt]        DATETIME2(7)      NOT NULL CONSTRAINT [DF_ConditionReports_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ConditionReports] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_ConditionReports_Loans] FOREIGN KEY ([LoanId])
            REFERENCES [dbo].[Loans] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ConditionReports_AspNetUsers] FOREIGN KEY ([ReportedByUserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [CK_ConditionReports_ReportType] CHECK ([ReportType] IN (N'Handover', N'Return')),
        CONSTRAINT [CK_ConditionReports_Grade] CHECK ([ConditionGrade] IN (N'New', N'Good', N'Fair', N'Worn', N'Damaged'))
    );
    CREATE NONCLUSTERED INDEX [IX_ConditionReports_LoanId] ON [dbo].[ConditionReports]([LoanId] ASC, [ReportType] ASC);
END
GO

-- 17. Bảng Reservations (Đặt trước và xếp hàng chờ tự động cho sách Hot - Dịch vụ S11)
IF OBJECT_ID(N'[dbo].[Reservations]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Reservations] (
        [Id]            INT IDENTITY(1,1) NOT NULL,
        [BookId]        INT               NOT NULL,
        [UserId]        NVARCHAR(450)     NOT NULL,
        [QueuePosition] INT               NOT NULL,
        [Status]        NVARCHAR(20)      NOT NULL CONSTRAINT [DF_Reservations_Status] DEFAULT (N'Waiting'),
        [CreatedAt]     DATETIME2(7)      NOT NULL CONSTRAINT [DF_Reservations_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [NotifiedAt]    DATETIME2(7)      NULL,
        [ExpiresAt]     DATETIME2(7)      NULL,
        CONSTRAINT [PK_Reservations] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Reservations_Books] FOREIGN KEY ([BookId])
            REFERENCES [dbo].[Books] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Reservations_AspNetUsers] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [CK_Reservations_QueuePosition] CHECK ([QueuePosition] >= 1),
        CONSTRAINT [CK_Reservations_Status] CHECK ([Status] IN (N'Waiting', N'Notified', N'Fulfilled', N'Cancelled', N'Expired'))
    );
    CREATE NONCLUSTERED INDEX [IX_Reservations_BookId_Queue] ON [dbo].[Reservations]([BookId] ASC, [Status] ASC, [QueuePosition] ASC);
END
GO

-- 18. Bảng Disputes (Khiếu nại và phân xử tranh chấp sách hỏng/mất - Dịch vụ S19)
IF OBJECT_ID(N'[dbo].[Disputes]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Disputes] (
        [Id]                INT IDENTITY(1,1) NOT NULL,
        [LoanId]            INT               NOT NULL,
        [RaisedByUserId]    NVARCHAR(450)     NOT NULL,
        [Reason]            NVARCHAR(1000)    NOT NULL,
        [EvidenceUrlsJson]  NVARCHAR(MAX)     NULL, -- Mảng JSON chứa URL ảnh bằng chứng đối chiếu
        [Status]            NVARCHAR(20)      NOT NULL CONSTRAINT [DF_Disputes_Status] DEFAULT (N'Open'),
        [ResolvedByAdminId] NVARCHAR(450)     NULL,
        [ResolutionNote]    NVARCHAR(1500)    NULL,
        [CreatedAt]         DATETIME2(7)      NOT NULL CONSTRAINT [DF_Disputes_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [ResolvedAt]        DATETIME2(7)      NULL,
        CONSTRAINT [PK_Disputes] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Disputes_Loans] FOREIGN KEY ([LoanId])
            REFERENCES [dbo].[Loans] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Disputes_AspNetUsers_RaisedBy] FOREIGN KEY ([RaisedByUserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Disputes_AspNetUsers_ResolvedBy] FOREIGN KEY ([ResolvedByAdminId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [CK_Disputes_Status] CHECK ([Status] IN (N'Open', N'UnderReview', N'Resolved', N'Rejected'))
    );
    CREATE NONCLUSTERED INDEX [IX_Disputes_Status] ON [dbo].[Disputes]([Status] ASC, [CreatedAt] DESC);
END
GO

/* =========================================================================================
   KHỐI 5: GÓI THÀNH VIÊN, GIAO DỊCH SANDBOX, ĐÁNH GIÁ, THÔNG BÁO & TRỢ LÝ AI GEMINI (6 BẢNG)
   Dịch vụ S13, S14, S15, S16, S17, S18
   ========================================================================================= */

-- 19. Bảng Memberships (Quản lý gói thành viên Free / Premium - Dịch vụ S16)
IF OBJECT_ID(N'[dbo].[Memberships]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Memberships] (
        [Id]                 INT IDENTITY(1,1) NOT NULL,
        [UserId]             NVARCHAR(450)     NOT NULL,
        [Tier]               NVARCHAR(20)      NOT NULL CONSTRAINT [DF_Memberships_Tier] DEFAULT (N'Free'), -- Free / Premium
        [MaxConcurrentLoans] INT               NOT NULL CONSTRAINT [DF_Memberships_MaxLoans] DEFAULT (2),
        [StartDate]          DATETIME2(7)      NOT NULL CONSTRAINT [DF_Memberships_StartDate] DEFAULT (SYSUTCDATETIME()),
        [ExpiryDate]         DATETIME2(7)      NULL,
        [IsActive]           BIT               NOT NULL CONSTRAINT [DF_Memberships_IsActive] DEFAULT (1),
        CONSTRAINT [PK_Memberships] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Memberships_AspNetUsers] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [CK_Memberships_Tier] CHECK ([Tier] IN (N'Free', N'Premium')),
        CONSTRAINT [CK_Memberships_MaxLoans] CHECK ([MaxConcurrentLoans] >= 1 AND [MaxConcurrentLoans] <= 20)
    );
    CREATE NONCLUSTERED INDEX [IX_Memberships_UserId_Active] ON [dbo].[Memberships]([UserId] ASC, [IsActive] ASC);
END
GO

-- 20. Bảng Transactions (Cổng thanh toán Sandbox cho gói Premium, phí phạt trễ hạn & cọc mô phỏng - Dịch vụ S17)
-- Lưu ý: TUYỆT ĐỐI KHÔNG có loại giao dịch mua bán sách!
IF OBJECT_ID(N'[dbo].[Transactions]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Transactions] (
        [Id]              INT IDENTITY(1,1) NOT NULL,
        [UserId]          NVARCHAR(450)     NOT NULL,
        [LoanId]          INT               NULL,
        [TransactionType] NVARCHAR(30)      NOT NULL, -- Membership / Fine / SimulatedDeposit
        [Amount]          DECIMAL(18,2)     NOT NULL,
        [PaymentGateway]  NVARCHAR(30)      NOT NULL CONSTRAINT [DF_Transactions_Gateway] DEFAULT (N'VNPaySandbox'),
        [PaymentStatus]   NVARCHAR(20)      NOT NULL CONSTRAINT [DF_Transactions_Status] DEFAULT (N'Pending'),
        [HmacSignature]   NVARCHAR(256)     NULL,
        [CreatedAt]       DATETIME2(7)      NOT NULL CONSTRAINT [DF_Transactions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_Transactions] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Transactions_AspNetUsers] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Transactions_Loans] FOREIGN KEY ([LoanId])
            REFERENCES [dbo].[Loans] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [CK_Transactions_Type] CHECK ([TransactionType] IN (N'Membership', N'Fine', N'SimulatedDeposit')),
        CONSTRAINT [CK_Transactions_Gateway] CHECK ([PaymentGateway] IN (N'VNPaySandbox', N'MoMoSandbox')),
        CONSTRAINT [CK_Transactions_Status] CHECK ([PaymentStatus] IN (N'Pending', N'Completed', N'Failed', N'Refunded')),
        CONSTRAINT [CK_Transactions_Amount] CHECK ([Amount] >= 0)
    );
    CREATE NONCLUSTERED INDEX [IX_Transactions_UserId_CreatedAt] ON [dbo].[Transactions]([UserId] ASC, [CreatedAt] DESC);
END
GO

-- 21. Bảng Reviews (Đánh giá chất lượng sách và chấm điểm uy tín TrustScore 2 chiều - Dịch vụ S15)
IF OBJECT_ID(N'[dbo].[Reviews]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Reviews] (
        [Id]         INT IDENTITY(1,1) NOT NULL,
        [ReviewerId] NVARCHAR(450)     NOT NULL,
        [TargetType] NVARCHAR(20)      NOT NULL, -- Book / User
        [TargetId]   NVARCHAR(450)     NOT NULL, -- BookId (dạng chuỗi) hoặc UserId được đánh giá
        [LoanId]     INT               NULL,
        [Rating]     INT               NOT NULL,
        [Comment]    NVARCHAR(1000)    NULL,
        [CreatedAt]  DATETIME2(7)      NOT NULL CONSTRAINT [DF_Reviews_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_Reviews] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Reviews_AspNetUsers_Reviewer] FOREIGN KEY ([ReviewerId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Reviews_Loans] FOREIGN KEY ([LoanId])
            REFERENCES [dbo].[Loans] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [CK_Reviews_TargetType] CHECK ([TargetType] IN (N'Book', N'User')),
        CONSTRAINT [CK_Reviews_Rating] CHECK ([Rating] >= 1 AND [Rating] <= 5)
    );
    CREATE NONCLUSTERED INDEX [IX_Reviews_Target] ON [dbo].[Reviews]([TargetType] ASC, [TargetId] ASC);
END
GO

-- 22. Bảng Notifications (Trung tâm thông báo Web kết hợp lịch sử gửi email Resend - Dịch vụ S18)
IF OBJECT_ID(N'[dbo].[Notifications]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Notifications] (
        [Id]                 INT IDENTITY(1,1) NOT NULL,
        [UserId]             NVARCHAR(450)     NOT NULL,
        [Title]              NVARCHAR(200)     NOT NULL,
        [Content]            NVARCHAR(1000)    NOT NULL,
        [Type]               NVARCHAR(30)      NOT NULL CONSTRAINT [DF_Notifications_Type] DEFAULT (N'System'), -- DueReminder / LoanApproved / QueueAvailable / Dispute / System
        [IsRead]             BIT               NOT NULL CONSTRAINT [DF_Notifications_IsRead] DEFAULT (0),
        [EmailSentViaResend] BIT               NOT NULL CONSTRAINT [DF_Notifications_EmailSent] DEFAULT (0),
        [CreatedAt]          DATETIME2(7)      NOT NULL CONSTRAINT [DF_Notifications_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_Notifications] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Notifications_AspNetUsers] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX [IX_Notifications_UserId_IsRead] ON [dbo].[Notifications]([UserId] ASC, [IsRead] ASC, [CreatedAt] DESC);
END
GO

-- 23. Bảng AiChatSessions (Phiên hội thoại với Trợ lý AI Gemini - Dịch vụ S13)
IF OBJECT_ID(N'[dbo].[AiChatSessions]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AiChatSessions] (
        [Id]        INT IDENTITY(1,1) NOT NULL,
        [UserId]    NVARCHAR(450)     NOT NULL,
        [Title]     NVARCHAR(200)     NOT NULL CONSTRAINT [DF_AiChatSessions_Title] DEFAULT (N'Tư vấn chọn sách & mượn trả'),
        [ModelName] NVARCHAR(50)      NOT NULL CONSTRAINT [DF_AiChatSessions_Model] DEFAULT (N'gemini-2.5-flash'),
        [CreatedAt] DATETIME2(7)      NOT NULL CONSTRAINT [DF_AiChatSessions_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] DATETIME2(7)      NOT NULL CONSTRAINT [DF_AiChatSessions_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_AiChatSessions] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_AiChatSessions_AspNetUsers] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX [IX_AiChatSessions_UserId] ON [dbo].[AiChatSessions]([UserId] ASC, [UpdatedAt] DESC);
END
GO

-- 24. Bảng AiChatMessages (Chi tiết tin nhắn hội thoại Gemini AI kèm RAG BookIds - Dịch vụ S13)
IF OBJECT_ID(N'[dbo].[AiChatMessages]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AiChatMessages] (
        [Id]                    INT IDENTITY(1,1) NOT NULL,
        [SessionId]             INT               NOT NULL,
        [UserId]                NVARCHAR(450)     NOT NULL,
        [Sender]                NVARCHAR(20)      NOT NULL, -- User / Gemini
        [MessageText]           NVARCHAR(MAX)     NOT NULL,
        [PromptTokens]          INT                NOT NULL CONSTRAINT [DF_AiChatMessages_Tokens] DEFAULT (0),
        [ReferencedBookIdsJson] NVARCHAR(500)     NULL,     -- Mảng JSON các BookId được RAG trích xuất
        [CreatedAt]             DATETIME2(7)      NOT NULL CONSTRAINT [DF_AiChatMessages_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_AiChatMessages] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_AiChatMessages_AiChatSessions] FOREIGN KEY ([SessionId])
            REFERENCES [dbo].[AiChatSessions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AiChatMessages_AspNetUsers] FOREIGN KEY ([UserId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [CK_AiChatMessages_Sender] CHECK ([Sender] IN (N'User', N'Gemini'))
    );
    CREATE NONCLUSTERED INDEX [IX_AiChatMessages_SessionId] ON [dbo].[AiChatMessages]([SessionId] ASC, [CreatedAt] ASC);
END
GO

PRINT N'=== ĐÃ KHỞI TẠO THÀNH CÔNG SCHEMA CSDL BOOKSHAREDB (CHUẨN SCAFFOLD .NET 10) ===';
GO
