# KẾ HOẠCH NÂNG CẤP TOÀN DIỆN SPRINT 3 — BOOKSHARE PORTAL & MOBILE SHELL

> **Phạm vi:** Nâng cấp hệ thống BookShare (.NET 10 MVC + SQL Server) với 5 mảnh ghép nghiệp vụ khép kín, Trợ lý AI Copilot thực thi hành động 2 tầng, Đăng nhập QR 2 chiều, Mobile WebView Shell Android (không hỏi URL) & CI/CD đóng gói APK trên GitHub Actions, kèm mở rộng Bot QA tự động Tầng 6.  
> **Nguyên tắc bắt buộc:** Strict Service Layer (`Controller -> IService -> Service -> BookShareDbContext`), 100% phi thương mại chia sẻ sách giấy 0đ, bảo mật RBAC/Anti-IDOR/CSRF, mặc định **Light Mode** (`#f8fafc`), 0 thuật ngữ kỹ thuật trên UI.

---

## TIÊU CHÍ NGHIỆM THU (DEFINITION OF DONE)

1. Toàn bộ tài liệu `.md` ở Pha 0 được lập trước khi chạm vào code, và cập nhật tiến độ `[x]` + Change Log đầy đủ sau từng pha.
2. `dotnet build BookShare.sln --nologo` đạt **0 Error(s)**.
3. CSDL `BookShareDb` cập nhật đúng quy trình Database-First (`database/01_BookShare_Schema.sql` -> `dotnet ef dbcontext scaffold --no-onconfiguring --force`), không sửa tay tệp trong `Models/Entities/`.
4. `python scripts/qa_full_bot.py --autostart` đạt **100% PASS (0 FAIL)** trên toàn bộ Lớp 1–6 (giữ trọn 277 assertions cũ + toàn bộ assertions Lớp 6 mới) và khôi phục sạch về mốc 6 Thành viên / 55 Bản sao ở Lớp 5.
5. `python scripts/smoke_test.py --autostart` đạt **100% PASS (0 lỗi)**.
6. Không lộ thanh URL, không hộp thoại hỏi URL trên Android App (`mobile/bookshare-android/`), giao diện sáng đồng bộ `#F8FAFC`, workflow `.github/workflows/build-bookshare-android-apk.yml` hợp lệ.

---

## BẢNG CHECKLIST TỪNG PHA

### PHA 0: Lập Bộ Tài Liệu Đặc Tả & Theo Dõi Tiến Độ (Làm Đầu Tiên)
- [x] `P0.1` Tạo kế hoạch tổng thể & Change Log `docs/UPGRADE_SPRINT3_PLAN.md`
- [x] `P0.2` Tạo đặc tả QR Auth 2 chiều & QR Bàn giao sách `docs/QR_AUTH_AND_HANDOVER_SPEC.md`
- [x] `P0.3` Tạo đặc tả AI Copilot Function Calling & Action Cards `docs/AI_COPILOT_TOOLS_SPEC.md`
- [x] `P0.4` Tạo kiến trúc Android WebView Shell & GitHub Actions APK `docs/MOBILE_APP_ARCHITECTURE.md`

### PHA 1: Hoàn Thiện 5 Khoảng Trống Nghiệp Vụ & Tách Dữ Liệu Mẫu Docker
- [x] `P1.1` Bổ sung bảng `QrLoginSessions`, `BookHandoverQrs`, `RoleUpgradeRequests` vào `database/01_BookShare_Schema.sql` (dọn bảng legacy không tương thích), chạy `sqlcmd` & `dotnet ef dbcontext scaffold` đồng bộ `BookShareDbContext` + `EnsureCoreAdminBootstrapAsync` + `S22.ManageSampleData`
  - *Tệp mục tiêu:* `database/01_BookShare_Schema.sql`, `Models/Entities/*`, `Data/BookShareDbContext.cs`, `Services/Implementations/S01AuthOtpService.cs`, `S22AdminAnalyticsService.cs`
  - *Rủi ro cần chặn:* Khóa ngoại bảng mới chặn reset dữ liệu mẫu `S22` hoặc `Scaffold` đổi tên property cũ.
