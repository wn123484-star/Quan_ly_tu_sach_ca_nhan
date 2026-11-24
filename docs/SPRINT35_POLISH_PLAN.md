# KẾ HOẠCH TINH CHỈNH & NÂNG CẤP SPRINT 3.5 — BOOKSHARE PORTAL

> **Phạm vi:** Đóng gói 100% thư viện nội bộ (`wwwroot/vendor/`), sửa tận gốc lỗi mã QR nháy trắng sau 1 giây, thiết kế lại Header 1 hàng cân đối không rớt tầng & không trùng thông báo, khắc phục triệt để lỗi 500 trên CSDL trắng / sau khi reset DB trên VPS Docker, thu gọn nút & pop-up Trợ lý AI, dọn sạch ký tự/chuỗi mẫu thừa, và xây dựng **Tab Admin Quản lý & Tùy biến Biểu tượng Toàn trang có công tắc gạt từng icon (`S22`)**.  
> **Nguyên tắc bắt buộc:** Strict Service Layer (`Controller -> IService -> Service -> BookShareDbContext`), Database-First Scaffold (`--schema dbo`), Light Mode mặc định (`#f8fafc`), 0 từ ngữ kỹ thuật trên UI, 100% phi thương mại chia sẻ sách giấy 0đ.

---

## TIÊU CHÍ NGHIỆM THU (DEFINITION OF DONE)

1. Hoàn tất 3 tệp đặc tả Pha 0 (`SPRINT35_POLISH_PLAN.md`, `ICON_CUSTOMIZATION_SPEC.md`, `LOCAL_ASSETS_AUDIT.md`) trước khi chỉnh sửa mã nguồn và cập nhật tiến độ `[x]` sau mỗi pha.
2. **0 liên kết CDN ngoài** bị phụ thuộc trong `Views/`, `wwwroot/js/`, `wwwroot/modules/`, `wwwroot/css/`.
3. Mã QR đăng nhập (120s / 60s) & QR bàn giao sách (10 phút) hiển thị ổn định liên tục bằng `<canvas>` sở hữu riêng có vùng trắng yên tĩnh (`quiet zone`), không nháy trắng.
4. Thanh Header Desktop (`>= 1200px`) luôn gọn **đúng 1 hàng duy nhất**, không rớt 2 tầng khi đăng nhập Admin/Librarian/Curator, không trùng lặp banner `#systemStatusBox` + SweetAlert2 toast đè Header.
5. Chạy ổn định **0 lỗi HTTP 500** trên cả **CSDL trắng (`01_BookShare_Schema.sql` / `FactoryClean`)** lẫn **CSDL đã nạp dữ liệu mẫu**, tự dọn Cookie cũ khi tài khoản bị xóa sau reset DB, đăng nhập `admin@bookshare.vn` / `Admin@123` thành công ngay lần đầu.
6. Tab Admin **"Tùy Chỉnh Biểu Tượng"** hỗ trợ đầy đủ 18 biểu tượng toàn trang, công tắc gạt từng icon (**Trái = Mặc định**, **Phải = Ảnh tùy chỉnh**), upload an toàn (`.png`/`.jpg`/`.webp`/`.svg` đã lọc <= 2MB) lưu bền vững trong `wwwroot/uploads/icons/`, áp dụng tức thì qua `IMemoryCache`.
7. `dotnet build BookShare.sln --nologo` đạt **0 Error(s)**, `python scripts/qa_full_bot.py --autostart` (L1–L7) đạt **100% PASS (0 FAIL)** và `python scripts/smoke_test.py --autostart` đạt **100% PASS (0 lỗi)**.

---

## BẢNG CHECKLIST TỪNG PHA

