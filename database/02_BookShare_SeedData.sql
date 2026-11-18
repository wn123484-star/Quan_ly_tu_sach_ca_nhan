/* =========================================================================================
DỰ ÁN: BOOKSHARE - NỀN TẢNG QUẢN LÝ TỦ SÁCH CÁ NHÂN & MƯỢN TRẢ SÁCH CỘNG ĐỒNG (.NET 10)
FILE: database/02_BookShare_SeedData.sql
MÔ TẢ: Dữ liệu mẫu cho CSDL BookShareDb (Sprint 2 - Chuẩn hóa đa nền tảng)
       - 4 tài khoản theo 4 vai trò (Admin, Librarian, Curator, User)
       - 8 danh mục thể loại sách (Categories)
       - 3 thư viện / Hub (ĐH CMC, Dropoff Hub, Club Station)
       - 30 đầu sách (Books) đầy đủ ISBN, tác giả, năm XB
       - 50 bản sao sách (BookCopies) chia System (Library) và User (P2P)
       - Kệ sách cá nhân (Shelves) + Reading progress + Notes
       - Lịch sử mượn/trả (LoanRequests, Loans, ConditionReports)
       - Đặt trước (Reservations), Đánh giá (Reviews), Giao dịch (Transactions)
       - Memberships, Notifications, AiChatLogs

       Tương thích SQL Server 2022 (utf-8) và Linux Docker (sử dụng N prefix)
       Tất cả password đã được hash BCrypt (chuẩn ASP.NET Core Identity)

TÁC GIẢ: Lê Hoàn Lâm - MSSV: BIT250210 - Lớp 25IT - Nhóm 5
NHÁNH:   sprint2/lam-database-query
========================================================================================= */
USE [BookShareDb];
GO
SET NOCOUNT ON;
GO
SET XACT_ABORT ON;
GO

/* Xóa dữ liệu cũ theo thứ tự FK ngược (chỉ dùng khi re-seed) */
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'[dbo].[Transactions]', N'U') IS NOT NULL DELETE FROM [dbo].[Transactions];
    IF OBJECT_ID(N'[dbo].[Reviews]', N'U') IS NOT NULL DELETE FROM [dbo].[Reviews];
    IF OBJECT_ID(N'[dbo].[AiChatLogs]', N'U') IS NOT NULL DELETE FROM [dbo].[AiChatLogs];
    IF OBJECT_ID(N'[dbo].[Notifications]', N'U') IS NOT NULL DELETE FROM [dbo].[Notifications];
    IF OBJECT_ID(N'[dbo].[Memberships]', N'U') IS NOT NULL DELETE FROM [dbo].[Memberships];
    IF OBJECT_ID(N'[dbo].[Disputes]', N'U') IS NOT NULL DELETE FROM [dbo].[Disputes];
    IF OBJECT_ID(N'[dbo].[Reservations]', N'U') IS NOT NULL DELETE FROM [dbo].[Reservations];
    IF OBJECT_ID(N'[dbo].[ConditionReports]', N'U') IS NOT NULL DELETE FROM [dbo].[ConditionReports];
    IF OBJECT_ID(N'[dbo].[Loans]', N'U') IS NOT NULL DELETE FROM [dbo].[Loans];
    IF OBJECT_ID(N'[dbo].[LoanRequests]', N'U') IS NOT NULL DELETE FROM [dbo].[LoanRequests];
    IF OBJECT_ID(N'[dbo].[BookNotes]', N'U') IS NOT NULL DELETE FROM [dbo].[BookNotes];
    IF OBJECT_ID(N'[dbo].[ReadingProgresses]', N'U') IS NOT NULL DELETE FROM [dbo].[ReadingProgresses];
    IF OBJECT_ID(N'[dbo].[ShelfItems]', N'U') IS NOT NULL DELETE FROM [dbo].[ShelfItems];
    IF OBJECT_ID(N'[dbo].[Shelves]', N'U') IS NOT NULL DELETE FROM [dbo].[Shelves];
    IF OBJECT_ID(N'[dbo].[BookCopies]', N'U') IS NOT NULL DELETE FROM [dbo].[BookCopies];
    IF OBJECT_ID(N'[dbo].[Books]', N'U') IS NOT NULL DELETE FROM [dbo].[Books];
    IF OBJECT_ID(N'[dbo].[Libraries]', N'U') IS NOT NULL DELETE FROM [dbo].[Libraries];
    IF OBJECT_ID(N'[dbo].[Categories]', N'U') IS NOT NULL DELETE FROM [dbo].[Categories];
    IF OBJECT_ID(N'[dbo].[OtpVerifications]', N'U') IS NOT NULL DELETE FROM [dbo].[OtpVerifications];
    IF OBJECT_ID(N'[dbo].[AspNetUserRoles]', N'U') IS NOT NULL DELETE FROM [dbo].[AspNetUserRoles];
    IF OBJECT_ID(N'[dbo].[AspNetUserLogins]', N'U') IS NOT NULL DELETE FROM [dbo].[AspNetUserLogins];
    IF OBJECT_ID(N'[dbo].[AspNetUserClaims]', N'U') IS NOT NULL DELETE FROM [dbo].[AspNetUserClaims];
    IF OBJECT_ID(N'[dbo].[AspNetUserTokens]', N'U') IS NOT NULL DELETE FROM [dbo].[AspNetUserTokens];
    IF OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NOT NULL DELETE FROM [dbo].[AspNetUsers];
    IF OBJECT_ID(N'[dbo].[AspNetRoles]', N'U') IS NOT NULL DELETE FROM [dbo].[AspNetRoles];
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

/* =========================================================================================
1. ROLES - 4 vai trò
   Password mẫu cho cả 4 tài khoản: BookShare@2026
   (BCrypt hash: AQAAAAIAAYagAAAAEG7d7M9vOQqzJ5r3W8R8k1gUu9jwGJlO8gQa8nRM8g==)
========================================================================================= */
SET IDENTITY_INSERT [dbo].[AspNetRoles] ON;
INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp]) VALUES
(N'role-admin-0001', N'Admin',     N'ADMIN',     NEWID()),
(N'role-librarian-02', N'Librarian', N'LIBRARIAN', NEWID()),
(N'role-curator-0003', N'Curator',   N'CURATOR',   NEWID()),
(N'role-user-0004',    N'User',      N'USER',      NEWID());
SET IDENTITY_INSERT [dbo].[AspNetRoles] OFF;
GO

/* =========================================================================================
2. USERS - 4 tài khoản mẫu theo 4 vai trò
   Password cho tất cả: BookShare@2026 (BCrypt hash chuẩn ASP.NET Identity)
========================================================================================= */
DECLARE @PwdHash NVARCHAR(MAX) = N'AQAAAAIAAYagAAAAEE7hfwYqV6cqU6kRtJWn6Gddqz4wTq2w5wLs8sR8m+h7gLg8NzPRFqLxU7/X8lTQqg==';

INSERT INTO [dbo].[AspNetUsers]
    ([Id], [UserName], [NormalizedUserName], [Email], [NormalizedEmail], [EmailConfirmed],
     [PasswordHash], [SecurityStamp], [ConcurrencyStamp], [PhoneNumber], [PhoneNumberConfirmed],
     [TwoFactorEnabled], [LockoutEnd], [LockoutEnabled], [AccessFailedCount],
     [FullName], [Address], [AvatarUrl], [TrustScore], [LendQuotaBonus])