- [x] `P1.2` Khép kín tài chính Tranh chấp `S19` <-> `S17` (có khoản phạt/bồi thường -> giữ `Loan.Status = "Disputed"`, tạo giao dịch `Pending` ở `S17` + thông báo `S18`; chỉ mở khóa khi thanh toán xong qua `HMAC-SHA512`)
  - *Tệp mục tiêu:* `Models/ViewModels/S16ToS22ViewModels.cs`, `Services/Implementations/S19DisputeResolutionService.cs`, `Controllers/S19DisputeResolutionController.cs`, `Services/Implementations/S17SandboxPaymentService.cs`
- [x] `P1.3` Triển khai QR Bàn giao & Hoàn trả Sách Vật lý 1 lần dùng (TTL 10 phút, SHA-256 hash, Anti-IDOR 403, chống replay 400) trên `S08`, `S09`, `S20`, `S21`
  - *Tệp mục tiêu:* `Services/Interfaces/ISprint3Services.cs`, `Services/Implementations/BookHandoverQrService.cs`, `Controllers/HandoverQrController.cs`, Views & JS `S08`/`S09`/`S20`/`S21`
- [x] `P1.4` Luồng Đề nghị Nâng quyền `Curator` (`S01` -> `S22`: User gửi đề nghị -> chặn gửi trùng -> Admin duyệt/từ chối -> tự gán Role & thông báo `S18`)
  - *Tệp mục tiêu:* `IS01AuthOtpService`, `S01AuthOtpGoogleController`, `S01_AuthOtpGoogle/Index.cshtml`, `s01-auth-otp-google.js`, `IS22AdminAnalyticsService`, `S22AdminAnalyticsController`, `S22_AdminAnalytics/Index.cshtml`, `s22-admin-analytics.js`
- [x] `P1.5` Thư viện quét Camera & QR tự host tại `wwwroot/lib/qr/` (Bật/Tắt Camera, Đổi trước/sau, Flash, Chọn ảnh từ máy) kết nối `S05` & `S21`, và tách `02_BookShare_SeedData.sql` khỏi `db-init` mặc định trong `docker-compose.yml`
- *Kiểm chứng Pha 1:* `dotnet build BookShare.sln --nologo` (0 Error).

### PHA 2: Nâng Cấp Trợ Lý AI Copilot Function Calling (`S13`, `S14` & Widget)
- [x] `P2.1` Tăng timeout lên `20 giây`, hỗ trợ hội thoại Multi-turn (`8` tin nhắn gần nhất đúng `UserId`) trong `S13GeminiAiService` & `S14AiRecommendationService`
- [x] `P2.2` Tích hợp Gemini `function_declarations` & Local Intent Fallback trả về `clientCommands` (`set_theme`, `navigate_to`, `search_catalog`) và `actionCards` (`renew_loan`, `update_reading_progress`, `send_my_reminder_email`, `send_due_reminders_batch` chỉ cho `Librarian,Admin`)
- [x] `P2.3` Endpoint tập trung `POST /S13GeminiAiAssistant/ExecuteCopilotAction` (`[Authorize]`, `[ValidateAntiForgeryToken]`, Anti-IDOR + RBAC 403) gọi lại đúng các Service gốc (`S03`, `S12`, `S18`, `IResendEmailService`)
- [x] `P2.4` Cập nhật UI thẻ Action Card nền sáng, nút Xác nhận 1 chạm và xử lý DOM chống XSS trong `S13` & AI Widget (`core-api.js`, `core-theme.css`)
- *Kiểm chứng Pha 2:* `dotnet build BookShare.sln --nologo` (0 Error).

