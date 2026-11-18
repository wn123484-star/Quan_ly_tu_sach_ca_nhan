# BookShare – Kịch bản Video Demo (3–5 phút)

**Độ phân giải đề nghị:** 1920×1080 • **Tốc độ khung hình:** 30 fps • **Thời lượng mục tiêu:** 4 phút
**Cách quay:** ghi trực tiếp cửa sổ trình duyệt và DevTools; dùng thuyết minh hoặc phụ đề ngắn. Không dựng cảnh giả lập thao tác thành công.

| Thời gian | Màn hình / thao tác | Lời thuyết minh hoặc chú thích |
|---|---|---|
| 00:00–00:15 | Trang chủ BookShare Portal, lướt thanh dịch vụ | “BookShare hỗ trợ quản lý tủ sách cá nhân, chia sẻ, mượn trả và phân quyền theo vai trò.” |
| 00:15–00:55 | S01 → tab OTP → chọn “Đăng nhập OTP”; nhập email test → gửi OTP → nhập 6 số → xác thực | “Người dùng nhận mã OTP sáu chữ số. Hệ thống chỉ tạo phiên sau khi mã hợp lệ, chưa hết hạn và chưa được sử dụng.” |
| 00:55–01:35 | S02 → tạo kệ → thêm một đầu sách/bản sao → lưu | “Người dùng tạo kệ và thêm sách vào tủ cá nhân; dữ liệu được lưu theo tài khoản đang đăng nhập.” |
| 01:35–02:20 | S06 tìm một bản sao đang sẵn có → gửi yêu cầu; mở S08 hoặc S09 xem yêu cầu | “Từ kho sách, người dùng chọn sách, thời hạn và điểm nhận để gửi yêu cầu mượn kho chung hoặc mượn P2P.” |
| 02:20–02:55 | Mở luồng nghiệp vụ trả sách ở S21 hoặc màn hình hợp đồng tương ứng; xác nhận trả | “Nhân sự có quyền ghi nhận việc nhận trả và cập nhật trạng thái hợp đồng.” |
| 02:55–03:35 | Đăng nhập OTP vai trò User → thử S22 và ghi nhận 403; đăng nhập OTP vai trò Admin → mở S22 | “Tài khoản User bị chặn khỏi màn hình quản trị; Admin được phép truy cập theo role phía máy chủ.” |
| 03:35–04:00 | Quay lại danh sách kết quả test/branch/PR | “Kết thúc demo với các tiêu chí đã kiểm thử và phiên bản mã nguồn.” |

## Thiết lập ghi hình

1. Chạy ứng dụng trong `Development`, kết nối SQL Server/BookShareDb đã seed dữ liệu kiểm thử và dùng email test riêng.
2. Chuẩn bị hai tài khoản test: `User` và `Admin`; cấu hình Resend test hoặc dùng Dev OTP panel chỉ trong Development.
3. Đặt trình duyệt 1920×1080; phóng to 100%; ẩn bookmark bar và thông tin cá nhân. Dùng DevTools mobile riêng cho phần responsive nếu cần.
4. Quay liên tục từng đoạn theo timecode, cắt bỏ thời gian chờ nhưng không ghép cảnh khiến luồng trông như đã chạy nếu chưa thực hiện.
5. Kiểm tra lần cuối để bảo đảm video không lộ cookie, CSRF token, API key, email cá nhân, mật khẩu hoặc OTP dùng thật.
6. Xuất H.264 MP4, 1080p, 30 fps, lưu thành `docs/BookShare_Video_Demo.mp4`.

**Trạng thái lần bàn giao này:** kịch bản đã soạn; file MP4 chưa được tạo vì môi trường hiện tại không có .NET 10 SDK và SQL Server/BookShareDb chạy được để ghi hình chức năng thật. Các ca mượn/trả và quyền Admin cũng cần xác nhận trên runtime trước khi ghi video.