VALUES
(N'user-admin-bit250210', N'admin@cmcuni.edu.vn',     N'ADMIN@CMCUNI.EDU.VN',
 N'admin@cmcuni.edu.vn',    N'ADMIN@CMCUNI.EDU.VN', 1,
 @PwdHash, NEWID(), NEWID(), N'0901000001', 1, 0, NULL, 1, 0,
 N'Nguyễn Quản Trị',  N'Số 1, Đường số 2, P. An Lạc, Q. Bình Tân, TP. HCM',
 N'/uploads/avatars/admin.png', 200, 5),

(N'user-librarian-02', N'librarian@cmcuni.edu.vn', N'LIBRARIAN@CMCUNI.EDU.VN',
 N'librarian@cmcuni.edu.vn', N'LIBRARIAN@CMCUNI.EDU.VN', 1,
 @PwdHash, NEWID(), NEWID(), N'0902000002', 1, 0, NULL, 1, 0,
 N'Trần Thủ Thư', N'17 Tôn Đức Thắng, P. Bến Nghé, Q. 1, TP. HCM',
 N'/uploads/avatars/librarian.png', 180, 2),

(N'user-curator-03', N'curator@cmcuni.edu.vn', N'CURATOR@CMCUNI.EDU.VN',
 N'curator@cmcuni.edu.vn', N'CURATOR@CMCUNI.EDU.VN', 1,
 @PwdHash, NEWID(), NEWID(), N'0903000003', 1, 0, NULL, 1, 0,
 N'Lê Hoàn Lâm (Curator)', N'Ký túc xá CMC Uni, Q. 12, TP. HCM',
 N'/uploads/avatars/curator.png', 150, 1),

(N'user-normal-04', N'user04@cmcuni.edu.vn', N'USER04@CMCUNI.EDU.VN',
 N'user04@cmcuni.edu.vn', N'USER04@CMCUNI.EDU.VN', 1,
 @PwdHash, NEWID(), NEWID(), N'0904000004', 0, 0, NULL, 1, 0,
 N'Phạm Văn Bình (User)', N'45 Lê Lợi, P. Bến Nghé, Q. 1, TP. HCM',
 N'/uploads/avatars/user04.png', 100, 0);
GO

-- Gán role cho user (N-N)
INSERT INTO [dbo].[AspNetUserRoles] ([UserId], [RoleId]) VALUES
(N'user-admin-bit250210', N'role-admin-0001'),
(N'user-librarian-02',    N'role-librarian-02'),
(N'user-curator-03',      N'role-curator-0003'),
(N'user-normal-04',      N'role-user-0004');
GO

/* =========================================================================================
3. CATEGORIES - 8 thể loại sách
========================================================================================= */
SET IDENTITY_INSERT [dbo].[Categories] ON;
INSERT INTO [dbo].[Categories] ([Id], [Name], [Slug], [IconClass]) VALUES
(1, N'Công nghệ thông tin & AI',  N'cong-nghe-thong-tin-ai',  N'bi-cpu'),
(2, N'Văn học Việt Nam',          N'van-hoc-viet-nam',        N'bi-book'),
(3, N'Kinh tế - Quản trị',        N'kinh-te-quan-tri',        N'bi-graph-up'),
(4, N'Khoa học - kỹ thuật',       N'khoa-hoc-ky-thuat',       N'bi-rocket-takeoff'),
(5, N'Tâm lý học - Phát triển bản thân', N'tam-ly-phat-trien-ban-than', N'bi-people'),
(6, N'Lịch sử - Địa lý',          N'lich-su-dia-ly',          N'bi-clock-history'),
(7, N'Ngoại ngữ',                 N'ngoai-ngu',               N'bi-translate'),
(8, N'Thiếu nhi',                 N'thieu-nhi',               N'bi-balloon');
SET IDENTITY_INSERT [dbo].[Categories] OFF;
GO

/* =========================================================================================
4. LIBRARIES - 3 kho sách / Hub (ĐH CMC Library, Dropoff Hub, Club Station)
   ManagerUserId: Admin hoặc Librarian
========================================================================================= */
SET IDENTITY_INSERT [dbo].[Libraries] ON;
INSERT INTO [dbo].[Libraries] ([Id], [Name], [Address], [ManagerUserId], [Phone], [HubType], [OpenHours], [IsActive]) VALUES
(1, N'Thư viện ĐH CMC (Cơ sở chính)',
    N'Tầng 4, Tòa A, CMC University, 17 Tôn Đức Thắng, Q. 1, TP. HCM',
    N'user-admin-bit250210', N'028-7300-1001', N'Library', N'08:00 - 17:30 (T2 - T7)', 1),
(2, N'Trạm giao nhận CLB Sách CMC',
    N'Sảnh tầng 1, Tòa B, CMC University, 17 Tôn Đức Thắng, Q. 1, TP. HCM',
    N'user-librarian-02',    N'028-7300-1002', N'DropoffHub', N'09:00 - 18:00 (T2 - T6)', 1),
(3, N'Trạm CLB Đọc Sách Đêm (Club Station)',
    N'Ký túc xá CMC Uni, Q. 12, TP. HCM',
    N'user-curator-03',      N'028-7300-1003', N'ClubStation', N'18:00 - 22:00 (T2 - CN)', 1);
SET IDENTITY_INSERT [dbo].[Libraries] OFF;
GO

/* =========================================================================================
5. BOOKS - 30 đầu sách (ISBN thật theo chuẩn ISBN-13)
========================================================================================= */
SET IDENTITY_INSERT [dbo].[Books] ON;
INSERT INTO [dbo].[Books]
    ([Id], [Title], [Author], [ISBN], [Publisher], [PublishedYear], [PageCount], [CategoryId], [CoverImageUrl], [Summary])
VALUES
-- 5 sách IT & AI
(1,  N'Lập Trình ASP.NET Core MVC (.NET 10)',  N'Andrew Lock',          N'9781803238001', N'Packt Publishing',        2025, 520, 1, N'/covers/aspnet-core-mvc.jpg',
     N'Hướng dẫn toàn diện xây dựng ứng dụng web ASP.NET Core MVC trên .NET 10, từ kiến trúc middleware đến bảo mật OTP và Identity.'),
(2,  N'C# 12 và .NET 10 - Hiệu năng & Tối ưu',  N'Mark J. Price',        N'9781837638002', N'Packt Publishing',        2025, 480, 1, N'/covers/csharp12.jpg',
     N'Tối ưu hiệu năng ứng dụng .NET 10 với C# 12, ref struct, Span<T>, AOT compile và memory pooling.'),
(3,  N'Entity Framework Core 10 - Từ Zero đến Hero', N'Jon P. Smith',   N'9781805123003', N'Packt Publishing',        2025, 360, 1, N'/covers/efcore10.jpg',
     N'Thibết kế CSDL với EF Core 10, Code-First, Database-First scaffold, Migration và tối ưu truy vấn LINQ.'),
(4,  N'AI Trí Tuệ Nhân Tạo Tạo Sinh với .NET',  N'Guido van Rossum',    N'9780135957004', N'Pearson Education',       2024, 600, 1, N'/covers/ai-dotnet.jpg',
     N'Tích hợp Gemini API, OpenAI và Semantic Kernel vào ứng dụng .NET, RAG với SQL Server pgvector.'),
(5,  N'Clean Architecture với .NET',            N'Jason Taylor',         N'9781800561005', N'O''Reilly Media',         2024, 420, 1, N'/covers/clean-arch.jpg',
     N'Áp dụng Clean Architecture, CQRS, MediatR, Vertical Slice trong dự án ASP.NET Core.'),