### PHA 3: Đăng Nhập Bằng Quét QR 2 Chiều (Web <-> Mobile App)
- [x] `P3.1` Xây dựng `IQrAuthService` / `QrAuthService` + `QrAuthController` (`/api/qr-login/*`) dùng `RandomNumberGenerator.GetBytes(32)`, chỉ lưu SHA-256 hash, TTL `120s` (Chiều 1) / `60s` (Chiều 2), atomic `Consumed` 1 lần dùng, kiểm tra `LockoutEnd`
- [x] `P3.2` Hoàn thiện đủ API Chiều 1 (`create`, `status/{sessionId}`, `preview`, `approve`, `claim`) và Chiều 2 (`create-mobile-link`, `mobile-claim`) cấp Cookie đăng nhập chuẩn kèm thông báo `S18`
- [x] `P3.3` Cập nhật UI tab **"Đăng nhập bằng QR"**, mã QR 60s trong tab Hồ sơ `/S01AuthOtpGoogle` và Modal **Máy quét QR đa năng** dùng chung nhận diện 3 định dạng (`BOOKSHARE-QRLOGIN`, `BOOKSHARE-MOBILELINK`, `BOOKSHARE-HANDOVER`, ISBN/Barcode)
- *Kiểm chứng Pha 3:* `dotnet build BookShare.sln --nologo` (0 Error).

### PHA 4: Giao Diện Mobile App Shell & API Thông Báo Nền
- [x] `P4.1` Xây dựng `IMobileCompanionService` / `MobileCompanionService` + `MobileApiController` (`GET /api/mobile/notifications/unread?sinceId=`, `GET /api/mobile/session`) & cấu hình `Program.cs` trả `401`/`403` JSON cho `/api/*`
- [x] `P4.2` Nhận diện `User-Agent` `BookShareAndroidApp` trong `_Layout.cshtml` (`is-mobile-app`), dựng Bottom Navigation Bar 5 mục nền sáng (có nút Quét QR trung tâm & badge thông báo) và bridge `window.BookShareAndroidBridge`
- *Kiểm chứng Pha 4:* `dotnet build BookShare.sln --nologo` (0 Error).

### PHA 5: Dự Án Android WebView Shell (`mobile/bookshare-android/`) & GitHub Actions APK
- [x] `P5.1` Tạo cấu trúc Gradle sạch (`settings.gradle`, `build.gradle`, `gradle.properties`, `app/build.gradle` nhúng `BuildConfig.BASE_URL` không hộp thoại hỏi URL)
- [x] `P5.2` Tạo `AndroidManifest.xml`, `MainActivity.java` (`User-Agent: BookShareAndroidApp/1.0`, `NoActionBar` nền sáng `#F8FAFC`, Custom Tabs Google OAuth `bookshare://open`, Camera/FileChooser, Back navigation, `@JavascriptInterface`), `NotificationPoller.java` (gắn `CookieManager.getInstance().getCookie(...)` + kênh Android 13+)
- [x] `P5.3` Hoàn thiện `res/values/{colors,strings,styles}.xml`, `network_security_config.xml`, bộ icon sapphire `mipmap-*`, `assets/offline.html` nền `#F8FAFC`
- [x] `P5.4` Tạo workflow `.github/workflows/build-bookshare-android-apk.yml` đóng gói `BookShare-Mobile-Android.apk` (Retention 30 ngày)
- *Kiểm chứng Pha 5:* Xác nhận đầy đủ cấu trúc Android & cú pháp YAML.

### PHA 6: Mở Rộng Bot Kiểm Thử E2E (`scripts/qa_full_bot.py`) Lớp 6 & Vá Sạch Lỗi
- [x] `P6.1` Bổ sung `run_layer6_sprint3` vào `scripts/qa_full_bot.py` kiểm thử khách quan QR Login 2 chiều, QR Bàn giao Anti-IDOR/Anti-replay, Khép kín `S19 <-> S17`, Đề nghị `Curator`, AI Copilot RBAC 403/200, Mobile API `401`/`200` + kiểm tra tĩnh Android/YAML/Docker
- [x] `P6.2` Chạy `dotnet build BookShare.sln --nologo`, `python scripts/qa_full_bot.py --autostart`, `python scripts/smoke_test.py --autostart`; đạt **310/310 PASS (0 FAIL)** (`L1=8, L2=141, L3=59, L4=66, L6=33, L5=3`)

