/* =========================================================================================
   DỰ ÁN: BOOKSHARE - NỀN TẢNG QUẢN LÝ TỦ SÁCH CÁ NHÂN & MƯỢN TRẢ SÁCH CỘNG ĐỒNG (.NET 10)
   FILE: database/02_BookShare_SeedData.sql
   MÔ TẢ: Dữ liệu mẫu phong phú cho 4 Vai trò, 6 Tài khoản, 3 Thư viện/Hub, 8 Thể loại,
          30 Đầu sách (Books), 50 Bản sao vật lý (BookCopies), Kệ sách, Tiến độ đọc Ver07,
          Đơn mượn Kho chung & P2P, Biên bản ảnh Cloudinary và Hội thoại Gemini AI.
   ========================================================================================= */

USE [BookShareDb];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

/* =========================================================================================
   1. NẠP 4 VAI TRÒ (ASP.NET CORE IDENTITY ROLES)
   ========================================================================================= */
IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles])
BEGIN
    INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
    VALUES
        (N'role-admin',     N'Admin',     N'ADMIN',     N'c1000000-0000-0000-0000-000000000001'),
        (N'role-librarian', N'Librarian', N'LIBRARIAN', N'c1000000-0000-0000-0000-000000000002'),
        (N'role-curator',   N'Curator',   N'CURATOR',   N'c1000000-0000-0000-0000-000000000003'),
        (N'role-user',      N'User',      N'USER',      N'c1000000-0000-0000-0000-000000000004');
END
GO

/* =========================================================================================
   2. NẠP 6 TÀI KHOẢN MẪU ĐẠI DIỆN CHO 4 CẤP PHÂN QUYỀN
   ========================================================================================= */
IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetUsers])
BEGIN
    INSERT INTO [dbo].[AspNetUsers] (
        [Id], [UserName], [NormalizedUserName], [Email], [NormalizedEmail], [EmailConfirmed],
        [PasswordHash], [SecurityStamp], [ConcurrencyStamp], [PhoneNumber], [PhoneNumberConfirmed],
        [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount],
        [FullName], [Address], [AvatarUrl], [TrustScore], [LendQuotaBonus]
    )
    VALUES
        (N'u-admin-01', N'admin@bookshare.vn', N'ADMIN@BOOKSHARE.VN', N'admin@bookshare.vn', N'ADMIN@BOOKSHARE.VN', 1,
         N'AQAAAAIAAYagAAAAEBookShareDemoHashAdmin123!', N'sec-admin-01', N'con-admin-01', N'0901000001', 1, 0, 1, 0,
         N'Quản Trị Viên Hệ Thống', N'Tòa nhà CMC, Duy Tân, Cầu Giấy, Hà Nội', N'/images/avatars/admin.png', 150, 5),

        (N'u-librarian-01', N'thuthu@cmc.edu.vn', N'THUTHU@CMC.EDU.VN', N'thuthu@cmc.edu.vn', N'THUTHU@CMC.EDU.VN', 1,
         N'AQAAAAIAAYagAAAAEBookShareDemoHashLib123!', N'sec-lib-01', N'con-lib-01', N'0901000002', 1, 0, 1, 0,
         N'ThS. Phạm Thu Hà (Thủ thư)', N'Thư viện Trung tâm ĐH CMC, Hà Nội', N'/images/avatars/librarian.png', 140, 3),

        (N'u-curator-01', N'clbsach@cmc.edu.vn', N'CLBSACH@CMC.EDU.VN', N'clbsach@cmc.edu.vn', N'CLBSACH@CMC.EDU.VN', 1,
         N'AQAAAAIAAYagAAAAEBookShareDemoHashCurator123!', N'sec-cur-01', N'con-cur-01', N'0901000003', 1, 0, 1, 0,
         N'Đỗ Quang Minh (Chủ nhiệm CLB Sách)', N'Trạm Sách Sinh Viên Cầu Giấy, Hà Nội', N'/images/avatars/curator.png', 135, 3),

        (N'u-user-01', N'nguyenhung@cmc.edu.vn', N'NGUYENHUNG@CMC.EDU.VN', N'nguyenhung@cmc.edu.vn', N'NGUYENHUNG@CMC.EDU.VN', 1,
         N'AQAAAAIAAYagAAAAEBookShareDemoHashUser123!', N'sec-usr-01', N'con-usr-01', N'0988111222', 1, 0, 1, 0,
         N'Nguyễn Hùng (BIT250157)', N'Khu đô thị Mỹ Đình, Nam Từ Liêm, Hà Nội', N'/images/avatars/user1.png', 128, 2),

        (N'u-user-02', N'minhanh@cmc.edu.vn', N'MINHANH@CMC.EDU.VN', N'minhanh@cmc.edu.vn', N'MINHANH@CMC.EDU.VN', 1,
         N'AQAAAAIAAYagAAAAEBookShareDemoHashUser123!', N'sec-usr-02', N'con-usr-02', N'0988333444', 1, 0, 1, 0,
         N'Trần Minh Anh', N'Xuân Thủy, Cầu Giấy, Hà Nội', N'/images/avatars/user2.png', 115, 1),

        (N'u-user-03', N'hoangnam@cmc.edu.vn', N'HOANGNAM@CMC.EDU.VN', N'hoangnam@cmc.edu.vn', N'HOANGNAM@CMC.EDU.VN', 1,
         N'AQAAAAIAAYagAAAAEBookShareDemoHashUser123!', N'sec-usr-03', N'con-usr-03', N'0988555666', 1, 0, 1, 0,
         N'Lê Hoàng Nam', N'Nguyễn Trãi, Thanh Xuân, Hà Nội', N'/images/avatars/user3.png', 96, 0);

    INSERT INTO [dbo].[AspNetUserRoles] ([UserId], [RoleId])
    VALUES
        (N'u-admin-01',     N'role-admin'),
        (N'u-librarian-01', N'role-librarian'),
        (N'u-curator-01',   N'role-curator'),
        (N'u-user-01',      N'role-user'),
        (N'u-user-02',      N'role-user'),
        (N'u-user-03',      N'role-user');