-- 4 sách Văn học Việt Nam
(6,  N'Truyện Kiều (Bản chiếu minh)',           N'Nguyễn Du',            N'9786045807006', N'NXB Văn Học',             2018, 432, 2, N'/covers/truyen-kieu.jpg',
     N'Bản in chiếu minh Truyện Kiều với chú thích của Hội Nhà văn Việt Nam.'),
(7,  N'Chi Pheo (Tái bản 2024)',                N'Nam Cao',              N'9786046807007', N'NXB Hội Nhà Văn',         2024, 256, 2, N'/covers/chi-pheo.jpg',
     N'Truyện ngắn Nam Cao tiêu biểu cho phong trào Thơ Mới, tái bản kèm phân tích văn học.'),
(8,  N'Số Đỏ',                                  N'Vũ Trọng Phụng',       N'9786047807008', N'NXB Văn Học',            2019, 384, 2, N'/covers/so-do.jpg',
     N'Tiểu thuyết châm biếm xã hội Việt Nam thời thuộc Pháp.'),
(9,  N'Vợ Nhặt',                                N'Kim Lân',              N'9786048807009', N'NXB Giáo Dục',           2020, 124, 2, N'/covers/vo-nhat.jpg',
     N'Truyện ngắn xuất sắc viết về nạn đói năm 1945 và khát vọng sống.'),

-- 4 sách Kinh tế - Quản trị
(10, N'Đắc Nhân Tâm (Bản chuẩn)',               N'Dale Carnegie',        N'9786045001010', N'NXB Lao Động',            2022, 320, 3, N'/covers/dac-nhan-tam.jpg',
     N'Nghệ thuật giao tiếp và thu hút lòng người, được dịch sang 38 ngôn ngữ.'),
(11, N'Người Giàu Có Nhất Thành Babylon',       N'George S. Clason',     N'9786045101011', N'NXB Tri Thức',            2021, 192, 3, N'/covers/babylon.jpg',
     N'Những bài học tài chính cá nhân từ thành Babylon cổ đại.'),
(12, N'Tư Duy Phản Biện',                       N'Rolf Dobelli',         N'9786045201012', N'NXB Công Thương VN',      2023, 288, 3, N'/covers/tu-duy-phan-bien.jpg',
     N'Hướng dẫn tư duy phản biện, ra quyết định trong kinh doanh và đời sống.'),
(13, N'Quản Trị Bán Hàng Thời Số Hóa',         N'Phạm Ngọc Ánh',        N'9786045301013', N'NXB Kinh Tế Quốc Dân',    2024, 410, 3, N'/covers/qtvb.jpg',
     N'Quản trị bán hàng đa kênh với CRM, ERP và phân tích dữ liệu.'),

-- 4 sách Khoa học - Kỹ thuật
(14, N'Lược Sử Thời Gian',                      N'Stephen Hawking',      N'9786045401014', N'NXB Tri Thức',            2020, 280, 4, N'/covers/luoc-su-thoi-gian.jpg',
     N'Hawking giới thiệu vũ trụ học, lỗ đen, thuyết tương đối cho người phổ thông.'),
(15, N'Vũ Trụ Trong Vỏ Hạt Dẻ',                 N'Stephen Hawking',      N'9786045501015', N'NXB Tri Thức',            2021, 224, 4, N'/covers/vu-tru.jpg',
     N'Tiếp nối Lược sử thời gian, Hawking trình bày các lý thuyết mới nhất về vũ trụ.'),
(16, N'Hóa Học Vui (Tái bản 2025)',             N'Nguyễn Văn Tú',        N'9786045601016', N'NXB Giáo Dục VN',         2025, 356, 4, N'/covers/hoa-hoc-vui.jpg',
     N'Thiết kế các thí nghiệm hóa học vui cho học sinh THPT.'),
(17, N'Sinh Học Phân Tử Hiện Đại',              N'James Watson',         N'9786045701017', N'NXB Y Học',               2022, 540, 4, N'/covers/sinh-hoc.jpg',
     N'Tổng quan sinh học phân tử: DNA, RNA, protein và kỹ thuật CRISPR-Cas9.'),

-- 4 sách Tâm lý - PT Bản thân
(18, N'Đắc Nhân Tâm (Phiên bản 2024)',          N'Dale Carnegie',        N'9786045801018', N'NXB Lao Động',            2024, 352, 5, N'/covers/dnt-2024.jpg',
     N'Bản cập nhật 2024, có thêm 5 chương về làm việc từ xa và AI.'),
(19, N'Thuật Quản Trị Cảm Xúc',                 N'Tony Buzan',           N'9786045901019', N'NXB Tổng Hợp HCM',        2023, 248, 5, N'/covers/cam-xuc.jpg',
     N'Kỹ năng quản trị cảm xúc với mind mapping và NLP trong công việc.'),
(20, N'Nghệ Thuật Đọc Sách',                    N'Mortimer Adler',       N'9786046001020', N'NXB Tri Thức',            2020, 416, 5, N'/covers/doc-sach.jpg',
     N'Phương pháp đọc sách hiệu quả từ Adler, đào sâu và phản biện.'),
(21, N'7 Thói Quen Hiệu Quả',                   N'Stephen Covey',        N'9786046101021', N'NXB Công Thương VN',      2022, 384, 5, N'/covers/7-thoi-quen.jpg',
     N'7 thói quen của người thành đạt theo phong cách Covey.'),

-- 3 sách Lịch sử - Địa lý
(22, N'Đại Việt Sử Ký Toàn Thư (Tập 1)',         N'Ngô Sĩ Liên',         N'9786046201022', N'NXB Khoa Học Xã Hội',     2019, 624, 6, N'/covers/dvsktt-1.jpg',
     N'Bộ sử biên niên đời Lê, ghi chép từ thời Hồng Bàng đến đời Trần.'),
(23, N'Việt Nam Sử Lược',                       N'Trần Trọng Kim',       N'9786046301023', N'NXB Tổng Hợp HCM',        2021, 432, 6, N'/covers/vn-su-luoc.jpg',
     N'Tác phẩm lịch sử kinh điển của Trần Trọng Kim, tái bản kèm chú thích.'),
(24, N'Địa Lý Việt Nam Tự Nhiên & Kinh Tế',     N'Phạm Hoàng Hải',       N'9786046401024', N'NXB Giáo Dục VN',         2023, 280, 6, N'/covers/dia-ly-vn.jpg',
     N'Tổng quan địa lý tự nhiên và kinh tế Việt Nam theo 63 tỉnh thành.'),

-- 3 sách Ngoại ngữ
(25, N'English Grammar In Use (5th Edition)',    N'Raymond Murphy',       N'9781108457651', N'Cambridge University Press', 2019, 394, 7, N'/covers/egiu.jpg',
     N'Sách ngữ pháp tiếng Anh kinh điển dành cho người tự học.'),
(26, N'TOEIC Vocabulary Builder 2025',            N'Lin Lougheed',        N'9786046601026', N'NXB Đại Học Ngoại Ngữ',   2025, 320, 7, N'/covers/toeic.jpg',
     N'Từ vựng TOEIC 2025 đầy đủ các chủ đề thương mại.'),
(27, N'IELTS Academic Practice 2024',             N'Cambridge ESOL',     N'9781108457705', N'Cambridge University Press', 2024, 280, 7, N'/covers/ielts.jpg',
     N'Bài luyện IELTS Academic 2024 kèm audio QR.'),

