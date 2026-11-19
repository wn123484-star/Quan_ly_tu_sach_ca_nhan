# 📚 BookShare Portal — Nền tảng Quản lý Tủ sách Cá nhân, Theo dõi Tiến độ Đọc & Chia sẻ Sách Giấy Cộng đồng (.NET 10 MVC)

> **Học phần:** Phát triển Ứng dụng Web • Trường Đại học CMC  
> **Nền tảng kỹ thuật:** ASP.NET Core 10 MVC (`.NET 10`) • Entity Framework Core 10 (`Database-First Scaffold`) • SQL Server (22 bảng nghiệp vụ + 3 bảng phụ trợ Identity) • Cookie Auth & Google OAuth 2.0 • Resend OTP & Email REST API • Google Gemini 2.5 Flash (RAG) • VNPay/MoMo Sandbox (`HMAC-SHA512`) • Hangfire Background Jobs  
> **Nguyên tắc Nghiệp vụ Cốt lõi:** **100% Chia sẻ & Mượn trả Sách Giấy Học thuật & P2P — TUYỆT ĐỐI KHÔNG BÁN SÁCH** (Không có cột giá bán `Price`, không có nút `"Mua ngay"` hay `"Giỏ hàng thương mại"`).

---

## 1. Kiến trúc Tổng thể & Nguyên tắc Kỹ thuật

1. **Database-First Scaffold Chuẩn .NET 10:**
   - Cơ sở dữ liệu `BookShareDb` gồm **22 bảng nghiệp vụ cốt lõi** (cùng 3 bảng phụ trợ ASP.NET Core Identity) được định nghĩa tại `database/01_BookShare_Schema.sql` và nạp dữ liệu mẫu phong phú tại `database/02_BookShare_SeedData.sql`.
   - Toàn bộ class thực thể trong `src/BookShare.Web/Models/Entities/` được sinh tự động bằng lệnh `dotnet ef dbcontext scaffold`.
2. **Kiến trúc Phân tầng Nghiêm ngặt (Strict Service Layer Pattern):**
   - `Controller` $\rightarrow$ `IService (Interface)` $\rightarrow$ `Service (Implementation)` $\rightarrow$ `BookShareDbContext`.
   - Không có bất kỳ Controller nào gọi trực tiếp `BookShareDbContext`.
3. **Quy tắc Cô lập 22 Dịch vụ Độc lập (Bộ 3 File `1-1-1`):**
   - Mỗi dịch vụ từ `S01` đến `S22` sở hữu riêng **1 file View `.cshtml`**, **1 file Style `.css`** và **1 file Script `.js` (ES6 Module)** giao tiếp qua `apiFetch()` tự động đính kèm `RequestVerificationToken` (chống CSRF) và cập nhật khung trạng thái `#systemStatusBox` (`MÃ 200 / 300 / 400 / 403 / 404 / 500`).
4. **Kiểm soát Phân quyền 4 Vai trò (`[Authorize(Roles = "...")]`) & Chống IDOR:**
   - Khóa chặt các thao tác của `Curator`, `Librarian` và `Admin` bằng `[Authorize(Roles = "...")]` ở Backend, tự động trả về cảnh báo `MÃ 403 FORBIDDEN` rõ ràng trên `#systemStatusBox` nếu tài khoản không đủ quyền.

---

## 2. Hướng dẫn Cài đặt, Nạp CSDL & Chạy Ứng dụng

### Bước 1: Yêu cầu Môi trường
- **.NET SDK 10.0** trở lên (`dotnet --version`).
- **SQL Server** (`(localdb)\MSSQLLocalDB` hoặc `SQL Server Express / Developer`).

### Bước 2: Cấu hình Biến Môi trường (`.env`)
Tại thư mục gốc `BookShare.Solution`, sao chép tệp `.env.example` thành `.env` (hoặc chỉnh sửa tệp `.env` có sẵn). Hệ thống sử dụng `DotNetEnv.Env.TraversePath().Load()` ở dòng đầu tiên của `Program.cs` với quy ước dấu 2 gạch dưới (`__`):

