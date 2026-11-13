# BẢNG THEO DÕI TIẾN ĐỘ DỰ ÁN BOOKSHARE (PROGRESS.md)

Dự án **BookShare (.NET 10 MVC)** được chia thành **5 Giai đoạn (Phases)** phát triển tuần tự để kiểm soát chất lượng kiến trúc, dữ liệu và 22 bộ giao diện dịch vụ độc lập.

---

## Giai đoạn 1: Khóa Kiến trúc, Thiết kế CSDL 22 Bảng & Duyệt Mẫu Giao diện (Hoàn thành 100%)
- [x] Khởi tạo cấu trúc thư mục gốc `BookShare.Solution` và trích xuất tài liệu tham chiếu `_reference/book-tracker-ver07`.
- [x] Tạo tệp `.gitignore`, `.env.example` và `.env` chuẩn cấu hình `.NET 10` (`__` double underscore).
- [x] Thiết kế tài liệu kiến trúc tổng thể `docs/ARCHITECTURE.md` (Sơ đồ cây thư mục, ma trận 4 Role, quy hoạch 22 dịch vụ S01 $\rightarrow$ S22).
- [x] Thiết kế bộ quy chuẩn kỹ thuật `docs/RULES.md` (Quy tắc không bán sách, Database-First, Service Layer, Google OAuth, Resend OTP, Gemini AI RAG, CSRF & IDOR).
- [x] Thiết kế bảng theo dõi tiến độ `docs/PROGRESS.md` (5 giai đoạn).
- [x] Xây dựng script `database/01_BookShare_Schema.sql` (Đầy đủ 22 bảng nghiệp vụ cốt lõi + 3 bảng Identity phụ trợ SQL Server, PK, FK, CHECK, INDEX).
- [x] Xây dựng script `database/02_BookShare_SeedData.sql` (Dữ liệu mẫu: 4 Roles, 6 tài khoản mẫu, 3 Thư viện/CLB, 8 Danh mục, 30 Đầu sách, 50 Bản sao `BookCopies`, kệ sách & tiến độ đọc mẫu).
- [x] Xây dựng 4 Mẫu Giao diện Tương tác (`docs/ui-previews/`) và đã được kiểm duyệt.

---

## Giai đoạn 2: Khởi tạo Project .NET 10, Chạy Scaffold, Khung Core & 3 Dịch vụ Đầu tiên S01–S03 (Hoàn thành 100%)
- [x] Khởi tạo Solution `BookShare.sln` và Project `src/BookShare.Web/BookShare.Web.csproj` (.NET 10 MVC).
- [x] Cài đặt các gói NuGet cốt lõi:
  - `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Tools` & `Microsoft.EntityFrameworkCore.Design` (`10.0.5`)
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (`10.0.5`)
  - `Microsoft.AspNetCore.Authentication.Google` (`10.0.5`)
  - `DotNetEnv` (`3.1.1`)
  - `Hangfire.AspNetCore` & `Hangfire.SqlServer` (`1.8.25`)
- [x] Thực thi script `01_BookShare_Schema.sql` & `02_BookShare_SeedData.sql` vào SQL Server (`(localdb)\MSSQLLocalDB` & `localhost`).
- [x] Thực thi lệnh `dotnet ef dbcontext scaffold` sinh tự động toàn bộ class Entities vào `Models/Entities/` và `BookShareDbContext` vào `Data/`.
- [x] Thiết lập `Program.cs`: `DotNetEnv.Env.TraversePath().Load()`, cấu hình Cookie Auth + 4 Roles (`Admin`, `Librarian`, `Curator`, `User`), Google OAuth, DI cho Services, Hangfire, `MapStaticAssets()` và Areas (`Admin`, `Librarian`, `Curator`).
- [x] Xây dựng bộ khung giao diện dùng chung:
  - `wwwroot/css/core-theme.css` (Kế thừa Font Roboto, đa bảng màu Portal, `.status-banner` Mã 200/300/400/404/500, Portal Header không bán sách).
  - `wwwroot/js/core-api.js` (Hàm `apiFetch` bọc CSRF Token, tự động cập nhật `#systemStatusBox` và điều khiển Widget AI).
  - `Views/Shared/_Layout.cshtml` & `Views/Home/Index.cshtml`.