| Mã | Hạng mục | Trạng thái | Tệp mục tiêu chính | Cách kiểm chứng |
| :--- | :--- | :---: | :--- | :--- |
| **P0.1** | Tạo kế hoạch & theo dõi tiến độ `docs/SPRINT35_POLISH_PLAN.md` | [x] | `docs/SPRINT35_POLISH_PLAN.md` | Tồn tại trước khi sửa code |
| **P0.2** | Tạo đặc tả Hệ thống Quản lý Biểu tượng `docs/ICON_CUSTOMIZATION_SPEC.md` | [x] | `docs/ICON_CUSTOMIZATION_SPEC.md` | Đầy đủ DDL, 18 icon, bảo mật upload |
| **P0.3** | Tạo bảng kiểm kê thư viện nội bộ `docs/LOCAL_ASSETS_AUDIT.md` | [x] | `docs/LOCAL_ASSETS_AUDIT.md` | Liệt kê đủ thư viện local |
| **P1.1** | Tự host 100% JS/CSS/QR/Placeholder vào `wwwroot/vendor/` & `wwwroot/images/`, gỡ sạch CDN ngoài (`jsdelivr`) | [x] | `wwwroot/vendor/*`, `core-api.js`, `service-worker.js`, `LOCAL_ASSETS_AUDIT.md` | Audit L7: 0 CDN ngoài |
| **P1.2** | Sửa triệt để lỗi QR hiện 1s rồi biến mất (tách `<canvas>` độc lập khỏi `qrcodejs` `img.onload`, khóa chống gọi trùng) | [x] | `wwwroot/js/core-api.js`, `s01-auth-otp-google.js` | QR ổn định suốt TTL 120s/60s/10p |
| **P1.3** | Dọn ký tự/chuỗi rác mẫu & chữ kỹ thuật (`CLEAN STATE`, `02_BookShare_SeedData.sql`, `book-tracker-ver07`, link Unsplash dự phòng) | [x] | `Views/Home/Index.cshtml`, `S03`, `S01`, `core-api.js`, `core-theme.css` | Quét tĩnh 0 chuỗi rác |
| **P2.1** | Thiết kế lại Header 1 hàng cân đối (Desktop `>= 1200px` `nowrap`), gom Dropdown Tài khoản & Bộ chọn chủ đề bên phải, bỏ trùng Toast/Banner | [x] | `Views/Shared/_Layout.cshtml`, `core-theme.css`, `core-api.js` | Kiểm tra responsive & 0 trùng thông báo |
| **P2.2** | Chuẩn hóa trạng thái Khách vs Đã đăng nhập trên `S01` (ẩn tab Đăng nhập/Đăng ký khi đã đăng nhập, kích hoạt ngay Hồ sơ & Bảo mật) | [x] | `Views/Modules/S01_AuthOtpGoogle/Index.cshtml` | L2 & L7 xác minh |
| **P2.3** | Xây dựng `DatabaseBootstrapService` khởi động an toàn + `OnValidatePrincipal` tự xóa Cookie cũ sau reset DB + đồng bộ mật khẩu `Admin@123` + chống 500 trên DB trắng | [x] | `Program.cs`, `DatabaseBootstrapService.cs`, `S01AuthOtpService.cs`, `S22AdminAnalyticsService.cs`, `PortalDiscoveryService.cs`, `docker-compose.yml` | L7 kiểm thử thực tế trên `FactoryClean` |
| **P3.1** | Thu gọn nút nổi Trợ lý AI thành nút tròn `52x52px`, pop-up `350x480px` có nút Mở rộng/Thu gọn/Đóng, tự ẩn tại `/S13GeminiAiAssistant` | [x] | `_Layout.cshtml`, `core-theme.css`, `core-api.js` | Giao diện gọn, không che Bottom Nav |
| **P3.2** | Rà soát đồng bộ trang lỗi ngoại lệ (`400`/`401`/`403`/`404`/`405`/`500`) cả Development & Production Docker | [x] | `Program.cs`, `HomeController.cs`, `Error.cshtml` | L6.8 PASS 100% |
| **P4.1** | Bổ sung bảng `SystemIconSettings` vào `01_BookShare_Schema.sql` & chạy `dotnet ef dbcontext scaffold --schema dbo` | [x] | `database/01_BookShare_Schema.sql`, `Models/Entities/SystemIconSetting.cs`, `BookShareDbContext.cs` | Build 0 Error |
| **P4.2** | Triển khai `ISystemIconService` / `SystemIconService` (`IMemoryCache`, 18 icon, upload an toàn <= 2MB, toggle 2 chiều, reset) | [x] | `ISprint35Services.cs`, `SystemIconService.cs`, `Program.cs` | Build 0 Error |
| **P4.3** | Gắn `@inject ISystemIconService` vào `_Layout.cshtml`, `Home/Index.cshtml`, `S01` và dựng Tab **"Tùy Chỉnh Biểu Tượng"** trong `S22` + `Dockerfile` | [x] | `_Layout.cshtml`, `Home/Index.cshtml`, `S22AdminAnalyticsController.cs`, `S22_AdminAnalytics/*`, `Dockerfile` | L7 RBAC + upload + toggle + reset PASS |
| **P5.1** | Mở rộng `scripts/qa_full_bot.py` Lớp 7 (0 CDN, QR, DB trắng + stale cookie + login Admin, Icon Manager RBAC/Toggle/Upload) | [x] | `scripts/qa_full_bot.py` | 100% PASS (0 FAIL) |
| **P5.2** | Chạy `dotnet build`, `qa_full_bot.py --autostart`, `smoke_test.py --autostart` đến 100% PASS | [x] | Toàn bộ solution | 0 Error / 0 FAIL (`346/346 PASS`) |
| **P6.1** | Đồng bộ tất cả tệp `.md` (`SPRINT35_POLISH_PLAN`, `ICON_CUSTOMIZATION_SPEC`, `LOCAL_ASSETS_AUDIT`, `QA_TEST_REPORT`, `PROGRESS`, `TODO`, `README`) | [x] | `docs/*.md`, `TODO.md`, `README.md` | Số liệu khớp thực tế 100% |

