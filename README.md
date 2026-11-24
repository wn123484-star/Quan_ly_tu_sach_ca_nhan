# BookShare Portal — Nền tảng Quản lý Tủ sách Cá nhân, Theo dõi Tiến độ Đọc & Chia sẻ Sách Giấy Cộng đồng (.NET 10 MVC)

> **Học phần:** Phát triển Ứng dụng Web • Trường Đại học CMC  
> **Nền tảng kỹ thuật:** ASP.NET Core 10 MVC (`.NET 10`) • Entity Framework Core 10 (`Database-First Scaffold`) • SQL Server (`28 bảng dbo`) • Cookie Authentication (`OnValidatePrincipal` tự dọn phiên mồ côi), 2-Way QR Login & Google OAuth 2.0 • Resend OTP & Email API • Google Gemini AI Copilot (Function Calling 2 tầng) • VNPay/MoMo Sandbox (`HMAC-SHA512`) • Hangfire Background Jobs • Admin System Icon Manager (`IMemoryCache`) • 100% Local Assets (0 CDN ngoài) • Android WebView Shell (`mobile/bookshare-android/`)  
> **Nguyên tắc Nghiệp vụ Cốt lõi:** **100% Chia sẻ & Mượn trả Sách Giấy Học thuật & P2P 0đ — TUYỆT ĐỐI KHÔNG BÁN SÁCH** (Không có giá bán, không có nút mua hàng hay giỏ hàng thương mại).

---

## 1. Kiến trúc Tổng thể & Các Chuẩn Kỹ thuật Đã Khóa Cứng

1. **Database-First Scaffold (.NET 10):**
   - Cơ sở dữ liệu `BookShareDb` gồm **28 bảng `dbo`** (22 bảng nghiệp vụ cốt lõi + 3 bảng ASP.NET Core Identity + 3 bảng Sprint 3 & 3.5: `QrLoginSessions`, `BookHandoverQrs`, `RoleUpgradeRequests`, `SystemIconSettings`) được định nghĩa tại [`database/01_BookShare_Schema.sql`](database/01_BookShare_Schema.sql) và dữ liệu mẫu chuẩn tại [`database/02_BookShare_SeedData.sql`](database/02_BookShare_SeedData.sql).
   - Thực thể và `BookShareDbContext` được sinh từ DB bằng `dotnet ef dbcontext scaffold --schema dbo` trong `src/BookShare.Web/Models/Entities/` và `src/BookShare.Web/Data/`.
   - **`DatabaseBootstrapService` & Tự phục hồi trên CSDL trắng:** Ngay khi ứng dụng khởi động (hoặc sau khi Admin làm sạch/nạp lại CSDL), hệ thống tự đảm bảo các bảng mở rộng, 4 vai trò gốc, tài khoản Quản trị viên `admin@bookshare.vn` (`Admin@123`) và 18 cấu hình biểu tượng mặc định; đồng thời `OnValidatePrincipal` tự động vô hiệu hóa Cookie cũ nếu tài khoản đã bị xóa sau khi reset CSDL Docker.
2. **Strict Service Layer Pattern:**
   - Mọi luồng xử lý đi qua `Controller -> IService -> Service -> BookShareDbContext`. Không Controller nào tham chiếu trực tiếp `BookShareDbContext`.
3. **22 Phân hệ Độc lập + Bộ API Đồng hành Sprint 3/3.5, Admin Icon Manager & Android App:**
   - Mỗi phân hệ (`S01` đến `S22`) sở hữu riêng view Razor (`Views/Modules/Sxx_*/Index.cshtml`), stylesheet CSS chuẩn biến `--portal-*`, và ES6 module JavaScript giao tiếp qua `apiFetch()` (`wwwroot/js/core-api.js`) tự động gắn CSRF Token và cập nhật `#systemStatusBox`.
   - Hỗ trợ đầy đủ **Đăng nhập QR 2 chiều** (`/api/qr-login/*`), **QR Bàn giao & Hoàn trả Sách** (`/api/handover-qr/*`), **Đề nghị nâng quyền Chủ nhiệm CLB** (`S01 -> S22`), **Trợ lý AI Copilot 2 tầng** (Client Commands + Action Cards có xác nhận 1 chạm & kiểm soát RBAC), **Tab Admin Tùy Chỉnh Biểu Tượng Toàn Trang** có công tắc gạt 2 chiều từng icon tại `/S22AdminAnalytics`, **100% thư viện tự host nội bộ** (`wwwroot/vendor/*`, 0 CDN ngoài) và **Ứng dụng Android WebView Shell** (`mobile/bookshare-android/`).