- [x] **Hoàn thiện 100% 3 dịch vụ mẫu đầu tiên (Chuẩn tách 3 file `.cshtml`, `.css`, `.js` riêng):**
  - [x] **S01 - `AuthOtpGoogle`**: Đăng ký, Đăng nhập Cookie Auth, chọn nhanh 4 tài khoản mẫu 4 Roles, gửi & xác thực OTP 6 số qua `IResendEmailService` (băm SHA-256 lưu DB + hiển thị mã Dev OTP trực tiếp trên UI/Console khi chưa điền Resend Key thật), và đăng nhập Google OAuth.
  - [x] **S02 - `PersonalShelf`**: Quản lý tủ sách cá nhân, chia kệ tùy chỉnh, thêm bản sao sách mới vào kệ, nút gạt bật/tắt trạng thái cho phép mượn P2P (`IsLendable`) có kiểm tra chống IDOR (`OwnerUserId == currentUserId`).
  - [x] **S03 - `ReadingTracker`**: Kế thừa và nâng cấp `book-tracker-ver07` — cập nhật số trang đang đọc bằng thanh trượt (`CurrentPage / TotalPages`), tự động tính `%` hoàn thành, đặt mục tiêu đọc sách năm, biểu đồ tròn & biểu đồ cột `Chart.js`, xuất tệp sao lưu JSON.
- [x] Kiểm tra `dotnet build` đạt **0 Error**.

---

## Giai đoạn 3: Phát triển Nhóm Dịch vụ Ghi chú, Tự điền ISBN, Tìm kiếm & Kho chung (S04 $\rightarrow$ S07) (Hoàn thành 100%)
- [x] **S04 - `BookNotesQuotes`**: Lưu ghi chú cá nhân, trích dẫn tâm đắc theo từng trang sách (`PageNumber`), bật/tắt chia sẻ công khai (`IsPublic`) có chống IDOR, lọc theo đầu sách hoặc chế độ Cộng đồng (`Views/Modules/S04_BookNotesQuotes/Index.cshtml`, `s04-book-notes-quotes.css`, `s04-book-notes-quotes.js`).
- [x] **S05 - `IsbnAutoCatalog`**: Nhập hoặc chọn mã vạch ISBN mẫu tự động tra cứu (CSDL nội bộ + Kho chuẩn ISBN + OpenLibrary API) để tự điền Tên sách, Tác giả, NXB, Năm XB, Số trang, Ảnh bìa và lưu thẳng vào `Books` kèm sinh bản sao `BookCopies` (`Views/Modules/S05_IsbnAutoCatalog/Index.cshtml`, `s05-isbn-auto-catalog.css`, `s05-isbn-auto-catalog.js`).
- [x] **S06 - `SearchDiscovery`**: Cổng tìm kiếm đa tiêu chí chuyên sâu (Từ khóa, Tác giả, ISBN, Thể loại, Khu vực địa lý, Nguồn sở hữu `System` vs `User`, Trạng thái, Điểm `TrustScore`) và tạo phiếu mượn `LoanRequests` trực tiếp (`Views/Modules/S06_SearchDiscovery/Index.cshtml`, `s06-search-discovery.css`, `s06-search-discovery.js`).
- [x] **S07 - `LibraryCatalog`**: Tra cứu tồn kho của các Thư viện đối tác (`Libraries`), thống kê theo 4 trạng thái (`Available`, `Reserved`, `Borrowed`, `Damaged`), xem mã `Barcode` và hộp thoại định vị kệ vật lý `ShelfLocation` (`Views/Modules/S07_LibraryCatalog/Index.cshtml`, `s07-library-catalog.css`, `s07-library-catalog.js`).
- [x] Cập nhật thanh điều hướng `_Layout.cshtml` & `Views/Home/Index.cshtml` kết nối đầy đủ `S01` $\rightarrow$ `S07` và kiểm tra `dotnet build` đạt **0 Error**.

---

