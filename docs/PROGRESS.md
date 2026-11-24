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
- [x] Kiểm thử tự động 6 tầng `scripts/qa_full_bot.py --autostart` đạt **310/310 PASS (0 FAIL)** (`L1=8, L2=141, L3=59, L4=66, L6=33, L5=3`) và `scripts/smoke_test.py --autostart` đạt **100% PASS (0 lỗi)**.
