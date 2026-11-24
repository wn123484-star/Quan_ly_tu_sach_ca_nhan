# TODO — Kế Hoạch Tái Cấu Trúc, Khóa Bảo Mật & Kiểm Thử E2E BookShare

Theo dõi tiến độ thực thi 7 pha chuẩn hóa hệ thống BookShare (.NET 10 ASP.NET Core MVC + EF Core SQL Server).

---

## PHA 1: Xác thực (`S01`), Loại bỏ Tài khoản Mẫu khỏi UI & Khóa Cứng Google OAuth Thật
- [x] Xóa hoàn toàn `DemoAccounts` và nút điền nhanh tài khoản mẫu khỏi `S01_AuthOtpGoogle/Index.cshtml` và `s01-auth-otp-google.js`; thay bằng giới thiệu giá trị cộng đồng không chứa thuật ngữ kỹ thuật.
- [x] Dọn tab mồ côi `#tabOtpResend` và ẩn tab Hồ sơ khi chưa đăng nhập; chuyển bảng 6 tài khoản mẫu 4 vai trò sang `README.md`.
- [x] Xóa nhánh tự đăng nhập Sandbox `nguyenhung@cmc.edu.vn` trong `S01AuthOtpGoogleController.GoogleLogin`; kiểm tra khóa thật qua helper `IsGoogleOAuthConfigured`, trả về HTTP 400 và vô hiệu hóa nút Google trên UI khi chưa cấu hình `.env`.
- [x] Vá `EnsureCoreAdminBootstrapAsync` (idempotent, tuyệt đối không tự `DELETE` dữ liệu seed), khóa `RegisterUserAsync` luôn gán role `"User"`, xóa dropdown `SelectedRole` khỏi form đăng ký.
- [x] Thực thi kiểm tra `LockoutEnd > DateTimeOffset.UtcNow` trên cả đăng nhập mật khẩu và `UpsertGoogleLoginAsync`, kèm `SignOutAsync` ngay khi upsert lỗi hoặc tài khoản bị khóa.
- [x] Xóa backdoor mật khẩu cũ trong `AuthenticateAsync`, đồng bộ hash chuẩn Identity (`Admin@123`, `Librarian@123`, `Curator@123`, `User@123`), gắn `[Authorize]` cho `UpdateProfile` và `ChangePassword`.

## PHA 2: Khóa Chặt Phân Quyền RBAC, `[Authorize]` & Chống IDOR Toàn Bộ 22 Phân Hệ
- [x] Xóa sạch fallback User ID giả (`?? "u-user-01"`, `?? "u-lib-01"`, `?? "u-admin-01"`, ...) trên tất cả Controller và Service; chuẩn hóa ID thủ thư `"u-librarian-01"`.
- [x] Bổ sung `[Authorize]` cho mọi action ghi dữ liệu và cấu hình `Program.cs` trả về `401 Unauthorized` / `403 Forbidden` dạng JSON cho request API/Ajax.
- [x] Khóa `[Authorize(Roles = "...")]` cho các API đặc quyền (`S05`, `S07`, `S08`, `S11`, `S12`, `S17`, `S18`, `S19`, `S20`, `S21`, `S22`) và trả `403` khi `User` cố tạo bản sao `TargetOwnerType = "System"` ở `S05`.
- [x] Bổ sung kiểm tra chống IDOR theo quyền sở hữu ở tầng Service (`S02`, `S03`, `S04`, `S09`, `S10`, `S11`, `S12`, `S13`, `S15`, `S17`, `S18`, `S19`, `S20`), đồng thời giới hạn danh sách `S12LoanRenewalFine` theo người mượn/chủ sách nếu không phải Thủ thư/Admin.