### PHA 7: Đồng Bộ Toàn Bộ Tài Liệu `.md` & Nghiệm Thu Cuối
- [x] `P7.1` Cập nhật `[x]` & kết quả thực tế vào `UPGRADE_SPRINT3_PLAN.md`, `TODO.md`, `PROGRESS.md`
- [x] `P7.2` Đồng bộ `ARCHITECTURE.md` (gỡ tham chiếu `Areas/` & `Cloudinary`, bổ sung kiến trúc Sprint 3 & Android Shell)
- [x] `P7.3` Cập nhật `SECURITY_RBAC.md`, `API_REFERENCE.md`, `QA_TEST_REPORT.md`
- [x] `P7.4` Cập nhật `README.md` & `DEPLOYMENT_OAUTH_GUIDE.md` với hướng dẫn build/tải APK từ GitHub Actions & nạp seed 1 chạm ở `S22`
- [x] `P7.5` Chạy nghiệm thu lần cuối `qa_full_bot.py --autostart` & `smoke_test.py --autostart --skip-build` -> **100% PASS**

---

## NHẬT KÝ THAY ĐỔI (CHANGE LOG)

| Thời điểm | Pha | Tóm tắt công việc & Kiểm chứng |
| :--- | :--- | :--- |
| 2026-11-24 | Pha 0 | Khởi tạo bộ 4 tài liệu đặc tả Sprint 3 (`UPGRADE_SPRINT3_PLAN.md`, `QR_AUTH_AND_HANDOVER_SPEC.md`, `AI_COPILOT_TOOLS_SPEC.md`, `MOBILE_APP_ARCHITECTURE.md`) trước khi chỉnh sửa mã nguồn. |
| 2026-11-24 | Pha 1 | Bổ sung 3 bảng Sprint 3 + Database-First scaffold `dbo`, khép kín bồi thường tranh chấp `S19 <-> S17`, QR bàn giao/trả sách `BookHandoverQrs`, đề nghị nâng quyền `Curator`, máy quét camera tự host `/lib/qr/`, tách seed khỏi `db-init` Docker; `dotnet build` đạt `0 Error(s)`. |
| 2026-11-24 | Pha 2 | Nâng cấp `S13`/`S14` timeout 20s, hội thoại Multi-turn 8 tin nhắn, Gemini Function Calling & Local Intent Fallback 2 tầng (`clientCommands` + `actionCards`), endpoint `ExecuteCopilotAction` khóa RBAC/Anti-IDOR, dựng Action Card chống XSS trong `core-api.js` & `s13-gemini-ai-assistant.js`. |
| 2026-11-24 | Pha 3 | Xây dựng `QrAuthService` + `QrAuthController` (`/api/qr-login/*`) 2 chiều (`120s`/`60s`, SHA-256 hash, atomic `Consumed` 1 lần dùng, `LockoutEnd`, giới hạn 20 req/phút/IP), UI `#tabQrLogin` & `#tabProfile` trên `S01` cùng `openUniversalScanner()` trong `core-api.js`. |
| 2026-11-24 | Pha 4 | Tạo `MobileCompanionService` & `MobileApiController` (`/api/mobile/notifications/unread`, `/api/mobile/session`), nhận diện `User-Agent` `BookShareAndroidApp` trong `_Layout.cshtml` (`is-mobile-app`), bổ sung Bottom Navigation Bar 5 mục nền sáng & `BookShareAndroidBridge`. |
| 2026-11-24 | Pha 5 | Dựng dự án Android Native WebView Shell sạch tại `mobile/bookshare-android/` (`BuildConfig.BASE_URL`, `#F8FAFC`, `NoActionBar`, Custom Tabs Google OAuth, `CookieManager` `NotificationPoller`) & workflow `.github/workflows/build-bookshare-android-apk.yml`. |
| 2026-11-24 | Pha 6 | Mở rộng `scripts/qa_full_bot.py` (`run_layer6_sprint3`); chạy `qa_full_bot.py --autostart` đạt **310/310 PASS (0 FAIL)** (`L1=8, L2=141, L3=59, L4=66, L6=33, L5=3`) và `smoke_test.py --autostart` đạt **100% PASS (0 lỗi)**. |
| 2026-11-24 | Pha 7 | Đồng bộ toàn bộ tài liệu (`TODO.md`, `PROGRESS.md`, `ARCHITECTURE.md`, `SECURITY_RBAC.md`, `API_REFERENCE.md`, `QA_TEST_REPORT.md`, `MOBILE_APP_ARCHITECTURE.md`, `README.md`, `DEPLOYMENT_OAUTH_GUIDE.md`) và nghiệm thu cuối cùng 100% PASS. |