END
GO

/* =========================================================================================
   3. NẠP 8 DANH MỤC THỂ LOẠI SÁCH (CATEGORIES)
   ========================================================================================= */
IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories])
BEGIN
    SET IDENTITY_INSERT [dbo].[Categories] ON;
    INSERT INTO [dbo].[Categories] ([Id], [Name], [Slug], [IconClass])
    VALUES
        (1, N'Công nghệ Thông tin & AI',   N'cong-nghe-thong-tin-ai', N'bi-cpu'),
        (2, N'Văn học Việt Nam',           N'van-hoc-viet-nam',       N'bi-Feather'),
        (3, N'Văn học Kinh điển Thế giới', N'van-hoc-the-gioi',       N'bi-globe-americas'),
        (4, N'Kinh tế & Quản trị',         N'kinh-te-quan-tri',       N'bi-graph-up-arrow'),
        (5, N'Kỹ năng & Tư duy',           N'ky-nang-tu-duy',         N'bi-lightbulb'),
        (6, N'Khoa học & Lịch sử Nhân loại',N'khoa-hoc-lich-su',      N'bi-rocket-takeoff'),
        (7, N'Thiết kế UI/UX & Sáng tạo',  N'thiet-ke-sang-tao',      N'bi-palette'),
        (8, N'Ngoại ngữ & Học thuật',      N'ngoai-ngu-hoc-thuat',    N'bi-translate');
    SET IDENTITY_INSERT [dbo].[Categories] OFF;
END
GO

/* =========================================================================================
   4. NẠP 3 THƯ VIỆN KHO CHUNG & ĐIỂM HẸN CLB P2P (LIBRARIES)
   ========================================================================================= */
IF NOT EXISTS (SELECT 1 FROM [dbo].[Libraries])
BEGIN
    SET IDENTITY_INSERT [dbo].[Libraries] ON;
    INSERT INTO [dbo].[Libraries] ([Id], [Name], [Address], [ManagerUserId], [Phone], [HubType], [OpenHours], [IsActive])
    VALUES
        (1, N'Thư viện Trung tâm Trường Đại học CMC',
            N'Tầng 3, Cơ sở Vạn Phúc, Hà Đông / Duy Tân, Cầu Giấy, Hà Nội',
            N'u-librarian-01', N'024-7102-9999', N'Library', N'08:00 - 17:30 (Thứ 2 - Thứ 7)', 1),
        (2, N'Kho sách Học thuật Khoa CNTT & Truyền thông (CMC)',
            N'Phòng 502, Khu Giảng đường CNTT, Cầu Giấy, Hà Nội',
            N'u-librarian-01', N'024-7102-8888', N'Library', N'08:30 - 17:00 (Thứ 2 - Thứ 6)', 1),
        (3, N'Trạm Kiểm định & Giao nhận CLB BookShare Hub Cầu Giấy',
            N'Không gian Sáng tạo Sinh viên - Số 11 Duy Tân, Cầu Giấy, Hà Nội',
            N'u-curator-01', N'0901-000-003', N'ClubStation', N'09:00 - 20:00 (Cả tuần)', 1);
    SET IDENTITY_INSERT [dbo].[Libraries] OFF;
END
GO

/* =========================================================================================
   5. NẠP 30 ĐẦU SÁCH PHONG PHÚ (BOOKS - ID TỪ 1 ĐẾN 30)
   ========================================================================================= */