## PHA 3: Khép Kín 100% Chuỗi Logic Liên Kết Đa Phân Hệ
- [x] Hợp nhất kiểm tra điều kiện & hạn mức mượn (`ILoanFlowCoordinatorService`) cho `S06`, `S08`, `S09`, `S21`: `IsLendable == true`, `Status == "Available"`, `TrustScore >= 50`, không quá hạn, không tranh chấp mở, và kiểm tra `MaxConcurrentLoans + LendQuotaBonus`.
- [x] Chống duyệt trùng P2P trên cùng bản sao tại `S09`; đồng bộ `Loan` -> `ConditionReports` (`S10`) -> hoàn trả `S08`/`S09`/`S21` tự động đôn hàng chờ `S11` và gắn nút đánh giá `S15`.
- [x] Liên thông `S16 <-> S17` (`SwitchMembership` tạo giao dịch `Pending` `S17` kèm `?txId=...`, xác thực chữ ký `HMAC-SHA512` `FixedTimeEquals` và chống phát lại idempotent) và đồng bộ thanh toán phạt `S12` qua `S17`.
- [x] Khóa mượn mới khi `Loan` bị tranh chấp `S19` và mở khóa khi `Admin` phân xử; bảo vệ cộng thưởng kiểm định 1 lần/bản sao tại `S20`; xác thực `EmailConfirmed` qua OTP `S01/S18`; bổ sung kiểm tra tồn tại `LoanId` ở `S15`.
- [x] Khắc phục triệt để `S22AdminAnalyticsService.ManageSampleDataAsync` xử lý an toàn khóa ngoại các bảng bổ sung (`SupportChatMessages`, `QrLoginSessions`, `ReadingSessions`, `UserWallets`) khi reset dữ liệu mẫu.

## PHA 4: Giao Diện Mặc Định Light Mode, CSS & Loại Bỏ Tệp Dư Thừa
- [x] Xóa `site.css`, `site.js`, `_Layout.cshtml.css`; khóa `data-bs-theme="light"` trên `<html>` trong `_Layout.cshtml`, bỏ tự động chuyển Dark Mode theo `prefers-color-scheme` trong `core-api.js`.
- [x] Chuyển màn hình splash `.app-splash`, `manifest.json` (`#f8fafc`), và card offline trong `service-worker.js` (`bookshare-static-v2`) sang nền sáng.
- [x] Chuẩn hóa biến màu `--portal-*` và tương phản Light/Dark trên các CSS phân hệ `wwwroot/modules/s01..s22/*.css`.

## PHA 5: Rà Soát Ngôn Từ Chuyên Nghiệp & Ẩn Thao Tác Phân Quyền Trên UI
- [x] Loại bỏ toàn bộ thuật ngữ kỹ thuật nội bộ (`CRUD`, `BookShareDb`, `SQL Server`, `EF Core`, `Cookie Auth`, `HttpOnly`, `SHA-256`, `IDOR`, `Rubric`, `Ver07`, `S01`–`S22`) khỏi UI Razor và thông báo trả về người dùng.
- [x] Giới hạn hiển thị nút thao tác đặc quyền trên giao diện (`S05`, `S11`, `S12`, `S18`, `S19`, `S20`, `S21`, `S22`) theo vai trò thực tế, tránh lộ PII cho Guest/`User`.
- [x] Đảm bảo 100% thẻ `<img>` đều có `alt` và `onerror` fallback; làm sạch mô tả `.env.example`.

## PHA 6: Xây Dựng Bot Kiểm Thử E2E Toàn Diện & Khắc Phục Sạch Lỗi
- [x] Xây dựng `scripts/qa_full_bot.py` kiểm thử tự động 5 tầng (`L1`–`L5`) đối chiếu trực tiếp server ASP.NET Core 10 + LocalDB thật.
- [x] Rà soát và chạy thông suốt `scripts/qa_full_bot.py --autostart` đạt **277/277 PASS (0 FAIL)**.
- [x] Chạy kiểm thử khói `scripts/smoke_test.py --autostart` đạt **100% PASS (0 lỗi)**.

