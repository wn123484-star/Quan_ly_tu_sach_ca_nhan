# BẢNG THEO DÕI TIẾN ĐỘ DỰ ÁN BOOKSHARE (PROGRESS.md)

Dự án **BookShare (.NET 10 MVC)** được triển khai qua **5 giai đoạn phát triển nền tảng** và **giai đoạn 6–7 kiểm toán bảo mật, tối ưu giao diện Light Mode, kiểm thử E2E & hoàn thiện tài liệu**.

---

## Giai đoạn 1: Khóa Kiến trúc & Thiết kế CSDL 22 Bảng (Hoàn thành 100%)
- [x] Thiết lập solution `BookShare.sln`, `.gitignore`, `.env.example`, cấu hình `DotNetEnv`.
- [x] Tài liệu kiến trúc [`docs/ARCHITECTURE.md`](ARCHITECTURE.md) và quy chuẩn kỹ thuật [`docs/RULES.md`](RULES.md).
- [x] Kịch bản [`database/01_BookShare_Schema.sql`](../database/01_BookShare_Schema.sql) (22 bảng nghiệp vụ + 3 bảng Identity) và [`database/02_BookShare_SeedData.sql`](../database/02_BookShare_SeedData.sql) (4 vai trò, 6 tài khoản, 3 trạm/thư viện, 35 đầu sách, 55 bản sao vật lý).

## Giai đoạn 2–5: Xây dựng Khung Core & Đủ 22 Phân hệ Độc lập S01 → S22 (Hoàn thành 100%)
- [x] Triển khai đầy đủ 23 Controller (`HomeController` + `S01` → `S22`), Interfaces (`IS01` → `IS22`, `ILoanFlowCoordinatorService`, `IRecaptchaService`, `IResendEmailService`, `IPortalDiscoveryService`), và Services tuân thủ nghiêm Strict Service Layer.
- [x] Hoàn thiện 22 bộ file `Views/Modules/Sxx_*/Index.cshtml` + `wwwroot/modules/sxx-*/*.{css,js}` dùng `apiFetch()` bọc CSRF Token và `#systemStatusBox`.
- [x] Tích hợp Hangfire Jobs (`BookShareDailyJobs`), Google Gemini AI RAG (`S13`, `S14`), VNPay Sandbox `HMAC-SHA512` (`S16`, `S17`), Resend Email OTP (`S01`, `S18`), và đóng gói Docker (`Dockerfile`, `docker-compose.yml`).

## Giai đoạn 6: Kiểm Toán Bảo Mật, Khép Kín Nghiệp Vụ, Light Mode & Bot Kiểm Thử E2E (Hoàn thành 100%)
- [x] **Xác thực & Google OAuth (`S01`):** Gỡ bỏ tài khoản mẫu & nút điền nhanh khỏi UI/JS; khóa Google OAuth fail-closed (`HTTP 400` + vô hiệu hóa nút khi chưa có key thật trong `.env`); gỡ nhánh backdoor mật khẩu; kiểm tra `LockoutEnd` trên cả mật khẩu và OAuth; đăng ký công khai luôn gán role `"User"`.
- [x] **RBAC & Anti-IDOR Toàn Hệ Thống:** Xóa toàn bộ User ID fallback mặc định; khóa `[Authorize]` / `[Authorize(Roles = "...")]` trên mọi endpoint ghi và đặc quyền; bổ sung kiểm tra quyền sở hữu ở Service layer cho `S02`–`S20`; giới hạn dữ liệu trang `S12` theo người dùng nếu không phải Thủ thư/Admin.
- [x] **Liên Thông Nghiệp Vụ:** Hợp nhất luật kiểm tra điều kiện mượn (`ILoanFlowCoordinatorService`); vá lỗi quét barcode `S21` bỏ qua `LoanRequest` cũ đã có `Loan`; nối `S16 <-> S17` thanh toán & hoàn trả phí phạt; khóa mượn khi có tranh chấp mở `S19`; bảo vệ cộng thưởng 1 lần/bản sao tại `S20`; kiểm tra tồn tại `LoanId` ở `S15`; xử lý đầy đủ khóa ngoại trong `S22AdminAnalyticsService.ManageSampleDataAsync`.
- [x] **Light Mode Mặc Định & Ngôn Ngữ Chuyên Nghiệp:** Chuẩn hóa `data-bs-theme="light"`, nền sáng cho splash screen / `manifest.json` / `service-worker.js`, xóa `site.css` / `site.js` / `_Layout.cshtml.css`, dọn thuật ngữ kỹ thuật trên Razor UI & thông báo JSON, ẩn nút đặc quyền theo Role, kiểm tra `alt` + `onerror` trên 100% ảnh.
- [x] **Kiểm Thử Tự Động Khách Quan:** Chạy `scripts/qa_full_bot.py --autostart` đạt **277/277 PASS (0 FAIL)** (`L1=8`, `L2=141`, `L3=59`, `L4=66`, `L5=3`) và `scripts/smoke_test.py --autostart` đạt **100% PASS (0 lỗi)**.