---

## NHẬT KÝ THAY ĐỔI (CHANGE LOG)

| Thời điểm | Pha | Kết quả thực hiện & Kiểm chứng |
| :--- | :--- | :--- |
| 2026-11-24 | Pha 0 | Khởi tạo bộ 3 tài liệu đặc tả Sprint 3.5 (`SPRINT35_POLISH_PLAN.md`, `ICON_CUSTOMIZATION_SPEC.md`, `LOCAL_ASSETS_AUDIT.md`) trước khi chỉnh sửa mã nguồn. |
| 2026-11-24 | Pha 1 | Tự host 100% thư viện (`qrcode.min.js`, `html5-qrcode.min.js`, `confetti.browser.min.js`, `jspdf.umd.min.js`, `html2canvas.min.js`) và ảnh dự phòng (`/images/book-placeholder.svg`, `/images/avatar-default.svg`); sửa triệt để lỗi QR nháy trắng bằng `<canvas>` độc lập có quiet zone 12px; gỡ sạch 100% CDN ngoài & chuỗi mẫu/kỹ thuật thừa. |
| 2026-11-24 | Pha 2 & 3 | Xây dựng `DatabaseBootstrapService` khởi động an toàn + `OnValidatePrincipal` tự xóa Cookie mồ côi khi reset CSDL + đồng bộ mật khẩu `Admin@123`; thiết kế lại Header 1 hàng gọn cân đối (Desktop `>= 1200px` `nowrap`), gom Dropdown Tài khoản & Chủ đề bên phải, ẩn `#systemStatusBox` mặc định khi không có flash & tắt lặp Toast/Banner; tách rạch ròi trạng thái `S01` (ẩn tab Đăng nhập/Đăng ký khi đã đăng nhập); thu gọn nút nổi Trợ lý AI `52x52px` + pop-up `350x460px` có nút Mở rộng/Thu gọn/Đóng. |
| 2026-11-24 | Pha 4 | Thêm bảng `SystemIconSettings` vào `01_BookShare_Schema.sql` & scaffold EF Core (`--schema dbo`); triển khai `SystemIconService` (`IMemoryCache`, 18 biểu tượng, kiểm tra magic bytes & lọc SVG XSS <= 2MB), 4 API `[Authorize(Roles="Admin")]` tại `S22AdminAnalyticsController`, Tab **"Tùy Chỉnh Biểu Tượng"** có công tắc gạt 2 chiều từng icon và đồng bộ `Dockerfile` + `.gitignore`. |
| 2026-11-24 | Pha 5 | Mở rộng `scripts/qa_full_bot.py` thêm **Lớp 7 (30 checks)** kiểm chứng 0 CDN ngoài, ổn định QR, Header 1 hàng + S01 auth state, Admin Icon Manager (RBAC/upload/XSS/toggle 2 chiều/reset) và kịch bản **CSDL trắng (`FactoryClean`) + Cookie mồ côi + đăng nhập `Admin@123`**. Kết quả: `dotnet build` **0 Error(s)**, `qa_full_bot.py` **345/345 PASS (0 FAIL)**, `smoke_test.py` **100% PASS (0 lỗi)**. |
| 2026-11-24 | Pha 6 | Đồng bộ đầy đủ 7 tệp tài liệu `.md` (`SPRINT35_POLISH_PLAN`, `ICON_CUSTOMIZATION_SPEC`, `LOCAL_ASSETS_AUDIT`, `QA_TEST_REPORT`, `PROGRESS`, `TODO`, `README`) và kiểm tra xác minh lần cuối trước bàn giao. |
| 2026-11-24 | Hotfix VPS | Phát hiện & xử lý tận gốc lỗi lệch schema Docker (`Invalid column name 'DailyRentalFee'/'DepositAmount'`): dựng lại LocalDB sạch 100% từ `01_BookShare_Schema.sql`, re-scaffold EF Core loại bỏ 5 cột thừa khỏi `BookCopy.cs`, `Loan.cs`, `BookShareDbContext.cs`, thêm chốt chặn L7 chống lệch schema (`346/346 PASS`). |