IF NOT EXISTS (SELECT 1 FROM [dbo].[Books])
BEGIN
    SET IDENTITY_INSERT [dbo].[Books] ON;
    INSERT INTO [dbo].[Books] ([Id], [Title], [Author], [ISBN], [Publisher], [PublishedYear], [PageCount], [CategoryId], [CoverImageUrl], [Summary])
    VALUES
        (1,  N'Clean Code - Mã Sạch và Con Đường Trở Thành Lập Trình Viên Giỏi', N'Robert C. Martin', N'9780132350884', N'NXB Dân Trí', 2022, 464, 1, N'https://covers.openlibrary.org/b/isbn/9780132350884-L.jpg', N'Cẩm nang kinh điển giúp lập trình viên viết mã nguồn dễ đọc, dễ bảo trì và tuân thủ kiến trúc sạch.'),
        (2,  N'Domain-Driven Design - Thiết Kế Hướng Tên Miền', N'Eric Evans', N'9780321125217', N'Addison-Wesley', 2021, 560, 1, N'https://covers.openlibrary.org/b/isbn/9780321125217-L.jpg', N'Phương pháp giải quyết độ phức tạp cốt lõi trong các hệ thống phần mềm doanh nghiệp quy mô lớn.'),
        (3,  N'Designing Data-Intensive Applications', N'Martin Kleppmann', N'9781449373320', N'O''Reilly Media', 2023, 616, 1, N'https://covers.openlibrary.org/b/isbn/9781449373320-L.jpg', N'Kiến trúc cơ sở dữ liệu phân tán, xử lý luồng dữ liệu lớn, đảm bảo tính nhất quán và độ tin cậy.'),
        (4,  N'The Pragmatic Programmer - Lập Trình Viên Thực Dụng', N'Andrew Hunt, David Thomas', N'9780135957059', N'Addison-Wesley', 2020, 352, 1, N'https://covers.openlibrary.org/b/isbn/9780135957059-L.jpg', N'Từ thợ code trở thành bậc thầy kiến tạo phần mềm với tư duy tự động hóa và kiểm thử bền vững.'),
        (5,  N'Refactoring - Cải Tổ Thiết Kế Mã Nguồn Hiện Có', N'Martin Fowler', N'9780134757599', N'Addison-Wesley', 2021, 448, 1, N'https://covers.openlibrary.org/b/isbn/9780134757599-L.jpg', N'Danh mục các kỹ thuật tái cấu trúc mã nguồn chuẩn mực mà không làm thay đổi hành vi bên ngoài.'),
        (6,  N'ASP.NET Core in Action (Third Edition)', N'Andrew Lock', N'9781633438620', N'Manning Publications', 2023, 864, 1, N'https://covers.openlibrary.org/b/isbn/9781633438620-L.jpg', N'Hướng dẫn toàn diện phát triển ứng dụng Web MVC, Razor, Middleware, EF Core và Bảo mật trên .NET.'),
        (7,  N'AI Engineering - Xây Dựng Ứng Dụng Với Foundation Models', N'Chip Huyen', N'9781098166304', N'O''Reilly Media', 2025, 512, 1, N'https://covers.openlibrary.org/b/isbn/9781098166304-L.jpg', N'Kỹ thuật tích hợp LLM, RAG, Prompt Engineering và đánh giá mô hình AI trong sản phẩm thực tế.'),
        (8,  N'Grokking Algorithms - Giải Thuật Minh Họa Trực Quan', N'Aditya Bhargava', N'9781617292231', N'Manning Publications', 2021, 256, 1, N'https://covers.openlibrary.org/b/isbn/9781617292231-L.jpg', N'Học cấu trúc dữ liệu và giải thuật qua hình vẽ minh họa sinh động, dễ hiểu cho sinh viên CNTT.'),
        (9,  N'Hai Con Mèo Ngồi Bên Cửa Sổ', N'Nguyễn Nhật Ánh', N'9786041201118', N'NXB Trẻ', 2022, 212, 2, N'https://images.unsplash.com/photo-1544947950-fa07a98d237f?w=400', N'Câu chuyện đồng thoại ấm áp về tình bạn, lòng bao dung và những bài học nhân văn trong veo.'),
        (10, N'Mắt Biếc', N'Nguyễn Nhật Ánh', N'9786041143319', N'NXB Trẻ', 2020, 300, 2, N'https://images.unsplash.com/photo-1512820790803-83ca734da794?w=400', N'Tác phẩm văn học Việt Nam giàu cảm xúc về tuổi học trò, làng Đo Đo và mối tình đơn phương sâu lắng.'),
        (11, N'Cho Tôi Xin Một Vé Đi Tuổi Thơ', N'Nguyễn Nhật Ánh', N'9786041189218', N'NXB Trẻ', 2021, 208, 2, N'https://images.unsplash.com/photo-1495446815901-a7297e633e8d?w=400', N'Chuyến tàu ngược thời gian trở về thế giới tuổi thơ đầy ắp trí tưởng tượng của cu Mùi và nhóm bạn.'),
        (12, N'Cánh Đồng Bất Tận', N'Nguyễn Ngọc Tư', N'9786041152113', N'NXB Trẻ', 2020, 224, 2, N'https://images.unsplash.com/photo-1532012197267-da84d127e765?w=400', N'Tập truyện ngắn khắc họa vẻ đẹp mộc mạc và số phận con người vùng sông nước miền Tây Nam Bộ.'),
        (13, N'Dế Mèn Phiêu Lưu Ký', N'Tô Hoài', N'9786042214568', N'NXB Kim Đồng', 2022, 168, 2, N'https://images.unsplash.com/photo-1476275466078-4007374efbbe?w=400', N'Hành trình trưởng thành và khát vọng hòa bình muôn loài của chú Dế Mèn qua ngòi bút Tô Hoài.'),
        (14, N'Nhà Giả Kim (The Alchemist)', N'Paulo Coelho', N'9780062315007', N'NXB Hội Nhà Văn', 2021, 228, 3, N'https://covers.openlibrary.org/b/isbn/9780062315007-L.jpg', N'Hành trình theo đuổi vận mệnh của chàng chăn cừu Santiago băng qua sa mạc Ai Cập.'),
        (15, N'Hoàng Tử Bé (Le Petit Prince)', N'Antoine de Saint-Exupéry', N'9780156012195', N'NXB Kim Đồng', 2022, 112, 3, N'https://covers.openlibrary.org/b/isbn/9780156012195-L.jpg', N'Điều cốt lõi vô hình trong mắt trần, người ta chỉ nhìn thấy thật rõ bằng trái tim.'),
        (16, N'Cây Cam Ngọt Của Tôi', N'José Mauro de Vasconcelos', N'9786043140651', N'NXB Hội Nhà Văn', 2021, 244, 3, N'https://images.unsplash.com/photo-1543002588-bfa74002ed7e?w=400', N'Câu chuyện chạm đến trái tim về cậu bé Zezé nhạy cảm và người bạn cây cam ngọt Minguinho.'),
        (17, N'Không Gia Đình (Sans Famille)', N'Hector Malot', N'9786041167896', N'NXB Văn Học', 2020, 580, 3, N'https://images.unsplash.com/photo-1519682337058-a94d519337bc?w=400', N'Cuộc hành trình thiên lý đầy nghị lực của cậu bé Rémi cùng gánh xiếc cụ Vitalis.'),
        (18, N'Tư Duy Nhanh Và Chậm (Thinking, Fast and Slow)', N'Daniel Kahneman', N'9780374533557', N'NXB Thế Giới', 2022, 612, 4, N'https://covers.openlibrary.org/b/isbn/9780374533557-L.jpg', N'Khám phá hai hệ thống tư duy chi phối cách con người đưa ra quyết định kinh tế và đời sống.'),
        (19, N'Khởi Nghiệp Tinh Gọn (The Lean Startup)', N'Eric Ries', N'9780307887894', N'NXB Trẻ', 2021, 336, 4, N'https://covers.openlibrary.org/b/isbn/9780307887894-L.jpg', N'Vòng lặp Xây dựng - Đo lường - Học hỏi giúp các dự án công nghệ đổi mới sáng tạo thành công.'),
        (20, N'Từ Tốt Đến Vĩ Đại (Good to Great)', N'Jim Collins', N'9780066620992', N'NXB Trẻ', 2021, 380, 4, N'https://covers.openlibrary.org/b/isbn/9780066620992-L.jpg', N'Nghiên cứu thực chứng về những yếu tố kỷ luật giúp doanh nghiệp bứt phá bền vững.'),
        (21, N'Atomic Habits - Thay Đổi Tí Hon, Hiệu Quả Bất Ngờ', N'James Clear', N'9780735211292', N'NXB Phương Đông', 2023, 320, 5, N'https://covers.openlibrary.org/b/isbn/9780735211292-L.jpg', N'Hệ thống 4 quy luật xây dựng thói quen tốt mỗi ngày tốt hơn 1% để tạo ra thành tựu lớn.'),
        (22, N'Deep Work - Làm Ra Làm, Chơi Ra Chơi', N'Cal Newport', N'9781455586691', N'NXB Lao Động', 2022, 296, 5, N'https://covers.openlibrary.org/b/isbn/9781455586691-L.jpg', N'Nghệ thuật tập trung sâu không xao nhãng trong kỷ nguyên số để làm chủ kiến thức phức tạp.'),
        (23, N'Đắc Nhân Tâm (How to Win Friends and Influence People)', N'Dale Carnegie', N'9780671027032', N'NXB Tổng Hợp TP.HCM', 2021, 320, 5, N'https://covers.openlibrary.org/b/isbn/9780671027032-L.jpg', N'Nghệ thuật giao tiếp, lắng nghe chân thành và khích lệ tiềm năng của những người xung quanh.'),
        (24, N'Sapiens - Lược Sử Loài Người', N'Yuval Noah Harari', N'9780062316097', N'NXB Tri Thức', 2022, 560, 6, N'https://covers.openlibrary.org/b/isbn/9780062316097-L.jpg', N'Bức tranh toàn cảnh về Cách mạng Nhận thức, Nông nghiệp và Khoa học định hình nhân loại.'),
        (25, N'Vũ Trụ Trong Vỏ Hạt Dẻ', N'Stephen Hawking', N'9780553802023', N'NXB Trẻ', 2021, 224, 6, N'https://covers.openlibrary.org/b/isbn/9780553802023-L.jpg', N'Khám phá thuyết tương đối, cơ học lượng tử, lỗ đen và cấu trúc không-thời gian.'),
        (26, N'Gen - Lịch Sử Và Tương Lai Của Nhân Loại', N'Siddhartha Mukherjee', N'9781476733500', N'NXB Dân Trí', 2022, 608, 6, N'https://covers.openlibrary.org/b/isbn/9781476733500-L.jpg', N'Hành trình giải mã di truyền học từ vườn đậu của Mendel đến công nghệ chỉnh sửa gen hiện đại.'),
        (27, N'Don''t Make Me Think - Đừng Bắt Tôi Phải Nghĩ', N'Steve Krug', N'9780321965516', N'New Riders', 2021, 216, 7, N'https://covers.openlibrary.org/b/isbn/9780321965516-L.jpg', N'Nguyên tắc vàng về khả năng sử dụng (Usability) và thiết kế giao diện Web trực quan cho người dùng.'),
        (28, N'The Design of Everyday Things', N'Don Norman', N'9780465050659', N'Basic Books', 2021, 368, 7, N'https://covers.openlibrary.org/b/isbn/9780465050659-L.jpg', N'Tâm lý học thiết kế lấy con người làm trung tâm: Affordances, Signifiers và Feedback.'),
        (29, N'English Collocations in Use Advanced', N'Felicity O''Dell, Michael McCarthy', N'9781316629758', N'Cambridge University Press', 2022, 192, 8, N'https://covers.openlibrary.org/b/isbn/9781316629758-L.jpg', N'Tài liệu luyện cụm từ tiếng Anh học thuật nâng cao dành cho sinh viên đại học và nghiên cứu.'),
        (30, N'Phương Pháp Đọc Sách Hiệu Quả (How to Read a Book)', N'Mortimer J. Adler', N'9780671212094', N'NXB Lao Động', 2022, 426, 8, N'https://covers.openlibrary.org/b/isbn/9780671212094-L.jpg', N'Bốn cấp độ đọc sách từ Đọc sơ cấp, Đọc kiểm tra, Đọc phân tích đến Đọc tổng hợp chuyên sâu.');
    SET IDENTITY_INSERT [dbo].[Books] OFF;