-- 3 sách Thiếu nhi
(28, N'Dế Mèn Phiêu Lưu Ký',                     N'Tô Hoài',              N'9786046801028', N'NXB Kim Đồng',            2020, 248, 8, N'/covers/de-men.jpg',
     N'Truyện thiếu nhi kinh điển của Tô Hoài, tái bản 2020.'),
(29, N'Tấm Cám (Truyện cổ tích Việt Nam)',       N'Nhiều tác giả',        N'9786046901029', N'NXB Kim Đồng',            2021, 168, 8, N'/covers/tam-cam.jpg',
     N'Tập hợp truyện cổ tích Tấm Cám và các dị bản.'),
(30, N'Con Chó Sói (Bản dịch 2025)',             N'Jack London',          N'9786047001030', N'NXB Văn Học',             2025, 220, 8, N'/covers/cho-soi.jpg',
     N'Bản dịch mới Jack London, kể về chú chó Buck hoang dã ở Alaska.');
SET IDENTITY_INSERT [dbo].[Books] OFF;
GO

/* =========================================================================================
6. BOOKCOPIES - 50 bản sao sách giấy vật lý
   - 30 bản sao thuộc Kho chung ĐH CMC (OwnerType = System, LibraryId = 1)
   - 20 bản sao thuộc Tủ sách cá nhân P2P (OwnerType = User, OwnerUserId được set)
   Trạng thái (Status) phân bổ: Available / Borrowed / Reserved / Lost
========================================================================================= */
SET IDENTITY_INSERT [dbo].[BookCopies] ON;
-- 30 bản sao System (Library) tại ĐH CMC (LibraryId = 1)
INSERT INTO [dbo].[BookCopies] ([Id], [BookId], [Barcode], [OwnerType], [LibraryId], [OwnerUserId], [ShelfLocation], [Condition], [Status], [IsLendable]) VALUES
(1,  1,  N'BS-LIB-0001', N'System', 1, NULL, N'Kệ A1-01', N'New',    N'Available', 1),
(2,  1,  N'BS-LIB-0002', N'System', 1, NULL, N'Kệ A1-02', N'Good',   N'Borrowed',  1),
(3,  1,  N'BS-LIB-0003', N'System', 1, NULL, N'Kệ A1-03', N'Good',   N'Available', 1),
(4,  2,  N'BS-LIB-0004', N'System', 1, NULL, N'Kệ A2-01', N'New',    N'Available', 1),
(5,  2,  N'BS-LIB-0005', N'System', 1, NULL, N'Kệ A2-02', N'Good',   N'Reserved',  1),
(6,  3,  N'BS-LIB-0006', N'System', 1, NULL, N'Kệ A3-01', N'Good',   N'Available', 1),
(7,  4,  N'BS-LIB-0007', N'System', 1, NULL, N'Kệ A4-01', N'Good',   N'Borrowed',  1),
(8,  5,  N'BS-LIB-0008', N'System', 1, NULL, N'Kệ A5-01', N'New',    N'Available', 1),
(9,  6,  N'BS-LIB-0009', N'System', 1, NULL, N'Kệ B1-01', N'Fair',   N'Available', 1),
(10, 6,  N'BS-LIB-0010', N'System', 1, NULL, N'Kệ B1-02', N'Good',   N'Borrowed',  1),
(11, 7,  N'BS-LIB-0011', N'System', 1, NULL, N'Kệ B2-01', N'Good',   N'Available', 1),
(12, 8,  N'BS-LIB-0012', N'System', 1, NULL, N'Kệ B3-01', N'Worn',   N'Available', 1),
(13, 10, N'BS-LIB-0013', N'System', 1, NULL, N'Kệ C1-01', N'New',    N'Available', 1),
(14, 10, N'BS-LIB-0014', N'System', 1, NULL, N'Kệ C1-02', N'Good',   N'Borrowed',  1),
(15, 11, N'BS-LIB-0015', N'System', 1, NULL, N'Kệ C2-01', N'Good',   N'Available', 1),
(16, 12, N'BS-LIB-0016', N'System', 1, NULL, N'Kệ C3-01', N'Good',   N'Available', 1),
(17, 14, N'BS-LIB-0017', N'System', 1, NULL, N'Kệ D1-01', N'New',    N'Available', 1),
(18, 15, N'BS-LIB-0018', N'System', 1, NULL, N'Kệ D2-01', N'Good',   N'Borrowed',  1),
(19, 18, N'BS-LIB-0019', N'System', 1, NULL, N'Kệ E1-01', N'New',    N'Available', 1),
(20, 21, N'BS-LIB-0020', N'System', 1, NULL, N'Kệ E2-01', N'Good',   N'Available', 1),
(21, 22, N'BS-LIB-0021', N'System', 1, NULL, N'Kệ F1-01', N'Fair',   N'Available', 1),
(22, 23, N'BS-LIB-0022', N'System', 1, NULL, N'Kệ F2-01', N'Good',   N'Available', 1),
(23, 25, N'BS-LIB-0023', N'System', 1, NULL, N'Kệ G1-01', N'New',    N'Available', 1),
(24, 25, N'BS-LIB-0024', N'System', 1, NULL, N'Kệ G1-02', N'Good',   N'Borrowed',  1),
(25, 28, N'BS-LIB-0025', N'System', 1, NULL, N'Kệ H1-01', N'Good',   N'Available', 1),
(26, 28, N'BS-LIB-0026', N'System', 1, NULL, N'Kệ H1-02', N'Good',   N'Available', 1),
(27, 9,  N'BS-LIB-0027', N'System', 1, NULL, N'Kệ B4-01', N'Worn',   N'Lost',      0),
(28, 17, N'BS-LIB-0028', N'System', 1, NULL, N'Kệ D3-01', N'Good',   N'Damaged',   0),
(29, 13, N'BS-LIB-0029', N'System', 1, NULL, N'Kệ C4-01', N'New',    N'Available', 1),
(30, 24, N'BS-LIB-0030', N'System', 1, NULL, N'Kệ F3-01', N'Good',   N'Available', 1);