## Giai đoạn 7: Hoàn Thiện Tài Liệu Bàn Giao (Hoàn thành 100%)
- [x] Cập nhật [`../README.md`](../README.md), [`../TODO.md`](../TODO.md), [`PROGRESS.md`](PROGRESS.md).
- [x] Tạo bổ sung [`SECURITY_RBAC.md`](SECURITY_RBAC.md), [`API_REFERENCE.md`](API_REFERENCE.md), [`DEPLOYMENT_OAUTH_GUIDE.md`](DEPLOYMENT_OAUTH_GUIDE.md) và [`QA_TEST_REPORT.md`](QA_TEST_REPORT.md).

## Sprint 3: Khép Kín 5 Nghiệp Vụ, AI Copilot 2 Tầng, Đăng Nhập QR 2 Chiều & Android WebView Shell (Hoàn thành 100%)
- [x] Lập đủ 4 tài liệu đặc tả Sprint 3 trong `docs/` ([`UPGRADE_SPRINT3_PLAN.md`](UPGRADE_SPRINT3_PLAN.md), [`QR_AUTH_AND_HANDOVER_SPEC.md`](QR_AUTH_AND_HANDOVER_SPEC.md), [`AI_COPILOT_TOOLS_SPEC.md`](AI_COPILOT_TOOLS_SPEC.md), [`MOBILE_APP_ARCHITECTURE.md`](MOBILE_APP_ARCHITECTURE.md)).
- [x] Mở rộng 3 bảng Sprint 3 (`QrLoginSessions`, `BookHandoverQrs`, `RoleUpgradeRequests`), khép kín bồi thường tranh chấp `S19 <-> S17`, QR bàn giao sách `/api/handover-qr/*`, nâng quyền `Curator` (`S01 -> S22`), thư viện `/lib/qr/` tự host và tách `02_BookShare_SeedData.sql` khỏi `db-init` Docker.
- [x] Nâng cấp AI Copilot (`S13`, `S14` & Widget) hỗ trợ Multi-turn 8 tin nhắn, timeout 20s, 2 tầng lệnh `clientCommands` & `actionCards` cùng `ExecuteCopilotAction` khóa RBAC/Anti-IDOR.
- [x] Xây dựng Đăng nhập QR 2 chiều (`/api/qr-login/*`), Mobile Companion API (`/api/mobile/*`), Bottom Nav `#mobileBottomNav`, Android Shell `mobile/bookshare-android/` & workflow GitHub Actions đóng gói `BookShare-Mobile-Android.apk`.
- [x] Hoàn thiện hệ thống Bắt ngoại lệ Toàn cục (`UseExceptionHandler`, `UseStatusCodePagesWithReExecute` cho cả Development & Production), bảo vệ request API/AJAX trả JSON `400`/`401`/`403`/`404`/`405`/`500`, trang báo lỗi [`Views/Shared/Error.cshtml`](../src/BookShare.Web/Views/Shared/Error.cshtml) nền sáng kèm gợi ý đường dẫn 404 thông minh (không ảnh hưởng file tĩnh `/uploads/`, `/css/`, `/js/`).
- [x] Kiểm thử tự động 6 tầng `scripts/qa_full_bot.py --autostart` đạt **315/315 PASS (0 FAIL)** (`L1=8, L2=141, L3=59, L4=66, L6=38, L5=3`) và `scripts/smoke_test.py --autostart` đạt **100% PASS (0 lỗi)**.