END
GO

/* =========================================================================================
   6. NẠP 50 BẢN SAO SÁCH VẬT LÝ (BOOKCOPIES - ID TỪ 1 ĐẾN 50)
   - Bản sao 1 -> 32: Thuộc Kho chung Thư viện / Trạm CLB (OwnerType = 'System')
   - Bản sao 33 -> 50: Thuộc Tủ sách cá nhân P2P (OwnerType = 'User')
   ========================================================================================= */
IF NOT EXISTS (SELECT 1 FROM [dbo].[BookCopies])
BEGIN
    SET IDENTITY_INSERT [dbo].[BookCopies] ON;
    INSERT INTO [dbo].[BookCopies] ([Id], [BookId], [Barcode], [OwnerType], [LibraryId], [OwnerUserId], [ShelfLocation], [Condition], [Status], [IsLendable])
    VALUES
        -- 32 Bản sao Kho chung (System)
        (1,  1,  N'BS-SYS-0001', N'System', 1, NULL, N'Kệ CNTT-A1', N'New',  N'Available', 1),
        (2,  1,  N'BS-SYS-0002', N'System', 2, NULL, N'Kệ CNTT-B1', N'Good', N'Borrowed',  1),
        (3,  2,  N'BS-SYS-0003', N'System', 1, NULL, N'Kệ CNTT-A1', N'New',  N'Available', 1),
        (4,  3,  N'BS-SYS-0004', N'System', 2, NULL, N'Kệ CNTT-B2', N'New',  N'Borrowed',  1),
        (5,  4,  N'BS-SYS-0005', N'System', 1, NULL, N'Kệ CNTT-A2', N'Good', N'Available', 1),
        (6,  5,  N'BS-SYS-0006', N'System', 2, NULL, N'Kệ CNTT-B2', N'Good', N'Available', 1),
        (7,  6,  N'BS-SYS-0007', N'System', 1, NULL, N'Kệ CNTT-A3', N'New',  N'Available', 1),
        (8,  6,  N'BS-SYS-0008', N'System', 2, NULL, N'Kệ CNTT-B3', N'New',  N'Available', 1),
        (9,  7,  N'BS-SYS-0009', N'System', 1, NULL, N'Kệ AI-A1',   N'New',  N'Reserved',  1),
        (10, 8,  N'BS-SYS-0010', N'System', 2, NULL, N'Kệ CNTT-B4', N'Good', N'Available', 1),
        (11, 9,  N'BS-SYS-0011', N'System', 1, NULL, N'Kệ VHVN-C1', N'New',  N'Available', 1),
        (12, 10, N'BS-SYS-0012', N'System', 1, NULL, N'Kệ VHVN-C1', N'Good', N'Available', 1),
        (13, 11, N'BS-SYS-0013', N'System', 3, NULL, N'Trạm CLB-K1',N'Good', N'Available', 1),
        (14, 12, N'BS-SYS-0014', N'System', 1, NULL, N'Kệ VHVN-C2', N'Good', N'Available', 1),
        (15, 13, N'BS-SYS-0015', N'System', 3, NULL, N'Trạm CLB-K1',N'New',  N'Available', 1),
        (16, 14, N'BS-SYS-0016', N'System', 1, NULL, N'Kệ VHTG-D1', N'Good', N'Available', 1),
        (17, 15, N'BS-SYS-0017', N'System', 1, NULL, N'Kệ VHTG-D1', N'New',  N'Available', 1),
        (18, 16, N'BS-SYS-0018', N'System', 3, NULL, N'Trạm CLB-K2',N'Good', N'Available', 1),
        (19, 17, N'BS-SYS-0019', N'System', 1, NULL, N'Kệ VHTG-D2', N'Fair', N'Available', 1),
        (20, 18, N'BS-SYS-0020', N'System', 1, NULL, N'Kệ KT-E1',   N'New',  N'Available', 1),
        (21, 19, N'BS-SYS-0021', N'System', 1, NULL, N'Kệ KT-E1',   N'Good', N'Available', 1),
        (22, 20, N'BS-SYS-0022', N'System', 1, NULL, N'Kệ KT-E2',   N'Good', N'Available', 1),
        (23, 21, N'BS-SYS-0023', N'System', 1, NULL, N'Kệ KN-F1',   N'New',  N'Available', 1),
        (24, 22, N'BS-SYS-0024', N'System', 2, NULL, N'Kệ KN-F1',   N'Good', N'Available', 1),
        (25, 23, N'BS-SYS-0025', N'System', 3, NULL, N'Trạm CLB-K2',N'Good', N'Available', 1),
        (26, 24, N'BS-SYS-0026', N'System', 1, NULL, N'Kệ KH-G1',   N'New',  N'Available', 1),
        (27, 25, N'BS-SYS-0027', N'System', 1, NULL, N'Kệ KH-G1',   N'Good', N'Available', 1),
        (28, 26, N'BS-SYS-0028', N'System', 1, NULL, N'Kệ KH-G2',   N'New',  N'Available', 1),
        (29, 27, N'BS-SYS-0029', N'System', 2, NULL, N'Kệ UIUX-H1', N'New',  N'Available', 1),
        (30, 28, N'BS-SYS-0030', N'System', 2, NULL, N'Kệ UIUX-H1', N'Good', N'Available', 1),
        (31, 29, N'BS-SYS-0031', N'System', 1, NULL, N'Kệ NN-I1',   N'New',  N'Available', 1),
        (32, 30, N'BS-SYS-0032', N'System', 1, NULL, N'Kệ NN-I2',   N'Good', N'Available', 1),

        -- 18 Bản sao Tủ sách cá nhân P2P (User: u-user-01, u-user-02, u-user-03, u-curator-01)
        (33, 1,  N'BS-P2P-0033', N'User', 3, N'u-user-01', N'Tủ sách Nguyễn Hùng', N'New',  N'Available', 1),
        (34, 6,  N'BS-P2P-0034', N'User', 3, N'u-user-01', N'Tủ sách Nguyễn Hùng', N'New',  N'Available', 0), -- Đang đọc, khóa cho mượn
        (35, 9,  N'BS-P2P-0035', N'User', 3, N'u-user-01', N'Tủ sách Nguyễn Hùng', N'Good', N'Available', 1),
        (36, 14, N'BS-P2P-0036', N'User', 3, N'u-user-01', N'Tủ sách Nguyễn Hùng', N'Good', N'Borrowed',  1),
        (37, 21, N'BS-P2P-0037', N'User', 3, N'u-user-01', N'Tủ sách Nguyễn Hùng', N'New',  N'Available', 1),
        (38, 27, N'BS-P2P-0038', N'User', 3, N'u-user-01', N'Tủ sách Nguyễn Hùng', N'Good', N'Available', 1),
        (39, 2,  N'BS-P2P-0039', N'User', 3, N'u-user-02', N'Tủ sách Minh Anh',    N'Good', N'Available', 1),
        (40, 10, N'BS-P2P-0040', N'User', 3, N'u-user-02', N'Tủ sách Minh Anh',    N'New',  N'Available', 1),
        (41, 16, N'BS-P2P-0041', N'User', 3, N'u-user-02', N'Tủ sách Minh Anh',    N'Good', N'Available', 1),
        (42, 18, N'BS-P2P-0042', N'User', 3, N'u-user-02', N'Tủ sách Minh Anh',    N'New',  N'Available', 1),
        (43, 22, N'BS-P2P-0043', N'User', 3, N'u-user-02', N'Tủ sách Minh Anh',    N'Good', N'Available', 1),
        (44, 24, N'BS-P2P-0044', N'User', 3, N'u-user-02', N'Tủ sách Minh Anh',    N'Good', N'Available', 0),
        (45, 4,  N'BS-P2P-0045', N'User', 3, N'u-user-03', N'Tủ sách Hoàng Nam',   N'Fair', N'Available', 1),
        (46, 8,  N'BS-P2P-0046', N'User', 3, N'u-user-03', N'Tủ sách Hoàng Nam',   N'Good', N'Available', 1),
        (47, 19, N'BS-P2P-0047', N'User', 3, N'u-user-03', N'Tủ sách Hoàng Nam',   N'Good', N'Available', 1),
        (48, 25, N'BS-P2P-0048', N'User', 3, N'u-user-03', N'Tủ sách Hoàng Nam',   N'Good', N'Available', 1),
        (49, 7,  N'BS-P2P-0049', N'User', 3, N'u-curator-01', N'Tủ CLB BookShare', N'New',  N'Available', 1),
        (50, 30, N'BS-P2P-0050', N'User', 3, N'u-curator-01', N'Tủ CLB BookShare', N'New',  N'Available', 1);
    SET IDENTITY_INSERT [dbo].[BookCopies] OFF;
