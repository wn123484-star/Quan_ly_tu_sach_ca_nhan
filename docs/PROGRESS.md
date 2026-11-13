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

## Giai đoạn 4: Phát triển Nhóm Dịch vụ Mượn – Trả, Kiểm định Ảnh, AI Gemini & Chuẩn hóa Rubric (S08 $\rightarrow$ S15) (Hoàn thành 100%)
- [x] **S08 - `LibraryLoanFlow`**: Luồng mượn sách Kho chung (Gửi yêu cầu `Pending` $\rightarrow$ Thủ thư kiểm tra hạn mức gói & bấm Duyệt `Approved` $\rightarrow$ Bàn giao tại quầy tạo `Loans` & đổi `BookCopies.Status = Borrowed` $\rightarrow$ Thu hồi sách trả, kiểm tra tình trạng & tự động báo hàng chờ S11).
- [x] **S09 - `P2PLoanFlow`**: Luồng mượn P2P giữa các cá nhân (Xem điểm uy tín `TrustScore` $\rightarrow$ Chủ sách duyệt & chọn Điểm hẹn Hub/CLB $\rightarrow$ Chuyển trạng thái `InTransit` $\rightarrow$ Bàn giao `Borrowed` cộng thưởng `+1 LendQuotaBonus` và `+3 TrustScore` cho chủ sách $\rightarrow$ Hoàn trả & cộng điểm).
- [x] **S10 - `ConditionReport`**: Biên bản điện tử chụp & tải ảnh tình trạng sách lúc giao (`Handover`) và lúc trả (`Return`) lưu vào bảng `ConditionReports` (hỗ trợ upload file ảnh vào `/uploads/conditions/` hoặc Cloudinary), đối chiếu 2 ảnh cạnh nhau (Side-by-Side Comparison) để phát hiện hư hại.
- [x] **S11 - `HotBookQueue`**: Đặt trước (`Reservations`) và xếp hàng chờ tự động cho các đầu sách hot đang được mượn hết (ưu tiên tự động đẩy thành viên gói `Premium` lên trước thành viên `Free`), tự động gửi thông báo `Notifications` cho người đứng đầu hàng khi có sách trả về.
- [x] **S12 - `LoanRenewalFine`**: Gia hạn thời gian mượn (`RenewCount` tối đa 2 lần, mỗi lần +7 ngày nếu sách chưa có người đặt trước S11) và tự động tính phí phạt trả trễ hạn theo ngày (`FineAmount`, 5.000đ/ngày) kèm tạo phiếu thu phạt có chữ ký HMAC trong bảng `Transactions`.
- [x] **S13 - `GeminiAiAssistant`**: Trợ lý Chat AI (Gemini 2.5 Flash) kèm RAG nhẹ từ SQL Server + Smart Local RAG Fallback, lưu lịch sử hội thoại vào `AiChatSessions` & `AiChatMessages`, đồng bộ với Widget Chat nổi ở góc phải `_Layout.cshtml` và chống XSS bằng `innerText`.
- [x] **S14 - `AiRecommendation`**: Gợi ý sách cá nhân hóa dựa trên lịch sử đọc `ReadingProgresses` (S03) và lịch sử mượn `Loans`, lọc theo tâm trạng (`DeepFocus`, `CareerBoost`, `WeekendRelax`) & chuyên ngành, tích hợp AI sinh lộ trình đọc 14 ngày và nút 1 chạm tạo phiếu mượn / thêm vào mục tiêu đọc S03.
- [x] **S15 - `TrustScoreReview`**: Đánh giá 2 chiều (`Reviews`: đánh giá sách `Book` và đánh giá thành viên `User` từ 1–5 sao kèm nhận xét), tự động cộng/trừ `AspNetUsers.TrustScore` và hiển thị Bảng vinh danh Độc giả Uy tín.
- [x] **Chuẩn hóa 4 Hạng mục Bắt buộc theo Rubric Giảng viên**:
  - [x] **Mục 2.8 (Phân trang SQL Server & Empty State)**: Bổ sung `.Skip((page - 1) * pageSize).Take(pageSize)`, chọn `pageSize = 6 / 12 / 24`, thanh điều hướng phân trang và giao diện Empty State tại `S06SearchDiscovery` & `S07LibraryCatalog`.
  - [x] **Mục 2.4 (Quản lý Hồ sơ Cá nhân & Đổi mật khẩu)**: Bổ sung Tab 4 tại `S01AuthOtpGoogle` cho phép xem và cập nhật `FullName`, `PhoneNumber`, `Address`, `AvatarUrl` và đổi mật khẩu lưu trực tiếp xuống `AspNetUsers`.
  - [x] **Mục 2.5 (Hoàn thiện đủ 4 thao tác CRUD)**: Bổ sung chức năng Sửa (`UpdateBookCopy`, `UpdateShelf`) và Xóa (`DeleteBookCopy`, `DeleteShelf`) có xác nhận và kiểm tra chống IDOR (`OwnerUserId == currentUserId`) tại `S02PersonalShelf`.
  - [x] **Mục 6 (An toàn Bảo mật & Kiểm soát Role 403)**: Làm sạch `appsettings.json` (chỉ đọc chuỗi kết nối & khóa bí mật từ `.env`), kiểm tra chặt quyền `Librarian` / `Admin` ở Backend tại `S07` và `S08`, trả về cảnh báo HTTP `403` rõ ràng trên `#systemStatusBox` khi `User` thường gọi trái phép.

