# Sprint 2 – Kiệt: Hồ sơ cá nhân, Validation UI & Video Demo

**Branch mục tiêu:** `sprint2/kiet-profile-validation`
**Base:** `main` (snapshot repository được cung cấp)
**Phạm vi:** S01 profile/OTP, validation, status banner và responsive cho 22 dịch vụ.

## Đã chỉnh sửa trong source

- S01: trang Hồ sơ hiển thị riêng theo trạng thái đăng nhập; khách không được nhận dữ liệu profile. Cập nhật Họ tên, Điện thoại, Địa chỉ, Avatar; chỉ ghi vào `AspNetUsers` của `ClaimTypes.NameIdentifier`.
- S01: API cập nhật hồ sơ và đổi mật khẩu có `[Authorize]`, CSRF và server-side `ModelState` validation. Hash mật khẩu mới bằng `PasswordHasher`; xử lý hash seed demo cũ khi user đổi mật khẩu lần đầu.
- OTP: hỗ trợ mục đích `Login`; chỉ phát mã cho email tồn tại, mã 6 chữ số được băm SHA-256, hết hạn 5 phút, tiêu thụ một lần. Sau khi OTP đúng, controller mới tạo cookie và chuyển hướng đến S02. Đây là đăng nhập OTP qua email (không phải bước MFA bổ sung sau mật khẩu).
- Validation: nạp validator dùng chung cho các form ở `_Layout.cshtml`; hiển thị lỗi inline, `aria-invalid`/`aria-describedby`, chặn handler AJAX khi form không hợp lệ. Bổ sung thuộc tính required/length/pattern cho các trường chính.
- Status: `#systemStatusBox` hiển thị 200/400/403/404/500, có status riêng cho 403 và vùng thông báo trợ năng. Trang AccessDenied chuyển về S01 kèm banner 403.
- Phân quyền bổ sung: đăng ký công khai chỉ tạo role `User`; trang S21 yêu cầu `Librarian/Admin`; trang S22 yêu cầu `Admin` ngay ở GET và POST.
- Responsive: thanh 22 dịch vụ cuộn ngang ở tablet/mobile; bảng nằm trong vùng cuộn, modal và AI chat giới hạn chiều cao/rộng, giảm nguy cơ tràn trang ở viewport nhỏ.

## Tệp cần xem

- `src/BookShare.Web/Controllers/S01AuthOtpGoogleController.cs`
- `src/BookShare.Web/Services/Implementations/S01AuthOtpService.cs`
- `src/BookShare.Web/Services/Implementations/ResendEmailService.cs`
- `src/BookShare.Web/Views/Modules/S01_AuthOtpGoogle/Index.cshtml`
- `src/BookShare.Web/wwwroot/modules/s01-auth-otp-google/s01-auth-otp-google.js`
- `src/BookShare.Web/wwwroot/js/form-validation.js`
- `src/BookShare.Web/wwwroot/js/core-api.js`
- `src/BookShare.Web/wwwroot/css/core-theme.css`

## QA hiện tại

- Cần chạy `dotnet build BookShare.sln` và kiểm tra E2E trên Windows/Linux có .NET 10 SDK và SQL Server `BookShareDb`.
- Tại môi trường biên tập hiện tại không có `dotnet`, SQL Server hoặc `sqlcmd`, nên không thể xác nhận build, thao tác lưu DB hay phân quyền qua HTTP runtime.
- `python scripts/qa_frontend_smoke.py` chạy thành công **17/17 kiểm tra component** trên Chromium; xem `docs/FRONTEND_SMOKE_TEST.md`.
- Các ca test chưa được chạy phải giữ trạng thái **Chưa chạy**, không đánh dấu Pass từ kiểm tra source.
- Video `docs/BookShare_Video_Demo.mp4` chưa thể tạo trung thực trong môi trường hiện tại; kịch bản 4 phút đã nằm trong `docs/ISSUE10_IMPLEMENTATION_AND_QA.md`. Sau khi chạy được app và seed DB, quay trực tiếp trình duyệt ở 1920×1080, 30 fps, 3–5 phút, có thuyết minh hoặc chú thích.

## Lệnh kiểm tra sau khi clone

```bash
cp .env.example .env
# Điền SQL Server connection string, Resend API (hoặc dùng Development OTP test)
dotnet restore BookShare.sln
dotnet build BookShare.sln
dotnet run --project src/BookShare.Web
```