4. **Giao diện Mặc định Light Mode & Ngôn ngữ Chuyên nghiệp:**
   - Mặc định `data-bs-theme="light"` trong `_Layout.cshtml`, màn hình tải `.app-splash` nền sáng `#f8fafc`, PWA `manifest.json` (`#f8fafc`), Android App nền `#F8FAFC`, và `service-worker.js` (`bookshare-static-v2`) đồng bộ nền sáng (có nút chuyển Dark Mode thủ công hoặc qua AI Copilot).
   - Toàn bộ giao diện người dùng đã loại bỏ thuật ngữ kỹ thuật nội bộ, không có tài khoản mẫu hay nút điền nhanh trên trang Đăng nhập `/S01AuthOtpGoogle`, và mọi thẻ `<img>` đều có `alt` và `onerror`.
5. **Bảo mật Đa tầng, RBAC 4 Vai trò & OAuth Fail-Closed:**
   - Khóa `[Authorize]` và `[Authorize(Roles = "...")]` trên toàn bộ action ghi dữ liệu và API đặc quyền (`Admin`, `Librarian`, `Curator`), trả về JSON `401`/`403` cho các yêu cầu API chưa xác thực hoặc thiếu quyền.
   - Kiểm tra chống IDOR theo quyền sở hữu ở tầng Service (`S02`, `S03`, `S04`, `S09`, `S10`, `S11`, `S12`, `S13`, `S15`, `S17`, `S18`, `S19`, `S20`).
   - Đăng nhập Google OAuth 2.0 yêu cầu `Authentication__Google__ClientId` và `Authentication__Google__ClientSecret` hợp lệ trong `.env`; nếu chưa cấu hình, backend từ chối `HTTP 400` và vô hiệu hóa nút Google trên UI (fail-closed, không tự đăng nhập mẫu).
   - Đăng ký công khai luôn gán vai trò `"User"`, khóa tài khoản thực thi `LockoutEnd > DateTimeOffset.UtcNow` trên cả đăng nhập mật khẩu và Google OAuth, đồng thời nâng cấp mật khẩu khởi tạo sang hash ASP.NET Core Identity chuẩn khi đăng nhập.

---

## 2. Hướng dẫn Cài đặt, Nạp CSDL & Chạy Ứng dụng

### Bước 1: Yêu cầu Môi trường
- **.NET SDK 10.0** trở lên (`dotnet --version`)
- **SQL Server** (`(localdb)\MSSQLLocalDB` hoặc SQL Server 2022 qua Docker Compose)
- **Python 3.10+** (nếu chạy bộ kiểm thử tự động `scripts/qa_full_bot.py` và `scripts/smoke_test.py`)

### Bước 2: Cấu hình Biến Môi trường (`.env`)
Sao chép `.env.example` thành `.env` ở thư mục gốc (`Program.cs` tự nạp qua `DotNetEnv.Env.TraversePath().Load()`):

```ini
ConnectionStrings__DefaultConnection="Server=(localdb)\\MSSQLLocalDB;Database=BookShareDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;"

Gemini__ApiKey=""
Gemini__Model="gemini-2.5-flash"

Resend__ApiKey=""
Resend__FromEmail="BookShare <onboarding@resend.dev>"

Authentication__Google__ClientId=""
Authentication__Google__ClientSecret=""

VNPay__TmnCode=""
VNPay__HashSecret=""
VNPay__BaseUrl="https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"
VNPay__ReturnUrl="http://localhost:5087/S17SandboxPayment"

ReCaptcha__SiteKey=""
ReCaptcha__SecretKey=""
```

> Xem hướng dẫn cấu hình Google OAuth 2.0, Resend Email, SMTP dự phòng và triển khai Docker tại [`docs/DEPLOYMENT_OAUTH_GUIDE.md`](docs/DEPLOYMENT_OAUTH_GUIDE.md).