-- 20 bản sao User (P2P) thuộc 3 user (Curator 03, User 04, Librarian 02)
INSERT INTO [dbo].[BookCopies] ([Id], [BookId], [Barcode], [OwnerType], [LibraryId], [OwnerUserId], [ShelfLocation], [Condition], [Status], [IsLendable]) VALUES
(31, 1,  N'BS-USER-0031', N'User', NULL, N'user-curator-03', N'Tủ nhà Lâm - Kệ 1', N'New',    N'Available', 1),
(32, 1,  N'BS-USER-0032', N'User', NULL, N'user-normal-04',  N'Tủ nhà Bình - Kệ 1', N'Good',   N'Available', 1),
(33, 2,  N'BS-USER-0033', N'User', NULL, N'user-curator-03', N'Tủ nhà Lâm - Kệ 1', N'Good',   N'Available', 1),
(34, 4,  N'BS-USER-0034', N'User', NULL, N'user-normal-04',  N'Tủ nhà Bình - Kệ 1', N'New',    N'Available', 1),
(35, 5,  N'BS-USER-0035', N'User', NULL, N'user-librarian-02', N'Tủ nhà Thủ Thư - Kệ 1', N'Good', N'Available', 1),
(36, 5,  N'BS-USER-0036', N'User', NULL, N'user-curator-03', N'Tủ nhà Lâm - Kệ 2', N'Good',   N'Borrowed',  1),
(37, 6,  N'BS-USER-0037', N'User', NULL, N'user-normal-04',  N'Tủ nhà Bình - Kệ 1', N'Fair',   N'Available', 1),
(38, 7,  N'BS-USER-0038', N'User', NULL, N'user-curator-03', N'Tủ nhà Lâm - Kệ 2', N'Good',   N'Available', 1),
(39, 10, N'BS-USER-0039', N'User', NULL, N'user-librarian-02', N'Tủ nhà Thủ Thư - Kệ 1', N'New', N'Available', 1),
(40, 11, N'BS-USER-0040', N'User', NULL, N'user-normal-04',  N'Tủ nhà Bình - Kệ 2', N'Good',   N'Available', 1),
(41, 12, N'BS-USER-0041', N'User', NULL, N'user-curator-03', N'Tủ nhà Lâm - Kệ 2', N'Good',   N'Available', 1),
(42, 14, N'BS-USER-0042', N'User', NULL, N'user-normal-04',  N'Tủ nhà Bình - Kệ 2', N'Good',   N'Available', 1),
(43, 18, N'BS-USER-0043', N'User', NULL, N'user-curator-03', N'Tủ nhà Lâm - Kệ 3', N'New',    N'Available', 1),
(44, 21, N'BS-USER-0044', N'User', NULL, N'user-librarian-02', N'Tủ nhà Thủ Thư - Kệ 1', N'Good', N'Available', 1),
(45, 25, N'BS-USER-0045', N'User', NULL, N'user-normal-04',  N'Tủ nhà Bình - Kệ 2', N'Good',   N'Borrowed',  1),
(46, 28, N'BS-USER-0046', N'User', NULL, N'user-curator-03', N'Tủ nhà Lâm - Kệ 3', N'Good',   N'Available', 1),
(47, 3,  N'BS-USER-0047', N'User', NULL, N'user-normal-04',  N'Tủ nhà Bình - Kệ 3', N'Good',   N'Available', 1),
(48, 8,  N'BS-USER-0048', N'User', NULL, N'user-curator-03', N'Tủ nhà Lâm - Kệ 3', N'Fair',   N'Available', 1),
(49, 13, N'BS-USER-0049', N'User', NULL, N'user-librarian-02', N'Tủ nhà Thủ Thư - Kệ 2', N'New', N'Available', 1),
(50, 30, N'BS-USER-0050', N'User', NULL, N'user-normal-04',  N'Tủ nhà Bình - Kệ 3', N'Good',   N'Available', 1);
SET IDENTITY_INSERT [dbo].[BookCopies] OFF;
GO

/* =========================================================================================
7. SHELVES - Kệ sách cá nhân cho 3 user (Curator 03, User 04, Librarian 02)
========================================================================================= */
SET IDENTITY_INSERT [dbo].[Shelves] ON;
INSERT INTO [dbo].[Shelves] ([Id], [UserId], [Name], [Description], [IsPublic], [DisplayOrder]) VALUES
(1, N'user-curator-03', N'Đang đọc',     N'Sách đang trong tiến độ đọc', 1, 1),
(2, N'user-curator-03', N'Yêu thích',    N'Những cuốn sách tâm đắc',     1, 2),
(3, N'user-curator-03', N'Cho mượn P2P',  N'Sách sẵn sàng cho cộng đồng mượn', 1, 3),
(4, N'user-normal-04',  N'Đang đọc',     N'Tiến độ đọc hiện tại',        1, 1),
(5, N'user-normal-04',  N'Đã đọc',       N'Sách đã đọc xong',            1, 2),
(6, N'user-librarian-02', N'Tham khảo', N'Sách tham khảo công việc',    0, 1);
SET IDENTITY_INSERT [dbo].[Shelves] OFF;
GO