END
GO

/* =========================================================================================
   7. NẠP KỆ SÁCH CÁ NHÂN (SHELVES & SHELFITEMS - DỊCH VỤ S02)
   ========================================================================================= */
IF NOT EXISTS (SELECT 1 FROM [dbo].[Shelves])
BEGIN
    SET IDENTITY_INSERT [dbo].[Shelves] ON;
    INSERT INTO [dbo].[Shelves] ([Id], [UserId], [Name], [Description], [IsPublic], [DisplayOrder])
    VALUES
        (1, N'u-user-01', N'Đang đọc học kỳ này',      N'Sách chuyên ngành .NET 10, Kiến trúc phần mềm & UI/UX đang theo dõi mỗi ngày', 1, 1),
        (2, N'u-user-01', N'Kệ sách sẵn sàng cho mượn P2P', N'Sách giấy mình đã đọc xong, sẵn sàng giao lưu tại Trạm CLB Cầu Giấy', 1, 2),
        (3, N'u-user-01', N'Tâm đắc & Yêu thích nhất', N'Những cuốn sách gối đầu giường đọc đi đọc lại nhiều lần', 1, 3),
        (4, N'u-user-02', N'Tủ sách Văn học & Kinh tế', N'Kệ sách chia sẻ cộng đồng của Minh Anh', 1, 1);
    SET IDENTITY_INSERT [dbo].[Shelves] OFF;

    INSERT INTO [dbo].[ShelfItems] ([ShelfId], [BookCopyId])
    VALUES
        (1, 34), (1, 38),
        (2, 33), (2, 35), (2, 36), (2, 37),
        (3, 33), (3, 35),
        (4, 39), (4, 40), (4, 41), (4, 42);