## PHA 7: Hoàn Thiện Tài Liệu Bàn Giao & Kiểm Tra Lần Cuối
- [x] Cập nhật `README.md` (bảng 6 tài khoản mẫu 4 vai trò, hướng dẫn cài đặt & kiểm thử), `TODO.md` và `docs/PROGRESS.md`.
- [x] Tạo mới `docs/SECURITY_RBAC.md`, `docs/API_REFERENCE.md`, `docs/DEPLOYMENT_OAUTH_GUIDE.md` và `docs/QA_TEST_REPORT.md`.
- [x] Thực hiện biên dịch lần cuối `dotnet build BookShare.sln --nologo` (`0 Error(s)`) và chạy toàn bộ bộ kiểm thử `scripts/qa_full_bot.py --autostart` xác nhận **100% PASS**.

---

## SPRINT 3: Nâng Cấp Toàn Diện Nghiệp Vụ, AI Copilot, QR 2 Chiều & Android App (`mobile/bookshare-android/`)
- [x] **Pha 0:** Lập bộ 4 tài liệu đặc tả & theo dõi tiến độ trước khi lập trình (`docs/UPGRADE_SPRINT3_PLAN.md`, `docs/QR_AUTH_AND_HANDOVER_SPEC.md`, `docs/AI_COPILOT_TOOLS_SPEC.md`, `docs/MOBILE_APP_ARCHITECTURE.md`).
- [x] **Pha 1:** Mở rộng SQL Schema (`QrLoginSessions`, `BookHandoverQrs`, `RoleUpgradeRequests`), chạy `dotnet ef dbcontext scaffold --schema dbo`, khép kín bồi thường tranh chấp `S19 <-> S17`, QR bàn giao/hoàn trả 1 lần dùng (`/api/handover-qr/*`), luồng đề nghị nâng quyền `Curator` (`S01 -> S22`), đóng gói thư viện `/lib/qr/` tự host và tách `02_BookShare_SeedData.sql` khỏi `db-init` Docker mặc định.
- [x] **Pha 2:** Nâng cấp AI Copilot (`S13`, `S14` & Widget nổi) timeout `20s`, hội thoại Multi-turn `8` tin nhắn, Function Calling 2 tầng (`clientCommands` + `actionCards` xác nhận 1 chạm chống XSS) và khóa RBAC/Anti-IDOR trên `POST /S13GeminiAiAssistant/ExecuteCopilotAction`.
- [x] **Pha 3:** Xây dựng Đăng nhập QR 2 chiều (`WebLoginByMobile` 120s & `MobileLoginByWeb` 60s, token 32 byte lưu SHA-256 hash, rate limit 20/phút, atomic `Consumed`, kiểm tra `LockoutEnd`) cùng Máy quét QR đa năng `openUniversalScanner()`.
- [x] **Pha 4:** Xây dựng Mobile Companion API (`/api/mobile/notifications/unread`, `/api/mobile/session`), `Program.cs` trả `401`/`403` JSON cho `/api/*`, nhận diện `BookShareAndroidApp` trong `_Layout.cshtml` (`is-mobile-app`) và Bottom Navigation Bar 5 mục nền sáng.
- [x] **Pha 5:** Viết sạch mã nguồn Android Native WebView Shell tại `mobile/bookshare-android/` (`BuildConfig.BASE_URL` không hộp thoại hỏi URL, nền `#F8FAFC` `NoActionBar`, Custom Tabs Google OAuth `bookshare://open`, Camera/FileChooser, `NotificationPoller` gắn `CookieManager`) và workflow GitHub Actions `.github/workflows/build-bookshare-android-apk.yml`.
- [x] **Pha 6–7:** Mở rộng `scripts/qa_full_bot.py` Lớp 6 đạt **315/315 PASS (0 FAIL)**, `scripts/smoke_test.py` **100% PASS**, và đồng bộ toàn bộ tài liệu `.md`.

---

