# TODO - KẾ HOẠCH NÂNG CẤP TOÀN DIỆN BOOKSHARE TURBO MODE (GIAI ĐOẠN 0 ➔ 8)

Nhánh phát triển: `feat/bookshare-rental-mobile-turbo`  
Nhánh sao lưu dự phòng: `backup/pre-turbo-rental`

---

## GIAI ĐOẠN 0: An Toàn Git — Backup & Tạo Nhánh Mới
- [x] Kiểm tra `.gitignore` bảo vệ `.env` và `appsettings.*.local.json` khỏi bị commit lên GitHub.
- [x] Tạo nhánh dự phòng `backup/pre-turbo-rental` và đẩy lên GitHub.
- [x] Tạo & chuyển sang nhánh `feat/bookshare-rental-mobile-turbo` và đẩy lên GitHub.

## GIAI ĐOẠN 1: Cập Nhật Luật (`docs/RULES.md`), Tạo `TODO.md` & Mã Truy Vết
- [x] Cập nhật `docs/RULES.md` sang Mô hình Lai (Thư viện miễn phí 0đ & Cho thuê sách giấy vật lý Escrow) + Quy chuẩn Mã Truy Vết `[BS-*]`.
- [x] Tạo `TODO.md` theo dõi toàn bộ 8 Giai đoạn.
- [x] Xác nhận `docker-compose.yml` mount `./database:/sql:ro` cho `bookshare-web` (`[BS-RULE-DOCKER-SEEDER]`).

## GIAI ĐOẠN 2: Mở Rộng CSDL SQL Server (Database-First + Auto-Ensure An Toàn)
- [x] Cập nhật `database/01_BookShare_Schema.sql`: mở rộng `BookCopies` (`DailyRentalFee`, `DepositAmount`), `Loans` (`RentalFeeTotal`, `DepositHeld`, `EscrowStatus`), thêm 4 bảng mới (`QrLoginSessions`, `UserWallets`, `ReadingSessions`, `SupportChatMessages`).
- [x] Cập nhật `Models/Entities/` & `Data/BookShareDbContext.cs` khớp tuyệt đối schema mới.
- [x] Tạo `DatabaseSchemaEnsurer` và gọi tự động trong `Program.cs` (`IF OBJECT_ID / COL_LENGTH IS NULL`) để VPS không bao giờ lỗi thiếu cột/bảng.
- [x] Cập nhật `database/02_BookShare_SeedData.sql`: nạp sẵn phí thuê, cọc Escrow & ví mẫu cho tài khoản demo.

## GIAI ĐOẠN 3: Nâng Cấp Giao Diện Chuyên Nghiệp (Header Thấp Khóa Cứng + Auto-Hide Banner)
- [ ] Thiết kế lại Header siêu gọn (~72px–84px Desktop), khóa cố định `position: fixed !important` kèm `#bsHeaderFixedSpacer` đo tự động bằng `ResizeObserver`, gắn nút `📱 Mobile App` (`[BS-UI-HEADER]`).
- [ ] Nâng cấp `#systemStatusBox` tự động ẩn sau 2s (mặc định) / 4s (200/300) / 6s (400/500), tạm dừng khi hover, có thanh tiến độ đếm ngược (`[BS-UI-STATUS-AUTOHIDE]`).
- [ ] Hoàn thiện CSS Doanh nghiệp `.bs-*` cho `S13`, `S14`, `S15`, chuẩn hóa 7 Theme Doanh nghiệp (gồm Warm Ivory Paper), thêm bộ chọn mật độ kệ sách (3 Cột / 4 Cột / Danh sách) + Clean Empty State cho `Home` và `S06` (`[BS-UI-ENTERPRISE]`).

## GIAI ĐOẠN 4: Nghiệp Vụ Thuê Sách Khép Kín, Ví Bảo Lãnh & QR Cross-Login
- [ ] Luồng Thuê Sách & Escrow (`S02`, `S06`, `S08`, `S09`, `S12`): thiết lập phí thuê/ngày & tiền cọc, bộ tính tổng 7/14/30 ngày, khóa cọc Escrow khi giao sách, hoàn 100% cọc & giải ngân phí thuê khi trả nguyên vẹn, modal Phiếu Hợp Đồng Thuê Điện Tử kèm QR/Barcode in A5/80mm (`[BS-RENTAL-FLOW]`).
- [ ] Ví Bảo Lãnh BookShare Pay (`S17` + API `/api/mobile/wallet/*`): `summary`, `topup` không giới hạn + HMAC-SHA512, `pay-qr` 2 bước + mã PIN 6 số (mặc định `123456`) + chống trừ lặp, `change-pin`, Thẻ Ví 3D (`[BS-WALLET-PAY]`).
- [ ] QR Cross-Login (`Mobile -> Web PC`): API `/api/mobile/qr-login/init`, `status`, `approve`, `/S01AuthOtpGoogle/ClaimQrLogin` (chống dùng lại 2 lần) + khung quét QR 180s polling 2s ngay trên trang đăng nhập `S01` (`[BS-QRLOGIN-BRIDGE]`).