---

## Giai đoạn 5: Phát triển Nhóm Dịch vụ Gói Thành viên, CLB/Hub, Thủ thư & Admin (S16 $\rightarrow$ S22) (Hoàn thành 100% — Đạt mốc 22/22 Dịch vụ)
- [x] **S16 - `MembershipTier`** (`/S16MembershipTier`): Quản lý gói thành viên (`Free` tối đa 2 cuốn vs `Premium` tối đa 5 cuốn + ưu tiên đầu hàng chờ S11), hiển thị công thức hạn mức mượn hiện tại (`MaxConcurrentLoans + LendQuotaBonus`), ngày hết hạn (`ExpiryDate`) và nâng cấp/gia hạn gói trực tiếp (`Views/Modules/S16_MembershipTier/Index.cshtml`, `s16-membership-tier.css`, `s16-membership-tier.js`).
- [x] **S17 - `SandboxPayment`** (`/S17SandboxPayment`): Cổng thanh toán mô phỏng (`VNPaySandbox` / `MoMoSandbox`) cho 3 loại giao dịch hợp lệ (`Membership`, `Fine`, `SimulatedDeposit`), tạo và xác thực chữ ký điện tử `HMAC-SHA512` (`HmacSignature`) chống giả mạo ở luồng Callback trước khi cập nhật bảng `Transactions` và kích hoạt gói `Memberships` (`Views/Modules/S17_SandboxPayment/Index.cshtml`, `s17-sandbox-payment.css`, `s17-sandbox-payment.js`).
- [x] **S18 - `NotificationHub`** (`/S18NotificationHub`): Trung tâm thông báo trong web (`Notifications`: lọc theo loại thông báo, đánh dấu đã đọc từng mục / tất cả) kết hợp nút kích hoạt gửi Email nhắc hạn trả sách (`DueReminder`) và thông báo có sách đặt trước (`QueueAvailable`) qua `IResendEmailService` (`Views/Modules/S18_NotificationHub/Index.cshtml`, `s18-notification-hub.css`, `s18-notification-hub.js`).
- [x] **S19 - `DisputeResolution`** (`/S19DisputeResolution` — `User` gửi khiếu nại, `[Authorize(Roles = "Admin")]` phân xử): Tạo hồ sơ tranh chấp (`Disputes`) kèm đối chiếu 2 ảnh cạnh nhau (`Handover` vs `Return` từ `ConditionReports`), cho phép Admin ra phán quyết (`Resolved`/`Rejected`), trừ điểm `TrustScore`, khấu trừ tiền cọc và cập nhật trạng thái bản sao (`Views/Modules/S19_DisputeResolution/Index.cshtml`, `s19-dispute-resolution.css`, `s19-dispute-resolution.js`).
- [x] **S20 - `ExchangeHubClub`** (`/S20ExchangeHubClub` — `[Authorize(Roles = "Curator,Admin")]` & `User`): Quản lý Điểm trạm giao nhận sách P2P an toàn (`Book Drop-off Hub` tại ĐH CMC / CLB Sách), cho phép User đăng ký ký gửi và Curator xác nhận kiểm định sách thật ngoài đời (+2 `TrustScore`) cũng như điều phối giao nhận trung gian P2P (`Views/Modules/S20_ExchangeHubClub/Index.cshtml`, `s20-exchange-hub-club.css`, `s20-exchange-hub-club.js`).
- [x] **S21 - `LibrarianDesk`** (`/S21LibrarianDesk` — Khóa quyền `[Authorize(Roles = "Librarian,Admin")]`): Bàn làm việc chuyên biệt của Thủ thư — quét nhanh Barcode để duyệt giao/nhận trả sách tại quầy, nhập lô bản sao mới (`BS-SYS-xxxx`) vào kho chung, chuyển trạng thái sách hỏng/mất (`Damaged`/`Lost`) và theo dõi danh sách độc giả quá hạn (`Views/Modules/S21_LibrarianDesk/Index.cshtml`, `s21-librarian-desk.css`, `s21-librarian-desk.js`).
- [x] **S22 - `AdminAnalytics`** (`/S22AdminAnalytics` — Khóa quyền `[Authorize(Roles = "Admin")]`): Dashboard biểu đồ thống kê toàn diện `Chart.js` (doanh thu gói thành viên & phí phạt, tỷ lệ mượn Kho chung vs P2P, phân bổ bản sao), quản lý khóa/mở khóa tài khoản & cấp quyền Role, xem nhật ký thao tác hệ thống và điều khiển chạy/giám sát 3 Background Jobs hằng ngày (`BackgroundJobs/BookShareDailyJobs.cs` + Hangfire) (`Views/Modules/S22_AdminAnalytics/Index.cshtml`, `s22-admin-analytics.css`, `s22-admin-analytics.js`).
- [x] **Hoàn thiện Tài liệu Bàn giao Cuối kỳ**: Tạo file `README.md` ở thư mục gốc, đồng bộ thanh điều hướng `_Layout.cshtml` & `Views/Home/Index.cshtml` kết nối đủ 22 dịch vụ (`S01` $\rightarrow$ `S22`), và kiểm tra `dotnet build` đạt **0 Error**.