## SPRINT 3.5: Tự Host 100% Tài Nguyên, Vá Triệt Để QR/Header/DB Trắng & Tab Admin Quản Lý Biểu Tượng
- [x] **Pha 0:** Tạo 3 tệp đặc tả trước khi chỉnh sửa mã nguồn (`docs/SPRINT35_POLISH_PLAN.md`, `docs/ICON_CUSTOMIZATION_SPEC.md`, `docs/LOCAL_ASSETS_AUDIT.md`).
- [x] **Pha 1:** Đóng gói 100% thư viện cục bộ vào `wwwroot/vendor/` & ảnh dự phòng SVG vào `wwwroot/images/` (0 CDN ngoài, 0 Unsplash fallback, 0 chuỗi mẫu/kỹ thuật); sửa tận gốc lỗi QR nháy trắng sau 1 giây bằng `<canvas>` sở hữu riêng có quiet zone 12px.
- [x] **Pha 2:** Thiết kế lại Header 1 hàng gọn cân đối (`>= 1200px` `nowrap`), gom Dropdown Tài khoản & Chủ đề bên phải, ẩn `#systemStatusBox` mặc định & bỏ trùng Toast/Banner; tách rạch ròi trạng thái `S01`; xây dựng `DatabaseBootstrapService` + `OnValidatePrincipal` tự hủy Cookie mồ côi khi reset CSDL + đồng bộ mật khẩu `Admin@123`.
- [x] **Pha 3:** Thu gọn nút nổi Trợ lý AI thành nút tròn `52x52px`, pop-up `350x460px` có nút Mở rộng/Thu gọn/Đóng, tự ẩn trên `/S13GeminiAiAssistant`; đồng bộ trang lỗi thân thiện (`400`–`500`) cả Dev & Production Docker.
- [x] **Pha 4:** Bổ sung bảng `SystemIconSettings` (28 bảng `dbo`) + EF Core Scaffold, triển khai `SystemIconService` (`IMemoryCache`, 18 biểu tượng, kiểm tra magic bytes & chống SVG XSS <= 2MB), 4 API `[Authorize(Roles="Admin")]` tại `S22` và giao diện Tab **"Tùy Chỉnh Biểu Tượng"** có công tắc gạt 2 chiều từng icon.
- [x] **Pha 5–6:** Mở rộng `scripts/qa_full_bot.py` Lớp 7 (kèm chốt chặn chống lệch schema Docker) đạt **346/346 PASS (0 FAIL)**, `scripts/smoke_test.py` **100% PASS (0 lỗi)**, và đồng bộ toàn bộ tài liệu `.md`.

---

## SPRINT 4: Ví Tiền Ảo Miễn Phí, Gói Hội Viên 4 Cấp, Quét QR Thanh Toán & Admin Xóa Tài Khoản
- [x] **Pha 0:** Tạo bộ 3 tài liệu đặc tả & theo dõi tiến độ trước khi sửa code (`docs/SPRINT4_WALLET_MEMBERSHIP_PLAN.md`, `docs/VIRTUAL_WALLET_AND_QR_PAY_SPEC.md`, `docs/ADMIN_USER_DELETE_SPEC.md`).
- [x] **Pha 1:** Xóa sạch cấu hình ngân hàng thật `VNPay__*` khỏi `.env.example`, bổ sung bảng `dbo.UserWallets` & nâng cấp 4 cấp `Memberships` + cột hóa đơn `Transactions` trong `01_BookShare_Schema.sql` + `DatabaseBootstrapService`, chạy lại EF Core Scaffold (`--schema dbo`, 0 lệch schema).
- [x] **Pha 2–3:** Xây dựng `MembershipTierCatalog` (4 cấp `Free`/`Standard`/`Premium`/`Scholar` thực thi thật ở `LoanFlowCoordinatorService`, `S08`, `S09`, `S11`, `S12`), trang Nạp Tiền Ảo miễn phí thoải mái (`/S17SandboxPayment`), bảng so sánh & nâng cấp 4 cấp (`/S16MembershipTier`), và luồng **Quét QR Thanh Toán** đồng bộ Web ↔ Android Web App (mở camera -> xem hóa đơn & số dư Ví -> bấm Xác nhận thanh toán -> polling tự cập nhật).
- [x] **Pha 4:** Thêm tính năng **Admin Xóa Tài Khoản** (`POST /S22AdminAnalytics/DeleteUser`) có chốt bảo vệ sách đang mượn/tranh chấp mở/tài khoản gốc và xóa nguyên tử khóa ngoại.
- [x] **Pha 5–6:** Mở rộng `scripts/qa_full_bot.py` Lớp 8 đạt **368/368 PASS (0 FAIL)**, `scripts/smoke_test.py` đạt **100% PASS (0 lỗi)** và đồng bộ toàn bộ tài liệu `.md`.