```ini
# 1. Chuỗi kết nối SQL Server
ConnectionStrings__DefaultConnection="Server=(localdb)\\MSSQLLocalDB;Database=BookShareDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;"

# 2. Gemini AI API (Dịch vụ S13 & S14 - Có sẵn Smart Local RAG Fallback khi chưa điền key)
Gemini__ApiKey=""
Gemini__Model="gemini-2.5-flash"

# 3. Resend Email API (Dịch vụ S01 OTP 6 số & S18 Nhắc hạn - Có sẵn Dev OTP Console Fallback)
Resend__ApiKey=""
Resend__FromEmail="BookShare <onboarding@resend.dev>"

# 4. Google OAuth 2.0 (Dịch vụ S01)
Authentication__Google__ClientId=""
Authentication__Google__ClientSecret=""

# 5. VNPay Sandbox HMAC-SHA512 (Dịch vụ S16 & S17)
VNPay__TmnCode="BOOKSHARECMC"
VNPay__HashSecret="BOOKSHARE_VNPAY_SANDBOX_SECRET_KEY_2026_HMAC512"
```

### Bước 3: Khởi tạo Schema 22 Bảng & Nạp Dữ liệu Mẫu SQL Server
Chạy 2 script SQL theo thứ tự vào SQL Server (`(localdb)\MSSQLLocalDB` hoặc `localhost`):
```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -i "database/01_BookShare_Schema.sql"
sqlcmd -S "(localdb)\MSSQLLocalDB" -i "database/02_BookShare_SeedData.sql"
```

### Bước 4: Biên dịch & Khởi chạy Ứng dụng
```powershell
dotnet build BookShare.sln
dotnet run --project src/BookShare.Web/BookShare.Web.csproj
```
Mở trình duyệt tại địa chỉ hiển thị trên Console (mặc định: `http://localhost:5062` hoặc `https://localhost:7195`).

---

## 3. Danh sách 4 Tài khoản Mẫu Đại diện 4 Cấp Phân quyền

Bạn có thể bấm nút **"Điền nhanh"** ngay tại trang **`/S01AuthOtpGoogle`** để chuyển đổi tức thì giữa 4 vai trò:

| STT | Vai trò (`Role`) | Họ và tên | Email đăng nhập | Mật khẩu mẫu | Quyền hạn cốt lõi |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **1** | **`Admin`** | Quản Trị Viên Hệ Thống | `admin@bookshare.vn` | `Admin@123` | Toàn quyền hệ thống, phân xử tranh chấp (`S19`), cấp quyền Role, khóa tài khoản & điều khiển Hangfire Jobs (`S22`). |
| **2** | **`Librarian`** | ThS. Phạm Thu Hà (Thủ thư) | `thuthu@cmc.edu.vn` | `Librarian@123` | Quản lý kho chung ĐH CMC (`S07`), duyệt mượn/trả kho chung (`S08`), quét Barcode nhanh, nhập lô sách & xử lý sách hỏng (`S21`). |
| **3** | **`Curator`** | Đỗ Quang Minh (Chủ nhiệm CLB) | `clbsach@cmc.edu.vn` | `Curator@123` | Quản lý Điểm trạm giao nhận P2P (`Book Drop-off Hub`), kiểm định sách thật ngoài đời & xác nhận giao nhận trung gian (`S20`). |
| **4** | **`User`** | Nguyễn Hùng (BIT250157) | `nguyenhung@cmc.edu.vn` | `User@123` | Quản lý tủ sách cá nhân (`S02`), theo dõi tiến độ đọc (`S03`), ghi chú (`S04`), đặt mượn sách (`S06/S08/S09`), đánh giá `TrustScore` (`S15`). |