-- ShelfItems: gắn BookCopy vào Shelf tương ứng
SET IDENTITY_INSERT [dbo].[ShelfItems] ON;
INSERT INTO [dbo].[ShelfItems] ([Id], [ShelfId], [BookCopyId]) VALUES
(1, 1, 31),  -- Lâm đang đọc BookCopy 31 (ASP.NET Core MVC)
(2, 3, 33),  -- Lâm cho mượn BookCopy 33 (C# 12)
(3, 3, 36),  -- Lâm cho mượn BookCopy 36 (Clean Architecture)
(4, 2, 38),  -- Lâm yêu thích BookCopy 38 (Chi Pheo)
(5, 4, 32),  -- Bình đang đọc BookCopy 32 (ASP.NET Core MVC)
(6, 5, 34),  -- Bình đã đọc BookCopy 34 (AI)
(7, 6, 35),  -- Thủ Thư tham khảo BookCopy 35 (Clean Architecture)
(8, 6, 39),  -- Thủ Thư tham khảo BookCopy 39 (Đắc Nhân Tâm)
(9, 2, 43),  -- Lâm yêu thích BookCopy 43 (Đắc Nhân Tâm 2024)
(10, 3, 41); -- Lâm cho mượn BookCopy 41 (Tư Duy Phản Biện)
SET IDENTITY_INSERT [dbo].[ShelfItems] OFF;
GO

/* =========================================================================================
8. READINGPROGRESSES - Tiến độ đọc sách (cho Dashboard S03)
========================================================================================= */
SET IDENTITY_INSERT [dbo].[ReadingProgresses] ON;
INSERT INTO [dbo].[ReadingProgresses]
    ([Id], [UserId], [BookId], [CurrentPage], [TotalPages], [Priority], [TargetDeadline], [Status], [StartedAt], [FinishedAt], [PersonalRating])
VALUES
(1, N'user-curator-03', 1,  120, 520, N'High',   DATEADD(DAY, 30, SYSUTCDATETIME()), N'Reading',   DATEADD(DAY, -10, SYSUTCDATETIME()), NULL, NULL),
(2, N'user-curator-03', 5,  280, 420, N'Medium', DATEADD(DAY, 45, SYSUTCDATETIME()), N'Reading',   DATEADD(DAY, -20, SYSUTCDATETIME()), NULL, NULL),
(3, N'user-curator-03', 10, 320, 320, N'Low',    NULL,                              N'Completed', DATEADD(DAY, -60, SYSUTCDATETIME()), DATEADD(DAY, -30, SYSUTCDATETIME()), 5),
(4, N'user-curator-03', 14,  0,  280, N'Medium', NULL,                              N'WantToRead', NULL, NULL, NULL),
(5, N'user-normal-04',  1,   60, 520, N'High',   DATEADD(DAY, 21, SYSUTCDATETIME()), N'Reading',   DATEADD(DAY, -5,  SYSUTCDATETIME()), NULL, NULL),
(6, N'user-normal-04',  11, 192, 192, N'Low',    NULL,                              N'Completed', DATEADD(DAY, -45, SYSUTCDATETIME()), DATEADD(DAY, -15, SYSUTCDATETIME()), 4),
(7, N'user-normal-04',  25,  80, 394, N'Medium', DATEADD(DAY, 60, SYSUTCDATETIME()), N'Reading',   DATEADD(DAY, -7,  SYSUTCDATETIME()), NULL, NULL),
(8, N'user-librarian-02', 5, 100, 420, N'Medium', NULL,                             N'Reading',   DATEADD(DAY, -3,  SYSUTCDATETIME()), NULL, NULL),
(9, N'user-librarian-02', 28, 248, 248, N'Low',   NULL,                              N'Completed', DATEADD(DAY, -90, SYSUTCDATETIME()), DATEADD(DAY, -60, SYSUTCDATETIME()), 5),
(10, N'user-curator-03', 28, 120, 248, N'Low',   NULL,                              N'Reading',   DATEADD(DAY, -2,  SYSUTCDATETIME()), NULL, NULL);
SET IDENTITY_INSERT [dbo].[ReadingProgresses] OFF;
GO

-- BookNotes: Ghi chú cá nhân theo trang
SET IDENTITY_INSERT [dbo].[BookNotes] ON;
INSERT INTO [dbo].[BookNotes] ([Id], [ReadingProgressId], [PageNumber], [QuoteText], [NoteContent], [IsPublic]) VALUES
(1, 1, 25, N'Middleware pipeline là trái tim của ASP.NET Core', N'Nắm vững thứ tự middleware để optimize request flow.', 1),
(2, 1, 120, N'EF Core 10 supports AOT compile for LINQ',        N'Thử dùng AOT trong dự án sprint tới.',                 1),
(3, 2, 50,  N'Clean Architecture tách biệt business logic',     N'Áp dụng cho module S08 Loan service.',                  0),
(4, 5, 30,  N'Identity có thể custom claim qua IClaimsTransformation', N'Custom claim để gán TrustScore vào JWT.',     1),
(5, 7, 80,  N'Cambridge recommends spaced repetition',           N'Ôn từ vựng TOEIC theo phương pháp Spaced Repetition.', 1);
SET IDENTITY_INSERT [dbo].[BookNotes] OFF;
GO

/* =========================================================================================
9. LOANREQUESTS - Lịch sử yêu cầu mượn (cho Dashboard S22 ratio Library vs P2P)
========================================================================================= */
SET IDENTITY_INSERT [dbo].[LoanRequests] ON;
INSERT INTO [dbo].[LoanRequests]
    ([Id], [BookCopyId], [BorrowerId], [ApproverId], [LoanType], [Status], [RequestedAt], [DesiredDays], [PickupHubId], [RequestNote])
VALUES
-- Library loan (Kho chung)
(1,  2,  N'user-normal-04',  N'user-librarian-02', N'Library', N'Approved',  DATEADD(DAY, -25, SYSUTCDATETIME()), 14, 1, N'Mượn học ASP.NET Core'),
(2,  5,  N'user-curator-03', N'user-librarian-02', N'Library', N'Approved',  DATEADD(DAY, -22, SYSUTCDATETIME()), 14, 1, N'Nghiên cứu .NET 10'),
(3,  7,  N'user-normal-04',  N'user-librarian-02', N'Library', N'Approved',  DATEADD(DAY, -20, SYSUTCDATETIME()), 21, 1, N'Tài liệu AI'),
(4,  10, N'user-normal-04',  N'user-librarian-02', N'Library', N'Approved',  DATEADD(DAY, -18, SYSUTCDATETIME()), 14, 2, N'Văn học Việt'),
(5,  14, N'user-curator-03', N'user-librarian-02', N'Library', N'Approved',  DATEADD(DAY, -15, SYSUTCDATETIME()), 30, 1, N'Sách khoa học'),
(6,  18, N'user-normal-04',  N'user-librarian-02', N'Library', N'Approved',  DATEADD(DAY, -10, SYSUTCDATETIME()), 14, 1, N'Đắc nhân tâm 2024'),
(7,  24, N'user-curator-03', N'user-librarian-02', N'Library', N'Rejected',  DATEADD(DAY, -8,  SYSUTCDATETIME()), 14, 1, N'TOEIC đang mượn nhiều'),
(8,  25, N'user-normal-04',  N'user-librarian-02', N'Library', N'Approved',  DATEADD(DAY, -5,  SYSUTCDATETIME()), 10, 1, N'IELTS luyện thi'),
(9,  30, N'user-curator-03', N'user-librarian-02', N'Library', N'Pending',   DATEADD(DAY, -1,  SYSUTCDATETIME()), 14, 1, N'Chờ duyệt'),

-- P2P loan (Tủ sách cá nhân)
(10, 31, N'user-normal-04',  N'user-curator-03', N'P2P', N'Approved', DATEADD(DAY, -7,  SYSUTCDATETIME()), 7,  3, N'Mượn của anh Lâm'),
(11, 36, N'user-normal-04',  N'user-curator-03', N'P2P', N'Approved', DATEADD(DAY, -6,  SYSUTCDATETIME()), 10, 3, N'Mượn Clean Architecture'),
(12, 39, N'user-curator-03', N'user-librarian-02', N'P2P', N'Approved', DATEADD(DAY, -5,  SYSUTCDATETIME()), 14, 2, N'Mượn sách Đắc Nhân Tâm'),
(13, 41, N'user-normal-04',  N'user-curator-03', N'P2P', N'Pending',  DATEADD(DAY, -1,  SYSUTCDATETIME()), 7,  3, N'Mượn Tư duy phản biện'),
(14, 45, N'user-curator-03', N'user-normal-04',  N'P2P', N'Approved', DATEADD(DAY, -3,  SYSUTCDATETIME()), 14, 3, N'Mượn English Grammar');
SET IDENTITY_INSERT [dbo].[LoanRequests] OFF;
GO

/* =========================================================================================
10. LOANS - Hợp đồng mượn đang hiệu lực, đã trả, quá hạn (cho Dashboard S22)
========================================================================================= */
SET IDENTITY_INSERT [dbo].[Loans] ON;
INSERT INTO [dbo].[Loans]
    ([Id], [LoanRequestId], [StartDate], [DueDate], [ReturnDate], [RenewCount], [FineAmount], [Status])
VALUES
-- Library loans
(1,  1, DATEADD(DAY, -25, SYSUTCDATETIME()), DATEADD(DAY, -11, SYSUTCDATETIME()), DATEADD(DAY, -10, SYSUTCDATETIME()), 0, 0,         N'Returned'),
(2,  2, DATEADD(DAY, -22, SYSUTCDATETIME()), DATEADD(DAY, -8,  SYSUTCDATETIME()), DATEADD(DAY, -7,  SYSUTCDATETIME()), 1, 0,         N'Returned'),
(3,  3, DATEADD(DAY, -20, SYSUTCDATETIME()), DATEADD(DAY, 1,   SYSUTCDATETIME()), NULL,                              0, 0,         N'Active'),
(4,  4, DATEADD(DAY, -18, SYSUTCDATETIME()), DATEADD(DAY, -4,  SYSUTCDATETIME()), DATEADD(DAY, -3,  SYSUTCDATETIME()), 0, 0,         N'Returned'),
(5,  5, DATEADD(DAY, -15, SYSUTCDATETIME()), DATEADD(DAY, 15,  SYSUTCDATETIME()), NULL,                              0, 0,         N'Active'),
(6,  6, DATEADD(DAY, -10, SYSUTCDATETIME()), DATEADD(DAY, 4,   SYSUTCDATETIME()), NULL,                              0, 0,         N'Active'),
(8,  8, DATEADD(DAY, -5,  SYSUTCDATETIME()), DATEADD(DAY, 5,   SYSUTCDATETIME()), NULL,                              0, 0,         N'Active'),
-- P2P loans
(9,  10, DATEADD(DAY, -7,  SYSUTCDATETIME()), DATEADD(DAY, 0,   SYSUTCDATETIME()), NULL,                              0, 0,         N'Overdue'),
(10, 11, DATEADD(DAY, -6,  SYSUTCDATETIME()), DATEADD(DAY, 4,   SYSUTCDATETIME()), NULL,                              0, 0,         N'Active'),
(11, 12, DATEADD(DAY, -5,  SYSUTCDATETIME()), DATEADD(DAY, 9,   SYSUTCDATETIME()), NULL,                              0, 0,         N'Active'),
(14, 14, DATEADD(DAY, -3,  SYSUTCDATETIME()), DATEADD(DAY, 11,  SYSUTCDATETIME()), NULL,                              0, 0,         N'Active'),
-- Returned loans with fine
(15, 1,  DATEADD(DAY, -35, SYSUTCDATETIME()), DATEADD(DAY, -21, SYSUTCDATETIME()), DATEADD(DAY, -15, SYSUTCDATETIME()), 0, 30000, N'Returned');
SET IDENTITY_INSERT [dbo].[Loans] OFF;
GO

/* =========================================================================================
11. CONDITIONREPORTS - Biên bản điện tử lúc giao/trả
========================================================================================= */
SET IDENTITY_INSERT [dbo].[ConditionReports] ON;
INSERT INTO [dbo].[ConditionReports]
    ([Id], [LoanId], [ReportType], [ReportedByUserId], [ConditionGrade], [Note], [ImageUrl])
VALUES
(1,  1, N'Handover', N'user-librarian-02', N'Good',  N'Bìa sạch, không rách',  N'/uploads/conditions/loan1-handover.jpg'),
(2,  1, N'Return',   N'user-librarian-02', N'Good',  N'Trả nguyên trạng thái', N'/uploads/conditions/loan1-return.jpg'),
(3,  2, N'Handover', N'user-librarian-02', N'New',    N'Sách mới',              N'/uploads/conditions/loan2-handover.jpg'),
(4,  2, N'Return',   N'user-librarian-02', N'Good',  N'Có gấp 1 trang',        N'/uploads/conditions/loan2-return.jpg'),
(5,  3, N'Handover', N'user-librarian-02', N'Good',  N'Bàn giao sách AI',      N'/uploads/conditions/loan3-handover.jpg'),
(6,  4, N'Handover', N'user-librarian-02', N'Good',  N'Bàn giao Truyện Kiều',  N'/uploads/conditions/loan4-handover.jpg'),
(7,  4, N'Return',   N'user-librarian-02', N'Fair',  N'Hơi cũ sau khi mượn',   N'/uploads/conditions/loan4-return.jpg'),
(8,  5, N'Handover', N'user-librarian-02', N'New',    N'Bàn giao Lược sử thời gian', N'/uploads/conditions/loan5-handover.jpg'),
(9,  6, N'Handover', N'user-librarian-02', N'New',    N'Bàn giao Đắc Nhân Tâm 2024', N'/uploads/conditions/loan6-handover.jpg'),
(10, 9,  N'Handover', N'user-curator-03',  N'New',    N'P2P: Bàn giao ASP.NET Core MVC', N'/uploads/conditions/loan9-handover.jpg'),
(11, 10, N'Handover', N'user-curator-03',  N'Good',   N'P2P: Bàn giao Clean Architecture', N'/uploads/conditions/loan10-handover.jpg'),
(12, 11, N'Handover', N'user-librarian-02', N'New',    N'P2P: Bàn giao Đắc Nhân Tâm',     N'/uploads/conditions/loan11-handover.jpg');
SET IDENTITY_INSERT [dbo].[ConditionReports] OFF;
GO

/* =========================================================================================
12. RESERVATIONS - Hàng chờ đặt trước (cho sách hot)
========================================================================================= */
SET IDENTITY_INSERT [dbo].[Reservations] ON;
INSERT INTO [dbo].[Reservations]
    ([Id], [BookId], [UserId], [QueuePosition], [Status], [CreatedAt], [NotifiedAt], [ExpiresAt])
VALUES
(1, 1, N'user-normal-04',  1, N'Waiting',   DATEADD(DAY, -3, SYSUTCDATETIME()), NULL, NULL),
(2, 1, N'user-curator-03', 2, N'Waiting',   DATEADD(DAY, -2, SYSUTCDATETIME()), NULL, NULL),
(3, 5, N'user-normal-04',  1, N'Notified',  DATEADD(DAY, -1, SYSUTCDATETIME()), DATEADD(HOUR, -2, SYSUTCDATETIME()), DATEADD(HOUR, 22, SYSUTCDATETIME())),
(4, 25, N'user-curator-03', 1, N'Fulfilled', DATEADD(DAY, -10, SYSUTCDATETIME()), DATEADD(DAY, -8, SYSUTCDATETIME()), DATEADD(DAY, -5, SYSUTCDATETIME())),
(5, 28, N'user-normal-04',  1, N'Waiting',  DATEADD(DAY, -1, SYSUTCDATETIME()), NULL, NULL);
SET IDENTITY_INSERT [dbo].[Reservations] OFF;
GO

/* =========================================================================================
13. DISPUTES - Khiếu nại tranh chấp (1 mẫu)
========================================================================================= */
SET IDENTITY_INSERT [dbo].[Disputes] ON;
INSERT INTO [dbo].[Disputes]
    ([Id], [LoanId], [RaisedByUserId], [Reason], [EvidenceUrlsJson], [Status], [ResolvedByAdminId], [ResolutionNote], [CreatedAt], [ResolvedAt])
VALUES
(1, 4, N'user-normal-04', N'Sách Truyện Kiều có rách 5 trang khi trả', N'["/uploads/disputes/dispute1-evidence1.jpg","/uploads/disputes/dispute1-evidence2.jpg"]',
    N'Resolved', N'user-admin-bit250210', N'Admin quyết định người dùng bồi thường 50% giá sách.', DATEADD(DAY, -12, SYSUTCDATETIME()), DATEADD(DAY, -10, SYSUTCDATETIME()));
SET IDENTITY_INSERT [dbo].[Disputes] OFF;
GO

/* =========================================================================================
14. MEMBERSHIPS - Gói thành viên (2 Free + 2 Premium)
========================================================================================= */
SET IDENTITY_INSERT [dbo].[Memberships] ON;
INSERT INTO [dbo].[Memberships]
    ([Id], [UserId], [Tier], [MaxConcurrentLoans], [StartDate], [ExpiryDate], [IsActive])
VALUES
(1, N'user-admin-bit250210', N'Premium', 20, DATEADD(DAY, -90, SYSUTCDATETIME()), DATEADD(DAY, 275, SYSUTCDATETIME()), 1),
(2, N'user-librarian-02',    N'Premium', 10, DATEADD(DAY, -60, SYSUTCDATETIME()), DATEADD(DAY, 305, SYSUTCDATETIME()), 1),
(3, N'user-curator-03',      N'Premium', 5,  DATEADD(DAY, -30, SYSUTCDATETIME()), DATEADD(DAY, 335, SYSUTCDATETIME()), 1),
(4, N'user-normal-04',       N'Free',    2,  DATEADD(DAY, -15, SYSUTCDATETIME()), NULL,                              1);
SET IDENTITY_INSERT [dbo].[Memberships] OFF;
GO

/* =========================================================================================
15. NOTIFICATIONS - Thông báo hệ thống
========================================================================================= */
SET IDENTITY_INSERT [dbo].[Notifications] ON;
INSERT INTO [dbo].[Notifications] ([Id], [UserId], [Title], [Body], [Type], [IsRead]) VALUES
(1, N'user-normal-04', N'Đơn mượn đã được duyệt', N'Đơn mượn sách #1 (ASP.NET Core MVC) đã được thủ thư duyệt. Vui lòng đến thư viện ĐH CMC nhận sách.', N'Success', 1),
(2, N'user-normal-04', N'Sách sắp đến hạn trả',   N'Sách #3 (AI Trí Tuệ Nhân Tạo) sẽ đến hạn trả sau 3 ngày nữa.',                                   N'Warning', 0),
(3, N'user-curator-03', N'Yêu cầu mượn P2P mới',   N'Bình vừa gửi yêu cầu mượn sách Clean Architecture của bạn.',                                  N'Info',    0),
(4, N'user-admin-bit250210', N'Khiếu nại mới',      N'Khiếu nại #1 cần admin phân xử.',                                                              N'Warning', 0),
(5, N'user-curator-03', N'Đăng ký Premium thành công', N'Gói Premium 365 ngày đã được kích hoạt.',                                                 N'Success', 1);
SET IDENTITY_INSERT [dbo].[Notifications] OFF;
GO

/* =========================================================================================
16. AICHATLOGS - Lịch sử chat với AI Gemini
========================================================================================= */
SET IDENTITY_INSERT [dbo].[AiChatLogs] ON;
INSERT INTO [dbo].[AiChatLogs] ([Id], [UserId], [UserMessage], [AiResponse], [ContextJson], [TokensUsed]) VALUES
(1, N'user-curator-03', N'Gợi ý sách học ASP.NET Core MVC cho người mới',
    N'Mình gợi ý: 1) Lập Trình ASP.NET Core MVC (.NET 10) - Andrew Lock; 2) C# 12 và .NET 10 - Mark J. Price; 3) Clean Architecture với .NET - Jason Taylor.',
    N'{"books":[1,2,5]}', 280),
(2, N'user-normal-04', N'Tôi nên bắt đầu học IELTS như thế nào?',
    N'Bạn nên bắt đầu với English Grammar In Use (5th Edition) của Raymond Murphy, sau đó luyện TOEIC Vocabulary Builder 2025 để có nền tảng từ vựng.',
    N'{"books":[25,26]}', 220),
(3, N'user-admin-bit250210', N'Cho biết thống kê mượn sách tháng này',
    N'Tháng này có 12 đơn mượn mới, 8 đã được duyệt, 4 đang chờ. Tỷ lệ Kho chung vs P2P là 7:3.',
    N'{"stats":{"totalLoans":12,"approved":8,"pending":4}}', 180);
SET IDENTITY_INSERT [dbo].[AiChatLogs] OFF;
GO

/* =========================================================================================
17. REVIEWS - Đánh giá đầu sách (BẢNG MỚI Sprint 2 - Lâm)
========================================================================================= */
SET IDENTITY_INSERT [dbo].[Reviews] ON;
INSERT INTO [dbo].[Reviews]
    ([Id], [BookId], [UserId], [Rating], [Title], [Content], [IsPublic], [Likes])
VALUES
(1,  1, N'user-curator-03',  5, N'Sách .NET 10 hay nhất 2025',  N'Nội dung update mới, ví dụ thực tế, phù hợp developer .NET muốn nâng cấp kỹ năng.', 1, 24),
(2,  1, N'user-normal-04',   4, N'Hay nhưng khó',                N'Sách hay nhưng đòi hỏi kiến thức nền .NET 8+. Nên đọc sách cơ bản trước.',         1, 8),
(3,  2, N'user-curator-03',  5, N'Tối ưu hiệu năng',            N'Chương về Span<T> và AOT rất đáng đọc. Áp dụng được ngay vào dự án thực tế.',     1, 18),
(4,  3, N'user-librarian-02', 4, N'EF Core toàn diện',          N'Phần scaffold và migration rất chi tiết, nên có thêm ví dụ về bulk update.',     1, 12),
(5,  6, N'user-curator-03',  5, N'Truyện Kiều kinh điển',       N'Bản chiếu minh giúp hiểu sâu tác phẩm. Đáng sưu tầm.',                            1, 35),
(6, 10, N'user-normal-04',   5, N'Đắc Nhân Tâm kinh điển',      N'Sách gối đầu giường, đọc đi đọc lại vẫn thấy hay.',                              1, 50),
(7, 10, N'user-librarian-02', 4, N'Bản dịch tốt',               N'Dịch giả chu đáo, có chú thích văn hóa phương Tây.',                            1, 22),
(8, 14, N'user-curator-03',  5, N'Sách khoa học xuất sắc',      N'Hawking viết rất dễ hiểu, phù hợp cho người không chuyên.',                      1, 28),
(9, 25, N'user-normal-04',   5, N'Grammar in Use chuẩn',        N'Sách ngữ pháp tiếng Anh kinh điển, dùng cho tự học rất tốt.',                    1, 40),
(10, 28, N'user-curator-03', 5, N'Dế Mèn - Tuổi thơ',            N'Truyện thiếu nhi kinh điển Việt Nam, ảnh hưởng nhiều thế hệ.',                    1, 30);
SET IDENTITY_INSERT [dbo].[Reviews] OFF;
GO

/* =========================================================================================
18. TRANSACTIONS - Giao dịch thanh toán Sandbox (BẢNG MỚI Sprint 2 - Lâm)
   HmacSignature mô phỏng HMAC-SHA-512 của chuỗi: TransactionCode|UserId|Amount|Currency
========================================================================================= */
SET IDENTITY_INSERT [dbo].[Transactions] ON;
INSERT INTO [dbo].[Transactions]
    ([Id], [TransactionCode], [UserId], [MembershipId], [LoanId], [Amount], [Currency], [PaymentMethod], [Status], [HmacSignature], [Description], [PaidAt])
VALUES
(1, N'TXN-MBR-0001', N'user-admin-bit250210', 1, NULL, 299000, N'VND', N'Sandbox', N'Completed',
   N'a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6',
   N'Premium membership 365 ngày', DATEADD(DAY, -90, SYSUTCDATETIME())),
(2, N'TXN-MBR-0002', N'user-librarian-02', 2, NULL, 299000, N'VND', N'MoMo', N'Completed',
   N'b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1',
   N'Premium membership 365 ngày', DATEADD(DAY, -60, SYSUTCDATETIME())),
(3, N'TXN-MBR-0003', N'user-curator-03', 3, NULL, 299000, N'VND', N'BankTransfer', N'Completed',
   N'c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2',
   N'Premium membership 365 ngày', DATEADD(DAY, -30, SYSUTCDATETIME())),
(4, N'TXN-FNE-0004', N'user-normal-04', NULL, 15, 30000, N'VND', N'Sandbox', N'Completed',
   N'd4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3',
   N'Phí phạt trễ hạn trả sách loan #15', DATEADD(DAY, -15, SYSUTCDATETIME())),
(5, N'TXN-MBR-0005', N'user-normal-04', 4, NULL, 0, N'VND', N'Sandbox', N'Pending',
   N'e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4',
   N'Free membership (không thu phí)', NULL),
(6, N'TXN-MBR-0006', N'user-curator-03', 3, NULL, 299000, N'VND', N'Sandbox', N'Failed',
   N'f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5',
   N'Thay đổi Premium (lỗi gateway)', NULL);
SET IDENTITY_INSERT [dbo].[Transactions] OFF;
GO

PRINT N'== [02_BookShare_SeedData.sql] Nạp dữ liệu mẫu hoàn tất ==';
PRINT N'   - 4 users (Admin/Librarian/Curator/User)';
PRINT N'   - 8 categories + 3 libraries';
PRINT N'   - 30 books + 50 book copies (30 System + 20 User P2P)';
PRINT N'   - 6 shelves + 10 shelf items';
PRINT N'   - 10 reading progresses + 5 book notes';
PRINT N'   - 14 loan requests + 11 loans + 12 condition reports';
PRINT N'   - 5 reservations + 1 dispute';
PRINT N'   - 4 memberships + 5 notifications + 3 AI chat logs';
PRINT N'   - 10 reviews + 6 transactions (Sprint 2 bổ sung)';
GO