### Bước 3: Khởi tạo CSDL & Chạy Ứng dụng
```powershell
# 1. Nạp Schema 22 bảng & dữ liệu mẫu
sqlcmd -S "(localdb)\MSSQLLocalDB" -i "database/01_BookShare_Schema.sql"
sqlcmd -S "(localdb)\MSSQLLocalDB" -i "database/02_BookShare_SeedData.sql"

# 2. Biên dịch & chạy ứng dụng
dotnet build BookShare.sln --nologo
dotnet run --project src/BookShare.Web/BookShare.Web.csproj
```

Hoặc chạy bằng Docker Compose (`SQL Server 2022 + db-init schema-only + BookShare.Web` trên cổng `5087`):
```powershell
docker compose up -d --build
```
> **Lưu ý Docker Schema-Only & Nạp Dữ Liệu Mẫu 1 Chạm:** Mặc định `db-init` chỉ chạy `01_BookShare_Schema.sql` (28 bảng sạch khi chạy thực tế; `DatabaseBootstrapService` tự khởi tạo 4 vai trò, tài khoản `admin@bookshare.vn` mật khẩu `Admin@123` và 18 biểu tượng mặc định ngay khi khởi động). Nếu muốn nạp bộ dữ liệu cộng đồng mẫu (35 đầu sách, 55 bản sao, 6 tài khoản), hãy đăng nhập `admin@bookshare.vn` / `Admin@123` -> vào **Quản trị Hệ thống** (`/S22AdminAnalytics`) -> bấm **"Khôi Phục Bộ Dữ Liệu Cộng Đồng Chuẩn"**.

---

## 3. Tài khoản Khởi tạo Từ Dữ liệu Mẫu (Dành cho Quản trị & Kiểm thử)

> Thông tin tài khoản mẫu chỉ lưu phục vụ kiểm thử nội bộ tại tài liệu này và trong [`database/02_BookShare_SeedData.sql`](database/02_BookShare_SeedData.sql) — **không hiển thị trên giao diện Đăng nhập `/S01AuthOtpGoogle`**. Khuyến nghị đổi mật khẩu ngay sau lần đăng nhập đầu tiên trên môi trường thực tế.

| STT | Vai trò (`Role`) | User ID | Họ và tên | Email đăng nhập | Mật khẩu khởi tạo | Ghi chú nghiệp vụ & dữ liệu mẫu |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **1** | **`Admin`** | `u-admin-01` | Quản Trị Viên Hệ Thống | `admin@bookshare.vn` | `Admin@123` | Toàn quyền hệ thống, phân xử tranh chấp (`S19`), quản trị vai trò/khóa tài khoản & Hangfire Jobs (`S22`). |
| **2** | **`Librarian`** | `u-librarian-01` | ThS. Phạm Thu Hà | `thuthu@cmc.edu.vn` | `Librarian@123` | Quản lý tồn kho Kho chung (`S07`), duyệt/giao/thu hồi (`S08`), quét mã vạch & nhập lô (`S21`). |
| **3** | **`Curator`** | `u-curator-01` | Đỗ Quang Minh | `clbsach@cmc.edu.vn` | `Curator@123` | Quản lý Điểm trạm CLB (`S20`), kiểm định sách giấy & điều phối giao nhận trung gian P2P (`S09/S20`). |
| **4** | **`User`** | `u-user-01` | Nguyễn Hùng (BIT250157) | `nguyenhung@cmc.edu.vn` | `User@123` | Độc giả gói `Premium` (tối đa 5 + 1 suất thưởng P2P), tủ sách cá nhân (`S02`), hợp đồng mượn #1 `Active`. |
| **5** | **`User`** | `u-user-02` | Trần Minh Anh | `minhanh@cmc.edu.vn` | `User@123` | Độc giả gói `Premium`, đang mượn P2P hợp đồng #2 `Active`, dùng để kiểm thử luồng mượn – trả – đánh giá sạch. |
| **6** | **`User`** | `u-user-03` | Lê Hoàng Nam | `hoangnam@cmc.edu.vn` | `User@123` | Độc giả gói `Free`, đang có hợp đồng #3 `Overdue` (`-5 ngày`, phạt `25.000đ`) và hồ sơ tranh chấp mở để kiểm thử luật chặn mượn. |

---

## 4. Kiểm thử Tự động (E2E QA Bot & Smoke Audit) & Đóng Gói APK Android

Dự án đi kèm 2 kịch bản kiểm thử tự động chạy trực tiếp trên server Kestrel & LocalDB thật:

