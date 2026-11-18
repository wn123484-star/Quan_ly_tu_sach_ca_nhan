# Sprint 2 – Kiệt: Hồ sơ cá nhân, Validation UI & Video Demo

> Phạm vi được triển khai theo Issue #10 và checklist giao việc. Danh sách 10 test case bên dưới là ma trận đề xuất trong tài liệu bàn giao; cần đối chiếu rubric chính thức của giảng viên trước khi coi đây là 10 tiêu chí nguyên bản.

## 1. Nhánh làm việc và những thay đổi trong source

- Branch cục bộ: `sprint2/kiet-profile-validation` (tạo từ `main`).
- Chưa push được lên GitHub trong phiên làm việc này; cần push branch từ máy có quyền ghi repository.

### S01 – Hồ sơ cá nhân và xác thực

- Bỏ fallback `u-user-01` khỏi luồng dựng ViewModel hồ sơ. Khi chưa đăng nhập, service không truy vấn hồ sơ demo; trang chỉ hiển thị lời nhắc đăng nhập.
- Chỉ render thẻ hồ sơ, form cập nhật và form đổi mật khẩu khi cookie đã xác thực.
- Thêm `[Authorize]` cho `UpdateProfile` và `ChangePassword`; ID tài khoản chỉ lấy từ `ClaimTypes.NameIdentifier`, không lấy `userId` từ client và không còn fallback tài khoản mẫu.
- Bổ sung DataAnnotations và kiểm tra `ModelState` cho Login, Register, Send OTP, Verify OTP, Update Profile và Change Password.
- Bỏ mật khẩu điền sẵn trong các input mật khẩu. Tự động điền tài khoản demo chỉ còn ở thẻ test trong Development/Testing; các thẻ này bị ẩn ở môi trường khác.
- Cập nhật hồ sơ có trim dữ liệu, kiểm tra URL ảnh `http/https`, và cho phép xóa URL ảnh bằng giá trị trống.
- Đổi mật khẩu kiểm tra mật khẩu hiện tại bằng PasswordHasher; các hash đánh dấu SeedData cũ chỉ được xác minh bằng mật khẩu seed trong luồng kiểm thử rồi thay ngay bằng hash Identity thực khi đổi mật khẩu.
- Giữ luồng đăng nhập OTP qua email hiện có: chỉ gửi mã Login cho email đã tồn tại; chỉ tạo cookie sau khi OTP đúng, còn hạn và được đánh dấu đã dùng một lần. Đây là phương thức đăng nhập bằng OTP, không phải MFA gồm mật khẩu + OTP.
- Tắt Google Sandbox giả lập ở môi trường không phải Development khi chưa cấu hình OAuth thật.

### OTP

- Sinh mã 6 chữ số bằng `RandomNumberGenerator`, bao gồm cả trường hợp có số 0 ở đầu.
- Lưu SHA-256 hash, hạn 5 phút; xác thực chỉ nhận đúng sáu ký tự số ASCII.
- Dùng cập nhật có điều kiện (`ExecuteUpdateAsync`) để chỉ một request có thể tiêu thụ mã OTP; mã đã dùng/hết hạn không thể xác thực lại.
- Không ghi OTP rõ hoặc hash OTP vào log. OTP Dev chỉ trả về cho giao diện trong Development/Testing; môi trường khác yêu cầu gửi Resend thành công, nếu không trả lỗi dịch vụ.

### UI dùng chung, Empty State và Responsive