*(Ngoài ra còn có 2 tài khoản `User` phụ phục vụ kiểm thử P2P & quá hạn: `minhanh@cmc.edu.vn` và `hoangnam@cmc.edu.vn` — mật khẩu `User@123`).*

---

## 4. Bảng Tổng hợp Đường dẫn Toàn bộ 22 Dịch vụ Độc lập (`S01` $\rightarrow$ `S22`)

| Mã DV | Tên Module Dịch vụ | Đường dẫn URL (Route) | Phân quyền | Bộ 3 File Độc lập (`CSHTML` + `CSS` + `JS`) |
| :--- | :--- | :--- | :--- | :--- |
| **S01** | **Xác thực, OTP Resend, Google OAuth & Hồ sơ** | `/S01AuthOtpGoogle` | Chung | `Views/Modules/S01_AuthOtpGoogle/Index.cshtml`<br>`wwwroot/modules/s01-auth-otp-google/s01-auth-otp-google.{css,js}` |
| **S02** | **Tủ sách Cá nhân & Bật/Tắt Cho mượn P2P (4-CRUD)** | `/S02PersonalShelf` | `User` | `Views/Modules/S02_PersonalShelf/Index.cshtml`<br>`wwwroot/modules/s02-personal-shelf/s02-personal-shelf.{css,js}` |
| **S03** | **Theo dõi Tiến độ Đọc, Mục tiêu & Biểu đồ (Ver07)** | `/S03ReadingTracker` | `User` | `Views/Modules/S03_ReadingTracker/Index.cshtml`<br>`wwwroot/modules/s03-reading-tracker/s03-reading-tracker.{css,js}` |
| **S04** | **Ghi chú Sách & Trích dẫn Tâm đắc theo Trang** | `/S04BookNotesQuotes` | `User` | `Views/Modules/S04_BookNotesQuotes/Index.cshtml`<br>`wwwroot/modules/s04-book-notes-quotes/s04-book-notes-quotes.{css,js}` |
| **S05** | **Quét Mã vạch ISBN Tự động Điền Thông tin Sách** | `/S05IsbnAutoCatalog` | `User`, `Librarian` | `Views/Modules/S05_IsbnAutoCatalog/Index.cshtml`<br>`wwwroot/modules/s05-isbn-auto-catalog/s05-isbn-auto-catalog.{css,js}` |
| **S06** | **Cổng Tìm kiếm Đa tiêu chí & Phân trang SQL** | `/S06SearchDiscovery` | Chung | `Views/Modules/S06_SearchDiscovery/Index.cshtml`<br>`wwwroot/modules/s06-search-discovery/s06-search-discovery.{css,js}` |
| **S07** | **Danh mục Tồn kho Thư viện Đối tác & Định vị Kệ** | `/S07LibraryCatalog` | `Librarian`, Chung | `Views/Modules/S07_LibraryCatalog/Index.cshtml`<br>`wwwroot/modules/s07-library-catalog/s07-library-catalog.{css,js}` |
| **S08** | **Luồng Mượn – Trả Sách Kho chung Thư viện** | `/S08LibraryLoanFlow` | `User`, `Librarian` | `Views/Modules/S08_LibraryLoanFlow/Index.cshtml`<br>`wwwroot/modules/s08-library-loan-flow/s08-library-loan-flow.{css,js}` |
| **S09** | **Luồng Mượn – Trả P2P Cộng đồng (+Quota Bonus)** | `/S09P2PLoanFlow` | `User` $\leftrightarrow$ `User` | `Views/Modules/S09_P2PLoanFlow/Index.cshtml`<br>`wwwroot/modules/s09-p2p-loan-flow/s09-p2p-loan-flow.{css,js}` |
| **S10** | **Biên bản Ảnh Tình trạng Sách (Handover vs Return)** | `/S10ConditionReport` | `User`, `Librarian` | `Views/Modules/S10_ConditionReport/Index.cshtml`<br>`wwwroot/modules/s10-condition-report/s10-condition-report.{css,js}` |
| **S11** | **Đặt trước & Hàng chờ Sách Hot (Ưu tiên Premium)** | `/S11HotBookQueue` | `User` | `Views/Modules/S11_HotBookQueue/Index.cshtml`<br>`wwwroot/modules/s11-hot-book-queue/s11-hot-book-queue.{css,js}` |
| **S12** | **Gia hạn Mượn Sách & Tự động Tính Phí Trễ hạn** | `/S12LoanRenewalFine` | `User`, `Librarian` | `Views/Modules/S12_LoanRenewalFine/Index.cshtml`<br>`wwwroot/modules/s12-loan-renewal-fine/s12-loan-renewal-fine.{css,js}` |
| **S13** | **Trợ lý AI Thủ thư Gemini 2.5 Flash (RAG SQL)** | `/S13GeminiAiAssistant` | Chung (AI) | `Views/Modules/S13_GeminiAiAssistant/Index.cshtml`<br>`wwwroot/modules/s13-gemini-ai-assistant/s13-gemini-ai-assistant.{css,js}` |
| **S14** | **Gợi ý Sách AI Cá nhân hóa & Lộ trình Đọc 14 Ngày** | `/S14AiRecommendation` | `User` (AI) | `Views/Modules/S14_AiRecommendation/Index.cshtml`<br>`wwwroot/modules/s14-ai-recommendation/s14-ai-recommendation.{css,js}` |
| **S15** | **Đánh giá 2 Chiều & Bảng Vinh danh TrustScore** | `/S15TrustScoreReview` | `User` | `Views/Modules/S15_TrustScoreReview/Index.cshtml`<br>`wwwroot/modules/s15-trust-score-review/s15-trust-score-review.{css,js}` |
| **S16** | **Quản lý Gói Thành viên (`Free` vs `Premium` Quota)** | `/S16MembershipTier` | `User` | `Views/Modules/S16_MembershipTier/Index.cshtml`<br>`wwwroot/modules/s16-membership-tier/s16-membership-tier.{css,js}` |
| **S17** | **Cổng Thanh toán Sandbox & Xác thực `HMAC-SHA512`** | `/S17SandboxPayment` | `User`, `Admin` | `Views/Modules/S17_SandboxPayment/Index.cshtml`<br>`wwwroot/modules/s17-sandbox-payment/s17-sandbox-payment.{css,js}` |
| **S18** | **Trung tâm Thông báo Web & Gửi Email Resend** | `/S18NotificationHub` | Chung | `Views/Modules/S18_NotificationHub/Index.cshtml`<br>`wwwroot/modules/s18-notification-hub/s18-notification-hub.{css,js}` |
| **S19** | **Khiếu nại & Phân xử Tranh chấp Sách Hỏng/Mất** | `/S19DisputeResolution` | `User`, `Admin` | `Views/Modules/S19_DisputeResolution/Index.cshtml`<br>`wwwroot/modules/s19-dispute-resolution/s19-dispute-resolution.{css,js}` |
| **S20** | **Điểm Trạm Giao nhận Sách P2P & CLB Sách** | `/S20ExchangeHubClub` | `Curator`, `User` | `Views/Modules/S20_ExchangeHubClub/Index.cshtml`<br>`wwwroot/modules/s20-exchange-hub-club/s20-exchange-hub-club.{css,js}` |
| **S21** | **Bàn Thủ thư: Quét Barcode, Nhập Lô & Quá hạn** | `/S21LibrarianDesk` | `Librarian`, `Admin` | `Views/Modules/S21_LibrarianDesk/Index.cshtml`<br>`wwwroot/modules/s21-librarian-desk/s21-librarian-desk.{css,js}` |
| **S22** | **Dashboard Quản trị Admin, Role & Hangfire Jobs** | `/S22AdminAnalytics` | `Admin` | `Views/Modules/S22_AdminAnalytics/Index.cshtml`<br>`wwwroot/modules/s22-admin-analytics/s22-admin-analytics.{css,js}` |