## Sprint 3.5: Đóng Gói 100% Tài Nguyên Nội Bộ, Sửa Lỗi QR/Header/DB Trắng & Tab Admin Quản Lý Biểu Tượng (Hoàn thành 100%)
- [x] Lập đủ 3 tài liệu đặc tả Sprint 3.5 trước khi lập trình ([`SPRINT35_POLISH_PLAN.md`](SPRINT35_POLISH_PLAN.md), [`ICON_CUSTOMIZATION_SPEC.md`](ICON_CUSTOMIZATION_SPEC.md), [`LOCAL_ASSETS_AUDIT.md`](LOCAL_ASSETS_AUDIT.md)).
- [x] Đóng gói 100% thư viện cục bộ (`wwwroot/vendor/qr/`, `confetti/`, `jspdf/`, `html2canvas/`) và ảnh dự phòng (`/images/book-placeholder.svg`, `/images/avatar-default.svg`) — 0 liên kết CDN ngoài & 0 chuỗi rác kỹ thuật; sửa triệt để lỗi QR nháy trắng bằng `<canvas>` độc lập có quiet zone 12px.
- [x] Thiết kế lại Header 1 hàng gọn cân đối trên Desktop (`>= 1200px` `nowrap`), gom Dropdown Tài khoản & Chủ đề bên phải, ẩn `#systemStatusBox` mặc định khi không có flash & tắt trùng Toast/Banner; tách rạch ròi trạng thái `S01` (ẩn tab Đăng nhập/Đăng ký khi đã đăng nhập); thu gọn Trợ lý AI nổi thành nút tròn `52x52px` + pop-up `350x460px` tự ẩn trên `/S13GeminiAiAssistant`.
- [x] Xây dựng `DatabaseBootstrapService` chạy ngay khi khởi động + sau mọi thao tác reset CSDL, thêm `OnValidatePrincipal` tự hủy Cookie mồ côi khi reset DB trên VPS Docker, đồng bộ mật khẩu `Admin@123` và bổ sung dự phòng `sqlcmd` trong `docker-compose.yml`.
- [x] Mở rộng bảng `SystemIconSettings` (28 bảng `dbo` chuẩn EF Scaffold), xây dựng `ISystemIconService` (`IMemoryCache`, 18 biểu tượng, kiểm tra magic bytes & lọc SVG XSS <= 2MB) và Tab **"Tùy Chỉnh Biểu Tượng"** có công tắc gạt 2 chiều từng icon tại `/S22AdminAnalytics`.
- [x] Kiểm thử tự động 7 tầng `scripts/qa_full_bot.py --autostart` đạt **346/346 PASS (0 FAIL)** (`L1=8, L2=141, L3=59, L4=66, L6=38, L7=31, L5=3`) và `scripts/smoke_test.py --autostart` đạt **100% PASS (0 lỗi)** trên CSDL dựng sạch 100% từ `01_BookShare_Schema.sql` (đã loại sạch 5 cột thừa lệch schema Docker).

## Sprint 4: Ví Tiền Ảo Miễn Phí, Gói Hội Viên 4 Cấp, Quét QR Thanh Toán & Admin Xóa Tài Khoản (Hoàn thành 100%)
- [x] Lập đủ 3 tài liệu đặc tả Sprint 4 trước khi lập trình ([`SPRINT4_WALLET_MEMBERSHIP_PLAN.md`](SPRINT4_WALLET_MEMBERSHIP_PLAN.md), [`VIRTUAL_WALLET_AND_QR_PAY_SPEC.md`](VIRTUAL_WALLET_AND_QR_PAY_SPEC.md), [`ADMIN_USER_DELETE_SPEC.md`](ADMIN_USER_DELETE_SPEC.md)).
- [x] Gỡ bỏ toàn bộ cấu hình ngân hàng thật `VNPay__*` khỏi `.env.example` và `S17SandboxPaymentService.cs`; bổ sung bảng `dbo.UserWallets`, mở rộng 4 cấp `Memberships` (`Free`, `Standard`, `Premium`, `Scholar`) và cột hóa đơn trên `Transactions` (32 bảng `dbo` chuẩn EF Scaffold).
- [x] Triển khai bộ chính sách `MembershipTierCatalog` thực thi luật mượn thật ở Backend (`MaxConcurrentLoans` `2`/`3`/`5`/`10` cuốn, `MaxBorrowDays` `14`/`21`/`30`/`45` ngày, phạm vi sách hot cho gói `Free`, xếp hạng ưu tiên 4 bậc ở `S11`, hạn mức gia hạn ở `S12`).
- [x] Xây dựng **Ví Tiền Ảo miễn phí** (`TopUpVirtualWallet`) và luồng **Quét QR Thanh Toán giống ngân hàng** (`PreviewQrInvoice`, `ConfirmQrInvoice`, `InvoiceStatus` polling) đồng bộ hoàn chỉnh giữa Web và Android Web App (`#btnMobileCenterQr`, `openQrPaymentScanner`).
- [x] Bổ sung chức năng **Admin Xóa Tài Khoản** (`POST /S22AdminAnalytics/DeleteUser`) có chốt chặn bảo vệ tài khoản gốc, sách đang mượn, tranh chấp mở và dọn sạch khóa ngoại nguyên tử.
- [x] Kiểm thử tự động 8 tầng `scripts/qa_full_bot.py --autostart` đạt **368/368 PASS (0 FAIL)** (`L1=8, L2=141, L3=59, L4=66, L6=38, L7=31, L8=22, L5=3`) và `scripts/smoke_test.py --autostart` đạt **100% PASS (0 lỗi)**.