```powershell
# 1. Bộ kiểm thử E2E 7 tầng toàn diện (346 assertions: L1-L5 + L6 Sprint 3 + L7 Sprint 3.5: 0 CDN, 0 Schema Drift, QR, Header, Admin Icon Manager, Blank DB & Stale Cookie)
python scripts/qa_full_bot.py --autostart

# 2. Bộ kiểm thử khói 3 lớp (Build, Docker mount & quét nhãn UI, 23 route 200 OK, RBAC 401/403)
python scripts/smoke_test.py --autostart
```

### Đóng gói & Tải Ứng dụng Android (`BookShare-Mobile-Android.apk`) Qua GitHub Actions
- Mã nguồn ứng dụng Android Native WebView Shell nằm trong [`mobile/bookshare-android/`](mobile/bookshare-android/) (tự động nhận diện bằng `BookShareAndroidApp/1.0`, nền sáng `#F8FAFC`, không hiển thị thanh URL hay hộp thoại hỏi URL, mở Google OAuth bằng Custom Tabs, kéo thông báo nền qua `CookieManager`).
- Workflow [`.github/workflows/build-bookshare-android-apk.yml`](.github/workflows/build-bookshare-android-apk.yml) tự động biên dịch **`BookShare-Mobile-Android.apk`** bằng Java 17 + Gradle 8.7; có thể nhập trực tiếp `base_url` khi chạy **`Run workflow`** (`workflow_dispatch`) hoặc cấu hình biến `BOOKSHARE_BASE_URL` trong Repository Variables rồi tải file `.apk` tại mục **Artifacts** (xem chi tiết ở [`docs/DEPLOYMENT_OAUTH_GUIDE.md`](docs/DEPLOYMENT_OAUTH_GUIDE.md)).

Xem báo cáo kết quả kiểm thử chi tiết tại [`docs/QA_TEST_REPORT.md`](docs/QA_TEST_REPORT.md).

---

## 5. Danh mục 22 Phân hệ & Tài liệu Chuyên sâu

| Mã DV | Tên Phân hệ | URL Route | Quyền Ghi / Đặc quyền |
| :--- | :--- | :--- | :--- |
| **S01** | Xác thực, OTP Resend, Google OAuth & Hồ sơ | `/S01AuthOtpGoogle` | Công khai / `[Authorize]` (Hồ sơ, Đổi MK) |
| **S02** | Tủ sách Cá nhân & Bật/Tắt Cho mượn P2P | `/S02PersonalShelf` | `[Authorize]` + Anti-IDOR chủ sở hữu |
| **S03** | Theo dõi Tiến độ Đọc, Mục tiêu & Biểu đồ | `/S03ReadingTracker` | `[Authorize]` + Anti-IDOR chủ sở hữu |
| **S04** | Ghi chú Sách & Trích dẫn Tâm đắc theo Trang | `/S04BookNotesQuotes` | `[Authorize]` + Anti-IDOR chủ sở hữu |
| **S05** | Quét Mã vạch ISBN Tự động Điền Sách | `/S05IsbnAutoCatalog` | `[Authorize]` (`Librarian,Admin` cho kho chung) |
| **S06** | Cổng Tìm kiếm Đa tiêu chí & Phân trang | `/S06SearchDiscovery` | Công khai (Tìm kiếm) / `[Authorize]` (Đặt mượn) |
| **S07** | Danh mục Tồn kho Thư viện & Định vị Kệ | `/S07LibraryCatalog` | Công khai (Tra cứu) / `Librarian,Admin` |
| **S08** | Luồng Mượn – Trả Sách Kho chung Thư viện | `/S08LibraryLoanFlow` | `[Authorize]` (Mượn/Hủy) / `Librarian,Admin` |
| **S09** | Luồng Mượn – Trả P2P Cộng đồng | `/S09P2PLoanFlow` | `[Authorize]` + Anti-IDOR chủ sách / Curator |
| **S10** | Biên bản Ảnh Tình trạng Sách | `/S10ConditionReport` | `[Authorize]` + Anti-IDOR bên liên quan |
| **S11** | Đặt trước & Hàng chờ Sách Hot (Ưu tiên Premium) | `/S11HotBookQueue` | `[Authorize]` / `Librarian,Admin` (`NotifyTop`) |
| **S12** | Gia hạn Mượn Sách & Tính Phí Trễ hạn | `/S12LoanRenewalFine` | `[Authorize]` + Anti-IDOR / `Librarian,Admin` (`AssessFine`) |
| **S13** | Trợ lý AI Thủ thư Gemini (RAG SQL) | `/S13GeminiAiAssistant` | `[Authorize]` + Anti-IDOR phiên hội thoại |
| **S14** | Gợi ý Sách AI Cá nhân hóa & Lộ trình 14 Ngày | `/S14AiRecommendation` | `[Authorize]` |
| **S15** | Đánh giá 2 Chiều & Bảng Vinh danh Uy tín | `/S15TrustScoreReview` | `[Authorize]` + Anti-IDOR người đánh giá |
| **S16** | Quản lý Hạng Thành viên (`Free` / `Premium`) | `/S16MembershipTier` | `[Authorize]` (Liên kết thanh toán `S17`) |
| **S17** | Ví Bảo lãnh & Xác thực `HMAC-SHA512` | `/S17SandboxPayment` | `[Authorize]` + Anti-IDOR / `Librarian,Admin` |
| **S18** | Trung tâm Thông báo & Xác minh Email OTP | `/S18NotificationHub` | `[Authorize]` + Anti-IDOR / `Librarian,Admin` |
| **S19** | Khiếu nại & Phân xử Tranh chấp | `/S19DisputeResolution` | `[Authorize]` (Khiếu nại) / `Admin` (`ResolveDispute`) |
| **S20** | Điểm Trạm Giao nhận Sách P2P & CLB Sách | `/S20ExchangeHubClub` | `[Authorize]` (Ký gửi) / `Curator,Admin` |
| **S21** | Quầy Thủ thư: Quét Barcode & Nhập Lô | `/S21LibrarianDesk` | `Librarian,Admin` |
| **S22** | Quản trị Hệ thống, Vai trò & Hangfire Jobs | `/S22AdminAnalytics` | `Admin` |