- Thêm `wwwroot/js/form-validation.js` và nạp tại `_Layout.cshtml`: áp dụng cho tất cả form, sử dụng constraint validation HTML5, đánh dấu trường lỗi, hiển thị thông báo gần trường nhập và hỗ trợ `aria-describedby`. Khi có lỗi, submit handler của module không được chạy nên không gửi AJAX sai dữ liệu.
- Bổ sung `required` cho các trường bắt buộc chính ở các form S02/S03/S05/S06/S10/S15/S16/S17/S19/S20/S21 và hộp chat AI; form lọc/tìm kiếm vẫn giữ trường tùy chọn.
- Thêm trạng thái màu riêng cho HTTP 403; giữ phân biệt 200/300/400/404/500.
- Bổ sung điều chỉnh mobile cho form S01, tabs, cụm email/OTP và 6 ô OTP.
- Tạo dải cuộn ngang cho thanh điều hướng 22 dịch vụ ở màn hình tablet/mobile; bổ sung giới hạn chiều rộng bảng, modal, widget chat và banner trạng thái.
- `#systemStatusBox` có `role=status`/`aria-live`; `core-api.js` phân biệt status 403 và xử lý phản hồi rỗng/204.
- Giữ nguyên Empty State hiện có của S06 (không có kết quả thì hiển thị thông báo và nút “Hiển thị toàn bộ kho sách”).

## 2. Danh sách file chính được chỉnh sửa

- `src/BookShare.Web/Controllers/S01AuthOtpGoogleController.cs`
- `src/BookShare.Web/Controllers/S21LibrarianDeskController.cs` và `src/BookShare.Web/Controllers/S22AdminAnalyticsController.cs`
- `src/BookShare.Web/Models/ViewModels/S01AuthViewModel.cs`
- `src/BookShare.Web/Services/Implementations/S01AuthOtpService.cs`
- `src/BookShare.Web/Services/Implementations/ResendEmailService.cs`
- `src/BookShare.Web/Services/Interfaces/IS01AuthOtpService.cs`
- `src/BookShare.Web/Views/Modules/S01_AuthOtpGoogle/Index.cshtml`
- `src/BookShare.Web/Views/Shared/_Layout.cshtml`
- Các view form liên quan S02/S03/S05/S06/S10/S15/S16/S17/S19/S20/S21 (thêm thuộc tính validation HTML5)
- `src/BookShare.Web/Program.cs`
- `src/BookShare.Web/wwwroot/js/core-api.js`
- `src/BookShare.Web/wwwroot/js/form-validation.js` (mới)
- `src/BookShare.Web/wwwroot/css/core-theme.css`
- `src/BookShare.Web/wwwroot/modules/s01-auth-otp-google/s01-auth-otp-google.js`
- `src/BookShare.Web/wwwroot/modules/s01-auth-otp-google/s01-auth-otp-google.css`
- `src/BookShare.Web/wwwroot/modules/s15-trust-score-review/s15-trust-score-review.js`
- `src/BookShare.Web/Views/Modules/S02_PersonalShelf/Index.cshtml` đến các biểu mẫu S03/S06/S08/S09/S10/S15/S16/S17/S19/S20/S21 có bổ sung validation
- `scripts/record_bookshare_demo.py`, `scripts/qa_frontend_smoke.py`, `docs/BookShare_Video_Demo_Script.md` và `docs/FRONTEND_SMOKE_TEST.md`

## 3. Ma trận 10 ca kiểm thử

**Trạng thái hiện tại: Chưa chạy kiểm thử chức năng trên runtime.** Không đánh dấu Pass khi chưa thực thi được ứng dụng.