END
GO

/* =========================================================================================
   8. NẠP TIẾN ĐỘ ĐỌC SÁCH CÁ NHÂN & GHI CHÚ TRANG SÁCH - KẾ THỪA VER07 (S03 & S04)
   ========================================================================================= */
IF NOT EXISTS (SELECT 1 FROM [dbo].[ReadingProgresses])
BEGIN
    SET IDENTITY_INSERT [dbo].[ReadingProgresses] ON;
    INSERT INTO [dbo].[ReadingProgresses] (
        [Id], [UserId], [BookId], [CurrentPage], [TotalPages], [Priority],
        [TargetDeadline], [Status], [StartedAt], [FinishedAt], [PersonalRating]
    )
    VALUES
        (1, N'u-user-01', 6,  540, 864, N'High',   '2026-11-30', N'Reading',    DATEADD(DAY, -14, SYSUTCDATETIME()), NULL, 5),
        (2, N'u-user-01', 1,  464, 464, N'High',   '2026-10-25', N'Completed',  DATEADD(DAY, -40, SYSUTCDATETIME()), DATEADD(DAY, -18, SYSUTCDATETIME()), 5),
        (3, N'u-user-01', 9,  212, 212, N'Medium', '2026-10-15', N'Completed',  DATEADD(DAY, -50, SYSUTCDATETIME()), DATEADD(DAY, -30, SYSUTCDATETIME()), 5),
        (4, N'u-user-01', 21, 320, 320, N'Medium', '2026-11-05', N'Completed',  DATEADD(DAY, -25, SYSUTCDATETIME()), DATEADD(DAY, -8,  SYSUTCDATETIME()), 5),
        (5, N'u-user-01', 27, 135, 216, N'High',   '2026-11-22', N'Reading',    DATEADD(DAY, -7,  SYSUTCDATETIME()), NULL, 4),
        (6, N'u-user-01', 7,  0,   512, N'High',   '2026-12-15', N'WantToRead', NULL, NULL, NULL),
        (7, N'u-user-02', 18, 280, 612, N'Medium', '2026-12-01', N'Reading',    DATEADD(DAY, -10, SYSUTCDATETIME()), NULL, 5);
    SET IDENTITY_INSERT [dbo].[ReadingProgresses] OFF;

    INSERT INTO [dbo].[BookNotes] ([ReadingProgressId], [PageNumber], [QuoteText], [NoteContent], [IsPublic])
    VALUES
        (1, 142, N'Controllers should be thin; business logic belongs in encapsulated application services.', N'Áp dụng ngay nguyên tắc này cho đồ án BookShare: Controller không bao giờ gọi thẳng DbContext!', 1),
        (1, 388, N'Use ExternalLoginSignInAsync with Identity to seamlessly link OAuth providers.', N'Ghi chú cấu hình Google OAuth 2.0 nối với bảng AspNetUserLogins trong dịch vụ S01.', 1),
        (2, 54,  N'Clean code always looks like it was written by someone who cares.', N'Tách 22 dịch vụ ra 22 bộ file .cshtml + .css + .js độc lập giúp code cực kỳ sạch và dễ bảo trì.', 1),
        (3, 88,  N'Tình bạn chân thành xóa nhòa mọi khoảng cách giữa hai thế giới khác biệt.', N'Cuốn sách nhẹ nhàng giúp thư giãn sau những giờ code C# căng thẳng.', 1);