### Tài liệu Kỹ thuật (`docs/`)
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — Kiến trúc phân tầng, sơ đồ cây thư mục và quy hoạch 22 phân hệ
- [`docs/RULES.md`](docs/RULES.md) — Quy chuẩn kỹ thuật và ràng buộc phi thương mại 0đ
- [`docs/SECURITY_RBAC.md`](docs/SECURITY_RBAC.md) — Ma trận phân quyền RBAC 4 vai trò, khóa IDOR & kiểm soát OAuth/HMAC/OTP
- [`docs/API_REFERENCE.md`](docs/API_REFERENCE.md) — Tài liệu đầy đủ các endpoint GET/POST của 23 Controller
- [`docs/DEPLOYMENT_OAUTH_GUIDE.md`](docs/DEPLOYMENT_OAUTH_GUIDE.md) — Hướng dẫn triển khai LocalDB/Docker, Google OAuth 2.0 & build APK GitHub Actions
- [`docs/QA_TEST_REPORT.md`](docs/QA_TEST_REPORT.md) — Báo cáo kết quả kiểm thử E2E 7 tầng (346/346 PASS) & lỗi đã khắc phục
- [`docs/UPGRADE_SPRINT3_PLAN.md`](docs/UPGRADE_SPRINT3_PLAN.md), [`docs/QR_AUTH_AND_HANDOVER_SPEC.md`](docs/QR_AUTH_AND_HANDOVER_SPEC.md), [`docs/AI_COPILOT_TOOLS_SPEC.md`](docs/AI_COPILOT_TOOLS_SPEC.md), [`docs/MOBILE_APP_ARCHITECTURE.md`](docs/MOBILE_APP_ARCHITECTURE.md) — Bộ đặc tả nâng cấp Sprint 3
- [`docs/SPRINT35_POLISH_PLAN.md`](docs/SPRINT35_POLISH_PLAN.md), [`docs/ICON_CUSTOMIZATION_SPEC.md`](docs/ICON_CUSTOMIZATION_SPEC.md), [`docs/LOCAL_ASSETS_AUDIT.md`](docs/LOCAL_ASSETS_AUDIT.md) — Bộ đặc tả Sprint 3.5 (Local Assets, QR/Header/Blank DB & Admin Icon Manager)
- [`docs/PROGRESS.md`](docs/PROGRESS.md) & [`TODO.md`](TODO.md) — Nhật ký hoàn thiện dự án theo từng pha
