# BookShare – Frontend Component Smoke Test

**Ngày chạy:** 16/11/2026
**Trình duyệt:** Chromium 144.0.7559.96
**Lệnh:** `python scripts/qa_frontend_smoke.py`

## Kết quả

**17/17 PASS**

| Nhóm | Kiểm tra | Kết quả |
|---|---|---|
| Client Validation | Trường bắt buộc rỗng bị chặn; lỗi hiển thị inline | PASS |
| Client Validation | Email sai định dạng bị nhận diện | PASS |
| Client Validation | Form hợp lệ được submit | PASS |
| OTP | Không submit khi thiếu một trong sáu chữ số | PASS |
| OTP | Chặn ký tự không phải chữ số | PASS |
| OTP | Sáu chữ số hợp lệ được chấp nhận | PASS |
| Responsive | Bảng tự được bọc trong vùng cuộn ngang | PASS |
| Responsive | Không tràn ngang document tại 375×812, 768×1024, 1366×768 | PASS |
| Status UI | HTTP 200 hiển thị trạng thái thành công | PASS |
| Status UI | HTTP 400 có trạng thái lỗi dữ liệu riêng | PASS |
| Status UI | HTTP 403 có lớp `status-403` riêng | PASS |
| API | Response 204 không body được xử lý an toàn | PASS |
| Status UI | HTTP 500 hiển thị thông báo an toàn, không đưa exception ra UI | PASS |
| Server guardrail (source check) | UpdateProfile/ChangePassword có `[Authorize]` | PASS |
| Server guardrail (source check) | S21/S22 GET giới hạn theo role; S22 không fallback ID admin | PASS |
| Server guardrail (source check) | Đăng ký công khai luôn được cấp role User ở server | PASS |
| Server validation (source check) | DTO xác thực OTP yêu cầu đúng sáu chữ số | PASS |

### Số đo responsive

| Viewport | Độ rộng document | Độ rộng vùng điều hướng | Độ rộng nội dung cuộn của điều hướng |
|---:|---:|---:|---:|
| 375px | 375px | 359px | 2052px |
| 768px | 768px | 752px | 752px |
| 1366px | 1366px | 1350px | 1350px |

Ở 375px, thanh điều hướng 22 dịch vụ cuộn ngang bên trong, không làm tràn toàn trang.

## Phạm vi và giới hạn

Đây là kiểm tra component bằng Chromium với DOM harness: chạy mã `form-validation.js`, `core-api.js` và CSS `core-theme.css` thật trong một trang kiểm thử nhỏ. Các kiểm tra quyền phía server trong script là kiểm tra source marker, chưa phải HTTP integration test.

Không khẳng định những phần sau đã PASS: `dotnet build`, EF Core lưu/đọc dữ liệu thật, cookie authentication, OTP qua Resend/DB, các luồng mượn/trả, phân quyền qua HTTP và toàn bộ 22 trang khi khởi chạy ứng dụng. Môi trường biên tập không có .NET 10 SDK, SQL Server hoặc `sqlcmd`; các nội dung này vẫn cần chạy lại trên máy có cấu hình dự án.