END
GO

/* =========================================================================================
   9. NẠP GÓI THÀNH VIÊN (MEMBERSHIPS - S16)
   ========================================================================================= */
IF NOT EXISTS (SELECT 1 FROM [dbo].[Memberships])
BEGIN
    INSERT INTO [dbo].[Memberships] ([UserId], [Tier], [MaxConcurrentLoans], [StartDate], [ExpiryDate], [IsActive])
    VALUES
        (N'u-user-01', N'Premium', 5, DATEADD(MONTH, -1, SYSUTCDATETIME()), DATEADD(MONTH, 11, SYSUTCDATETIME()), 1),
        (N'u-user-02', N'Premium', 5, DATEADD(MONTH, -2, SYSUTCDATETIME()), DATEADD(MONTH, 10, SYSUTCDATETIME()), 1),
        (N'u-user-03', N'Free',    2, DATEADD(MONTH, -3, SYSUTCDATETIME()), NULL, 1);
END
GO

/* =========================================================================================
   10. NẠP ĐƠN MƯỢN, HỢP ĐỒNG MƯỢN, BIÊN BẢN ẢNH CLOUDINARY & TRANH CHẤP (S08, S09, S10, S19)
   ========================================================================================= */
IF NOT EXISTS (SELECT 1 FROM [dbo].[LoanRequests])
BEGIN
    SET IDENTITY_INSERT [dbo].[LoanRequests] ON;
    INSERT INTO [dbo].[LoanRequests] (
        [Id], [BookCopyId], [BorrowerId], [ApproverId], [LoanType],
        [Status], [RequestedAt], [DesiredDays], [PickupHubId], [RequestNote]
    )
    VALUES
        -- Đơn 1: Nguyễn Hùng mượn Kho chung (Clean Code bản sao #2)
        (1, 2,  N'u-user-01', N'u-librarian-01', N'Library', N'Approved', DATEADD(DAY, -10, SYSUTCDATETIME()), 14, 2, N'Em xin mượn phục vụ đồ án môn Phát triển Ứng dụng Web ạ.'),
        -- Đơn 2: Minh Anh mượn P2P từ Nguyễn Hùng (Nhà Giả Kim bản sao #36 tại Trạm CLB Cầu Giấy)
        (2, 36, N'u-user-02', N'u-user-01',      N'P2P',     N'Approved', DATEADD(DAY, -6,  SYSUTCDATETIME()), 10, 3, N'Mình hẹn nhận sách tại Trạm CLB BookShare Duy Tân lúc 14h nhé!'),
        -- Đơn 3: Hoàng Nam mượn Kho chung (Designing Data-Intensive Apps bản sao #4 - Đang trễ hạn)
        (3, 4,  N'u-user-03', N'u-librarian-01', N'Library', N'Approved', DATEADD(DAY, -20, SYSUTCDATETIME()), 14, 2, N'Xin mượn tài liệu tham khảo hệ phân tán.'),
        -- Đơn 4: Minh Anh đang gửi yêu cầu chờ duyệt P2P cuốn Clean Code (#33) của Nguyễn Hùng
        (4, 33, N'u-user-02', N'u-user-01',      N'P2P',     N'Pending',  DATEADD(HOUR, -3, SYSUTCDATETIME()), 14, 3, N'Chào Hùng, cho mình mượn cuốn Clean Code 2 tuần nhé!');
    SET IDENTITY_INSERT [dbo].[LoanRequests] OFF;

    SET IDENTITY_INSERT [dbo].[Loans] ON;
    INSERT INTO [dbo].[Loans] ([Id], [LoanRequestId], [StartDate], [DueDate], [ReturnDate], [RenewCount], [FineAmount], [Status])
    VALUES
        (1, 1, DATEADD(DAY, -9,  SYSUTCDATETIME()), DATEADD(DAY, 5,  SYSUTCDATETIME()), NULL, 0, 0,     N'Active'),
        (2, 2, DATEADD(DAY, -5,  SYSUTCDATETIME()), DATEADD(DAY, 5,  SYSUTCDATETIME()), NULL, 1, 0,     N'Active'),
        (3, 3, DATEADD(DAY, -19, SYSUTCDATETIME()), DATEADD(DAY, -5, SYSUTCDATETIME()), NULL, 0, 25000, N'Overdue');
    SET IDENTITY_INSERT [dbo].[Loans] OFF;

    -- Biên bản ảnh tình trạng sách lúc giao (ConditionReports - S10)
    INSERT INTO [dbo].[ConditionReports] ([LoanId], [ReportType], [ReportedByUserId], [ConditionGrade], [Note], [ImageUrl])
    VALUES
        (1, N'Handover', N'u-librarian-01', N'Good', N'Bìa cứng nguyên vẹn, không gấp trang, mã vạch BS-SYS-0002 rõ nét.', N'https://images.unsplash.com/photo-1544716278-ca5e3f4abd8c?w=600'),
        (2, N'Handover', N'u-user-01',      N'Good', N'Đã bọc plastic cẩn thận, giao sách trực tiếp tại Trạm CLB Cầu Giấy.', N'https://images.unsplash.com/photo-1512820790803-83ca734da794?w=600'),
        (3, N'Handover', N'u-librarian-01', N'New',  N'Sách mới 100% xuất kho Khoa CNTT.', N'https://images.unsplash.com/photo-1532012197267-da84d127e765?w=600');

    -- Hàng chờ sách Hot (Reservations - S11: Cuốn AI Engineering #7)
    INSERT INTO [dbo].[Reservations] ([BookId], [UserId], [QueuePosition], [Status], [NotifiedAt], [ExpiresAt])
    VALUES
        (7, N'u-user-01', 1, N'Notified', SYSUTCDATETIME(), DATEADD(HOUR, 48, SYSUTCDATETIME())),
        (7, N'u-user-02', 2, N'Waiting',  NULL, NULL);
END
GO

/* =========================================================================================
   11. NẠP GIAO DỊCH SANDBOX, ĐÁNH GIÁ UY TÍN, THÔNG BÁO & LỊCH SỬ CHAT AI GEMINI
   ========================================================================================= */
IF NOT EXISTS (SELECT 1 FROM [dbo].[Transactions])
BEGIN
    INSERT INTO [dbo].[Transactions] ([UserId], [LoanId], [TransactionType], [Amount], [PaymentGateway], [PaymentStatus], [HmacSignature])
    VALUES
        (N'u-user-01', NULL, N'Membership',       99000, N'VNPaySandbox', N'Completed', N'hmac_sha512_demo_vnpay_sig_001'),
        (N'u-user-02', 2,    N'SimulatedDeposit', 50000, N'MoMoSandbox',  N'Completed', N'hmac_sha512_demo_momo_sig_002');
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Disputes])
BEGIN
    INSERT INTO [dbo].[Disputes] ([LoanId], [RaisedByUserId], [Reason], [EvidenceUrlsJson], [Status], [ResolvedByAdminId], [ResolutionNote])
    VALUES
        (3, N'u-librarian-01', N'Người mượn quá hạn 5 ngày chưa hoàn trả cuốn Designing Data-Intensive Applications (BS-SYS-0004).', N'["https://images.unsplash.com/photo-1532012197267-da84d127e765?w=600"]', N'UnderReview', N'u-admin-01', N'Đã gửi thông báo nhắc hạn lần 2 qua Resend Email và tạm khóa quyền mượn mới.');
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Reviews])
BEGIN
    INSERT INTO [dbo].[Reviews] ([ReviewerId], [TargetType], [TargetId], [LoanId], [Rating], [Comment])
    VALUES
        (N'u-user-02', N'User', N'u-user-01', 2, 5, N'Chủ sách cực kỳ nhiệt tình, sách bọc bìa rất mới và đúng hẹn tại Trạm CLB Cầu Giấy!'),
        (N'u-user-01', N'Book', N'1',         1, 5, N'Cuốn sách gối đầu giường cho mọi sinh viên CNTT khi làm đồ án kiến trúc phần mềm.');
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Notifications])
BEGIN
    INSERT INTO [dbo].[Notifications] ([UserId], [Title], [Content], [Type], [IsRead], [EmailSentViaResend])
    VALUES
        (N'u-user-01', N'Yêu cầu mượn P2P mới từ Trần Minh Anh', N'Minh Anh vừa gửi yêu cầu mượn cuốn Clean Code (BS-P2P-0033) nhận tại Trạm CLB Cầu Giấy.', N'LoanApproved', 0, 1),
        (N'u-user-01', N'Sách Hot đặt trước đã có sẵn!', N'Cuốn "AI Engineering" bạn xếp hàng số #1 đã sẵn sàng tại Thư viện ĐH CMC. Giữ chỗ trong 48 giờ.', N'QueueAvailable', 0, 1),
        (N'u-user-03', N'Nhắc hạn trả sách quá hạn (5 ngày)', N'Cuốn "Designing Data-Intensive Applications" đã quá hạn 5 ngày. Phí trễ tạm tính: 25.000đ.', N'DueReminder', 0, 1);
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[AiChatSessions])
BEGIN
    SET IDENTITY_INSERT [dbo].[AiChatSessions] ON;
    INSERT INTO [dbo].[AiChatSessions] ([Id], [UserId], [Title], [ModelName])
    VALUES
        (1, N'u-user-01', N'Tư vấn tài liệu ASP.NET Core 10 & Kiểm tra hạn trả sách', N'gemini-2.5-flash');
    SET IDENTITY_INSERT [dbo].[AiChatSessions] OFF;

    INSERT INTO [dbo].[AiChatMessages] ([SessionId], [UserId], [Sender], [MessageText], [PromptTokens], [ReferencedBookIdsJson])
    VALUES
        (1, N'u-user-01', N'User',   N'Chào Trợ lý AI, mình đang làm đồ án ASP.NET Core MVC theo kiến trúc sạch thì nên mượn cuốn nào trong kho trường ĐH CMC, và kiểm tra giúp mình hạn trả sách hiện tại nhé!', 42, NULL),
        (1, N'u-user-01', N'Gemini', N'Chào Nguyễn Hùng! Dựa trên dữ liệu Kho sách ĐH CMC và tủ sách của bạn:\n1. Gợi ý mượn ngay: "Domain-Driven Design" (Mã bản sao: BS-SYS-0003 - Đang sẵn sàng tại Kệ CNTT-A1) và "Refactoring" (BS-SYS-0006).\n2. Tiến độ đọc: Bạn đang đọc "ASP.NET Core in Action" tới trang 540/864 (62.5%).\n3. Hạn trả sách: Cuốn "Clean Code" (BS-SYS-0002) bạn mượn còn 5 ngày nữa đến hạn trả, hiện không có phí trễ hạn.', 185, N'[1,2,5,6]');
END
GO

PRINT N'=== ĐÃ NẠP THÀNH CÔNG DỮ LIỆU MẪU (4 ROLES, 30 ĐẦU SÁCH, 50 BẢN SAO) CHO BOOKSHAREDB ===';
GO