## GIAI ĐOẠN 5: Reading Studio Bổ Trợ Thuê Sách (Tuyệt Đối Không Âm Thanh)
- [ ] Xây dựng `/S03ReadingTracker/Studio?bookId=...` và API `/api/mobile/reading/log-session` (`[BS-READING-STUDIO]`):
  - [ ] Pomodoro im lặng (25p / 50p / Tùy chỉnh) lưu `ReadingSessions` & cộng trang `ReadingProgresses`.
  - [ ] Đo tốc độ đọc (trang/giờ), chuỗi Streak 🔥, lộ trình kịp hạn thuê (`DueDate`) & nút gia hạn nhanh +7 ngày.
  - [ ] Voice-to-Quote (`SpeechRecognition` `vi-VN`) lưu `BookNotes`, Quote Flashcards lật thẻ, chế độ nền Sepia / Sáng / Đêm & chỉnh cỡ chữ `A- / A / A+`.

## GIAI ĐOẠN 6: AI Copilot Hành Động, Chat Thủ Thư & Google reCAPTCHA v3
- [ ] Nâng cấp `S13GeminiAiService` & `/api/ai-chat/quick-ask` trả về `{ reply, sessionId, actions: [...] }` hỗ trợ `SET_DARK_MODE`, `SET_THEME`, `SEND_EMAIL`, `UPDATE_PROGRESS`, `NAVIGATE` (`[BS-AGENT-AI-COPILOT]`).
- [ ] Kiểm tra & đảm bảo tiêu đề Email OTP tại `ResendEmailService` tuyệt đối KHÔNG lộ mã OTP 6 chữ số (`[BS-SEC-OTP]`).
- [ ] Nâng cấp Widget Chat nổi thành 2 Tab: **🤖 Trợ Lý AI Copilot** & **👩‍🏫 Chat Thủ Thư / Chủ Sách** (bảng `SupportChatMessages`, polling 10s, hỗ trợ Thủ thư/Admin trả lời).
- [ ] Gắn Google reCAPTCHA v3 (`[BS-SEC-RECAPTCHA]`) cho đầy đủ: `S01` (Register, Login, SendOtp), `S06` (QuickRequestLoan), `S08` (CreateRequest), `S09` (CreateP2PRequest).

## GIAI ĐOẠN 7: Mobile Super-App (`/MobileApp`) & Bộ Đóng Gói Android APK
- [ ] Xây dựng `MobileAppController`, `Views/MobileApp/Index.cshtml`, `wwwroot/modules/mobile-app/mobile-app.css` & `mobile-app.js` với 5 Tab đáy màn hình (Trang Chủ, Đọc Sách Studio, Quét QR đa năng `html5-qrcode`, Ví Pay 3D, Thông Báo & Chat) + đăng nhập/đăng xuất đồng bộ Cookie (`[BS-MOBILE-SUPERAPP]`).
- [ ] Tạo bộ khung Android APK Capacitor tại `mobile-android-apk/` (`package.json`, `capacitor.config.json`, `AndroidManifest.xml`, `README_BUILD_APK.md`).

## GIAI ĐOẠN 8: Kiểm Thử Tự Động E2E, Tự Sửa Bug & Push Hoàn Chỉnh
- [ ] Chạy `dotnet build BookShare.sln` đạt 0 Errors.
- [ ] Tạo & chạy `scripts/run-e2e-verify.ps1` và `scripts/run-e2e-verify.sh` kiểm thử đầy đủ 7 kịch bản (22 trang + MobileApp 200 OK, Thuê->Escrow->Hoàn cọc, QR Cross-Login, Ví Pay + PIN + chống trừ lặp, AI Copilot + OTP subject, RBAC 403, Không Web Audio API).
- [ ] Xác minh 100% PASS, cập nhật toàn bộ `[x]` vào `TODO.md`, commit & push lên `feat/bookshare-rental-mobile-turbo`.
