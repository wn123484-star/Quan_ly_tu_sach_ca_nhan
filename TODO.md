# TODO - Kế Hoạch Hoàn Thiện Kiến Trúc, Giao Diện & Bảo Mật BookShare

Theo dõi tiến độ thực thi 5 pha hoàn thiện hệ thống BookShare (.NET 10 ASP.NET Core MVC + EF Core SQL Server).

---

## PHA 1: Vá Hạ tầng Docker & SQL Seeder
- [x] Kiểm tra `S22AdminAnalyticsService` và cách tìm đường dẫn tệp SQL mẫu (`02_BookShare_SeedData.sql`) để bảo đảm tương thích cả Docker (`/sql` & `/app/database`) và môi trường Local.
- [x] Cập nhật `docker-compose.yml`: mount thêm `- ./database:/sql:ro` vào service `bookshare-web`.
- [x] Cập nhật `Dockerfile` (và kiểm tra `.dockerignore`): sao chép thư mục `database/` vào image final (`/app/database` hoặc `/sql`) để nút "Nạp Dữ Liệu Mẫu Chuẩn Từ Tệp SQL" hoạt động 100% trên VPS.

## PHA 2: Nâng cấp Giao diện Chuẩn Doanh nghiệp (Enterprise UI)
- [x] Rà soát đầy đủ toàn bộ class `.bs-*` đang được dùng ở `S13`, `S14`, `S15` và các trang liên quan.
- [x] Bổ sung hệ thống CSS Enterprise Sapphire & Glassmorphism cho `.bs-card`, `.bs-input`, `.bs-select`, `.bs-textarea`, `.bs-badge`, `.bs-badge-info`, `.bs-badge-success`, `.bs-badge-warning`, `.bs-badge-danger`, `.bs-btn`, `.bs-btn-primary`, `.bs-btn-outline`, `.bs-label`, `.bs-muted` trong `src/BookShare.Web/wwwroot/css/core-theme.css`, tương thích cả Light Mode & Dark Mode.
- [x] Nâng cấp `Views/Home/Index.cshtml`:
  - [x] Thêm thanh chọn mật độ hiển thị kệ sách (3 Cột Rộng / 4 Cột Tiêu chuẩn / Danh sách ngang) với lưu trạng thái `localStorage`.
  - [x] Thêm Empty State chuyên nghiệp khi kho sách đang trống (kèm nút dẫn sang trang Quản trị để Admin nạp dữ liệu mẫu).
- [x] Rà soát toàn bộ `Views/` (bao gồm `_Layout.cshtml` và 22 phân hệ):
  - [x] Đảm bảo thanh Header cố định không che khuất nội dung đầu trang hoặc các thẻ `.sticky-top` bên trong `main`.
  - [x] Đảm bảo không còn sót nhãn kỹ thuật `S01`–`S22` hay `Ver07` trên giao diện hiển thị người dùng.

## PHA 3: Khép kín Chuỗi Logic Nghiệp vụ Thực tế
- [x] Trong `S08LibraryLoanService.cs`: bổ sung `[FEAT-LOGIC]` chặn độc giả tạo phiếu mượn mới nếu đang có hợp đồng quá hạn (`Overdue`), đang có tranh chấp mở (`Disputed`), hoặc `TrustScore < 50`, trả về thông báo lý do rõ ràng.
- [x] Trong `S09P2PLoanService.cs`: bổ sung `[FEAT-LOGIC]` chặn độc giả tạo phiếu mượn mới nếu đang có hợp đồng quá hạn (`Overdue`), đang có tranh chấp mở (`Disputed`), hoặc `TrustScore < 50`, trả về thông báo lý do rõ ràng.
- [x] Rà soát các luồng tạo mượn khác (`S06SearchDiscoveryService.CreateQuickLoanRequestAsync`) để đảm bảo cùng áp dụng luật chặn mượn nhất quán.
- [x] Cập nhật giao diện `S08` và `S09`: với các đơn đã hoàn trả (`Returned`), bổ sung nút liên kết nhanh `"⭐ Đánh giá lượt mượn này"` dẫn thẳng sang `/S15TrustScoreReview` (kèm tự động nạp `loanId`, `targetType`, `targetId` ở `s15-trust-score-review.js`) để khép kín vòng đời Mượn ➔ Trả ➔ Đánh giá ➔ Cộng TrustScore.

## PHA 4: Tích hợp Chống Bot Google reCAPTCHA v3 (Qua `.env`)
- [x] Tạo `IRecaptchaService` và `RecaptchaService` (đăng ký DI trong `Program.cs`) đọc `ReCaptcha:SiteKey` và `ReCaptcha:SecretKey` từ cấu hình `.env`.
- [x] Triển khai cơ chế Smart Bypass: nếu Key để trống ➔ tự động bỏ qua (test Local mượt mà); nếu có Key thật ➔ POST `https://www.google.com/recaptcha/api/siteverify`, yêu cầu `success == true` và `score >= 0.5`.
- [x] Nhúng script Google reCAPTCHA v3 (khi có `SiteKey`) ở Frontend (`_Layout.cshtml` + `core-api.js`) và gắn `[BOT-GUARD]` cho các điểm nóng:
  - [x] Đăng ký tài khoản (`S01AuthOtpGoogle/Register`)
  - [x] Đăng nhập (`S01AuthOtpGoogle/Login`)
  - [x] Gửi mã OTP (`S01AuthOtpGoogle/SendOtp` & caller từ `S18`)
  - [x] Tạo phiếu mượn nhanh (`S06SearchDiscovery/QuickRequestLoan` & caller từ `S14`)

## PHA 5: Kiểm thử Tự động (Auto-Test Tìm Bug) & Báo cáo
- [x] Chạy `dotnet build src/BookShare.Web/BookShare.Web.csproj` đạt 0 Errors.
- [x] Tạo script `scripts/smoke_test.py` kiểm tra tự động:
  - [x] Lớp 1: Kiểm tra biên dịch (`dotnet build`), Docker mount & quét mã nguồn không còn nhãn `S01`–`S22` / `Ver07` trên UI.
  - [x] Lớp 2: Quét GET Trang chủ & toàn bộ 22 trang phân hệ đảm bảo HTTP 200 OK (0 lỗi HTTP 500).
  - [x] Lớp 3: Kiểm thử bảo mật RBAC — gọi POST các API nhạy cảm (`S22AdminAnalytics/ManageSampleData`, `S21LibrarianDesk/BatchImportCopies`, `S19DisputeResolution/ResolveDispute`, `S20ExchangeHubClub/VerifyPhysicalBook`) khi chưa đăng nhập (cả không CSRF lẫn có CSRF), xác nhận bị chặn 403 Forbidden đúng chuẩn.
- [x] Thực thi `scripts/smoke_test.py --autostart --skip-build` xác minh toàn bộ 3 lớp đạt 100% (0 lỗi).
- [x] Cập nhật toàn bộ `[x]` vào `TODO.md` và tổng hợp báo cáo kết quả.