## Giai đoạn 4: Phát triển Nhóm Dịch vụ Mượn – Trả, Kiểm định Ảnh & AI Gemini (S08 $\rightarrow$ S15) (Đã hoàn thành Giai đoạn 4A: S08 $\rightarrow$ S12)
- [x] **S08 - `LibraryLoanFlow`**: Luồng mượn sách Kho chung (Gửi yêu cầu `Pending` $\rightarrow$ Thủ thư kiểm tra hạn mức gói & bấm Duyệt `Approved` $\rightarrow$ Bàn giao tại quầy tạo `Loans` & đổi `BookCopies.Status = Borrowed` $\rightarrow$ Thu hồi sách trả, kiểm tra tình trạng & tự động báo hàng chờ S11).
- [x] **S09 - `P2PLoanFlow`**: Luồng mượn P2P giữa các cá nhân (Xem điểm uy tín `TrustScore` $\rightarrow$ Chủ sách duyệt & chọn Điểm hẹn Hub/CLB $\rightarrow$ Chuyển trạng thái `InTransit` $\rightarrow$ Bàn giao `Borrowed` cộng thưởng `+1 LendQuotaBonus` và `+3 TrustScore` cho chủ sách $\rightarrow$ Hoàn trả & cộng điểm).
- [x] **S10 - `ConditionReport`**: Biên bản điện tử chụp & tải ảnh tình trạng sách lúc giao (`Handover`) và lúc trả (`Return`) lưu vào bảng `ConditionReports` (hỗ trợ upload file ảnh vào `/uploads/conditions/` hoặc Cloudinary), đối chiếu 2 ảnh cạnh nhau (Side-by-Side Comparison) để phát hiện hư hại.
- [x] **S11 - `HotBookQueue`**: Đặt trước (`Reservations`) và xếp hàng chờ tự động cho các đầu sách hot đang được mượn hết (ưu tiên tự động đẩy thành viên gói `Premium` lên trước thành viên `Free`), tự động gửi thông báo `Notifications` cho người đứng đầu hàng khi có sách trả về.
- [x] **S12 - `LoanRenewalFine`**: Gia hạn thời gian mượn (`RenewCount` tối đa 2 lần, mỗi lần +7 ngày nếu sách chưa có người đặt trước S11) và tự động tính phí phạt trả trễ hạn theo ngày (`FineAmount`, 5.000đ/ngày) kèm tạo phiếu thu phạt có chữ ký HMAC trong bảng `Transactions`.
- [ ] **S13 - `GeminiAiAssistant`**: Trợ lý Chat AI (Gemini 2.5 Flash) kèm RAG nhẹ tư vấn chọn sách theo tâm trạng/ngành học, tóm tắt sách và tra cứu nhanh hạn trả sách.
- [ ] **S14 - `AiRecommendation`**: Gợi ý sách cá nhân hóa dựa trên lịch sử thể loại hay đọc/mượn và tủ sách hiện tại.
- [ ] **S15 - `TrustScoreReview`**: Đánh giá chất lượng bản sao sách và chấm điểm uy tín (`TrustScore`) 2 chiều giữa người cho mượn và người mượn.

---

## Giai đoạn 5: Phát triển Nhóm Dịch vụ Gói Thành viên, CLB/Hub, Thủ thư & Admin (S16 $\rightarrow$ S22)
- [ ] **S16 - `MembershipTier`**: Quản lý gói thành viên (`Free` / `Premium`) tăng giới hạn số sách mượn cùng lúc (`MaxConcurrentLoans`) và ưu tiên hàng chờ.
- [ ] **S17 - `SandboxPayment`**: Cổng thanh toán mô phỏng (`VNPaySandbox` / `MoMoSandbox`) có chữ ký `HmacSignature` cho nâng cấp gói Premium, nộp phạt trễ hạn và tiền cọc mô phỏng.
- [ ] **S18 - `NotificationHub`**: Trung tâm thông báo thời gian thực trên web kết hợp lịch sử gửi email nhắc hạn tự động qua Resend.
- [ ] **S19 - `DisputeResolution`** (`Area: Admin`): Khiếu nại và phân xử tranh chấp khi sách trả bị rách, mất trang hoặc thất lạc dựa trên ảnh đối chiếu `ConditionReports`.
- [ ] **S20 - `ExchangeHubClub`** (`Area: Curator`): Quản lý Câu lạc bộ sách & Điểm hẹn giao nhận sách an toàn (`Book Drop-off Hub`) tại trường học/quán cà phê đối tác.
- [ ] **S21 - `LibrarianDesk`** (`Area: Librarian`): Bàn làm việc chuyên biệt của Thủ thư — nhập lô sách mới, quét Barcode duyệt nhanh mượn/trả tại quầy, quản lý sách hỏng.
- [ ] **S22 - `AdminAnalytics`** (`Area: Admin`): Dashboard biểu đồ thống kê toàn hệ thống, quản lý người dùng/vai trò và giám sát Background Jobs (Hangfire).