## Sprint 5: Vá Lỗi 404 Icon & Google OAuth, Đa Giao Diện Bootstrap/Font Cục Bộ, Cài Đặt Mọi Cấp, Popup Thông Báo, AI Bền Vững & Chống Spam Hội Viên (Hoàn thành 100%)
- [x] Lập đủ 3 tài liệu đặc tả Sprint 5 trước khi sửa mã nguồn ([`SPRINT5_FIXES_AND_UX_PLAN.md`](SPRINT5_FIXES_AND_UX_PLAN.md), [`THEME_TYPOGRAPHY_AND_SETTINGS_SPEC.md`](THEME_TYPOGRAPHY_AND_SETTINGS_SPEC.md), [`MEMBERSHIP_SWITCH_AND_ANTISPAM_SPEC.md`](MEMBERSHIP_SWITCH_AND_ANTISPAM_SPEC.md)).
- [x] Vá triệt để lỗi 404 biểu tượng tải lên bằng `app.UseStaticFiles()` + lưu nhị phân `CustomImageData` trong SQL Server + tự khôi phục đĩa & endpoint dự phòng `/assets/system-icons/{iconKey}`.
- [x] Vá triệt để lỗi Google OAuth 2.0 (`MÃ 400`) bằng External Cookie Scheme riêng `BookShare.External` (không bị `OnValidatePrincipal` cắt phiên), lưu bền vững DataProtection keys và chuẩn hóa dấu ngoặc `.env`.
- [x] Tải cục bộ 4 bộ font tiếng Việt (`Be Vietnam Pro`, `Lora`, `Patrick Hand`, `JetBrains Mono`), gỡ sạch `@import` CDN khỏi 4 tệp Bootswatch, triển khai 5 phong cách Bootstrap (gồm **Nét vẽ nghệch ngoặc Sketchy**), 10 bảng màu (gồm **Vàng Mộng Mơ**, **Hồng Ấm Áp**, Tím Oải Hương, Giấy Cổ), 4 cỡ chữ, cân bằng đổ màu Header `180deg` và thu gọn reCAPTCHA về góc trái dưới không đè AI Chatbot.
- [x] Nâng cấp Cài Đặt Tài Khoản mọi cấp (tách bạch `UserName`, `FullName`, `Email` + lưu `UiPreferencesJson` + chốt bắt buộc có Email hợp lệ khi gửi thư), Popup Thông Báo nhanh tại chuông Header (`RecentPopup`), lịch sử Chat AI bền vững theo tài khoản/phân quyền kèm nút **Reset Chat** & **Gửi chat qua Email**.
- [x] Nâng cấp `S16` & `S17`: chặn mua trùng gói đang sở hữu, cơ chế **Chuyển Gói** minh bạch lệ phí `15.000đ` + khấu trừ ngày dư, **nút Hủy Gói**, khóa `IdempotencyKey` (`UNIQUE INDEX`), chống spam nạp ví `429` & gộp hóa đơn `Pending` trùng lặp.
- [x] Kiểm thử tự động 9 tầng `scripts/qa_full_bot.py --autostart` đạt **395/395 PASS (0 FAIL)** (`L1=8, L2=141, L3=59, L4=66, L6=38, L7=31, L8=22, L9=27, L5=3`) và `scripts/smoke_test.py --autostart` đạt **100% PASS (0 lỗi)**.