---

## SPRINT 5: Vá Lỗi Icon 404 & Google OAuth, Đa Giao Diện Bootstrap/Font Cục Bộ, Cài Đặt Email Mọi Cấp, Popup Thông Báo, AI Chat Bền Vững & Chống Spam Hội Viên
- [x] **Pha 0:** Tạo bộ 3 tài liệu đặc tả & theo dõi tiến độ trước khi sửa mã nguồn (`docs/SPRINT5_FIXES_AND_UX_PLAN.md`, `docs/THEME_TYPOGRAPHY_AND_SETTINGS_SPEC.md`, `docs/MEMBERSHIP_SWITCH_AND_ANTISPAM_SPEC.md`).
- [x] **Pha 1:** Tải cục bộ 4 bộ font tiếng Việt (`Be Vietnam Pro`, `Lora`, `Patrick Hand`, `JetBrains Mono`) vào `wwwroot/fonts/`, gỡ `@import` CDN khỏi `wwwroot/vendor/bootswatch/*.min.css`, nâng cấp `01_BookShare_Schema.sql` + `DatabaseBootstrapService` + chạy lại EF Core Scaffold (`--schema dbo`).
- [x] **Pha 2:** Vá triệt để lỗi 404 ảnh biểu tượng tải lên (`app.UseStaticFiles()` + lưu nhị phân SQL Server + tự phục hồi đĩa + `/assets/system-icons/{iconKey}`) và lỗi đăng nhập Google OAuth 2.0 (tách `BookShare.External` Cookie Scheme + DataProtection + chuẩn hóa dấu ngoặc `.env`).
- [x] **Pha 3:** Nâng cấp `S16` & `S17`: chặn mua trùng gói đang sở hữu (`400`), cơ chế Chuyển Gói minh bạch lệ phí `15.000đ` + khấu trừ ngày dư (`PreviewTierSwitch`), nút Hủy Gói (`CancelMembership`), khóa `IdempotencyKey`, gộp hóa đơn `Pending`, chống spam nạp ví `429` và minh bạch `BalanceBefore -> BalanceAfter`.
- [x] **Pha 4–5:** Xây dựng Cài đặt tài khoản mọi cấp phân tách rạch ròi `UserName`/`FullName`/`Email` + chốt bắt buộc Email khi gửi thư, Popup Thông Báo nhanh (`RecentPopup`), lịch sử Chat AI bền vững theo phân quyền + nút **Reset Chat** + gửi chat qua Email + chọn Style/Màu/Font/Cỡ chữ trong chat, cân bằng Header `180deg`, 5 phong cách Bootstrap (gồm Sketchy nghệch ngoặc) + 10 bảng màu (gồm Vàng Mộng Mơ, Hồng Ấm Áp) và thu gọn reCAPTCHA về góc trái dưới không đè AI Chatbot.
- [x] **Pha 6:** Bổ sung Lớp 9 vào `scripts/qa_full_bot.py` đạt **395/395 PASS (0 FAIL)**, `scripts/smoke_test.py` đạt **100% PASS (0 lỗi)** và đồng bộ toàn bộ tài liệu `.md`.