| Mã | Luồng | Kết quả mong đợi | Trạng thái |
|---|---|---|---|
| TC01 | Đăng nhập/đăng xuất và nhận diện vai trò | Đăng nhập hợp lệ; sai thông tin bị từ chối; logout đóng phiên; role đúng | Chưa chạy |
| TC02 | Đăng ký và validation | Trường bắt buộc/email/mật khẩu sai định dạng bị chặn; dữ liệu đúng được xử lý | Chưa chạy |
| TC03 | Guest và quyền xem hồ sơ | Guest không thấy dữ liệu hồ sơ; gọi API cập nhật/đổi mật khẩu không đăng nhập nhận 403 | Chưa chạy |
| TC04 | Cập nhật hồ sơ | Chỉ cập nhật tài khoản hiện tại; lưu xong tải lại hiển thị dữ liệu mới | Chưa chạy |
| TC05 | Đổi mật khẩu | Sai mật khẩu hiện tại bị từ chối; mật khẩu mới đăng nhập được; mật khẩu cũ không còn dùng được | Chưa chạy |
| TC06 | OTP 6 chữ số | Mã đúng được chấp nhận; mã thiếu/sai/hết hạn/đã dùng lại bị từ chối | Chưa chạy |
| TC07 | Client + Server Validation | Client chặn dữ liệu sai; request giả lập bỏ qua client vẫn bị server từ chối | Chưa chạy |
| TC08 | Status UI 200/300/400/403/404/500 | Mã và thông báo đúng, 403 có kiểu riêng; không lộ stack trace/secret | Chưa chạy |
| TC09 | Empty State tìm kiếm | Không có kết quả hiển thị Empty State; xóa bộ lọc hiển thị lại dữ liệu | Chưa chạy |
| TC10 | Responsive và hồi quy | Kiểm tra các luồng S01/S02/S03/S06 và thanh điều hướng 22 dịch vụ trên 375×812, 768×1024 và 1366×768; kiểm tra bảng/modal không làm tràn trang | Chưa chạy |

## 4. Hạn chế kiểm thử trong lần bàn giao này

- Môi trường thao tác hiện tại không có .NET SDK (`dotnet` không có trong PATH), vì vậy chưa thể chạy `dotnet build` hoặc khởi động ASP.NET Core.
- Chưa có phiên SQL Server/BookShareDb đang chạy để kiểm tra luồng ghi/đọc thực tế.
- Solution hiện chỉ khai báo project `BookShare.Web`; chưa có project kiểm thử tự động riêng.
- Đã chạy `node --check` cho toàn bộ JavaScript trong `wwwroot` mà không có lỗi cú pháp.
- Đã chạy `python scripts/qa_frontend_smoke.py`: **17/17 kiểm tra component PASS** bằng Chromium 144; gồm required/email/OTP validation, status 200/400/403/500, response 204, auto-wrap bảng và không tràn document ở 375/768/1366px. Chi tiết tại `docs/FRONTEND_SMOKE_TEST.md`. Đây không thay thế kiểm thử trên ứng dụng ASP.NET Core đang chạy.
- Video demo thực tế chưa thể quay ở môi trường hiện tại vì không có .NET 10 SDK và SQL Server/BookShareDb đang chạy. Không tạo video mô phỏng giả luồng thao tác; kịch bản quay thật sẽ được bàn giao thành tài liệu riêng.

## 5. Kịch bản quay video demo đề xuất (3–5 phút)

Kịch bản chi tiết và phụ đề tiếng Việt mẫu: [`BookShare_Video_Demo_Script.md`](BookShare_Video_Demo_Script.md). Công cụ ghi hình thủ công tại máy đã chạy được ứng dụng: `python scripts/record_bookshare_demo.py --url https://localhost:5001`. Script ghi cửa sổ trình duyệt 1920×1080, sau đó xuất MP4 và nhúng track phụ đề tiếng Việt từ `docs/BookShare_Video_Demo.vi.srt`.

## 6. Cách chạy và hoàn tất kiểm thử ở máy có .NET 10

```bash
# 1) Cấu hình môi trường theo .env.example (SQL Server là bắt buộc)
cp .env.example .env

# 2) Kiểm tra SDK và build
 dotnet --version
 dotnet build BookShare.sln

# 3) Chạy ứng dụng trong Development để kiểm tra OTP demo
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/BookShare.Web
```

Sau khi chạy được app, thực hiện TC01–TC10 theo thứ tự ở mục 3; ghi rõ trình duyệt, kích thước viewport, dữ liệu test, Actual/Expected và ảnh/video bằng chứng. Trước khi quay video, kiểm tra lại môi trường đang là Development và không dùng dữ liệu người thật.
