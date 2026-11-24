# ĐẶC TẢ KỸ THUẬT ĐĂNG NHẬP QR 2 CHIỀU & QR BÀN GIAO SÁCH VẬT LÝ

> **Tài liệu Sprint 3 — Bảo mật Mật mã học, Chống Replay & Anti-IDOR**

---

## 1. Kiến trúc Chung Về Token & Băm An toàn

1. **Sinh Token:** Sử dụng `RandomNumberGenerator.GetBytes(32)` mã hóa **Base64Url** (không padding `=`, thay `+` -> `-`, `/` -> `_`) tạo chuỗi ngẫu nhiên 256-bit không thể đoán trước.
2. **Lưu trữ Chỉ-Băm (Hash-Only Storage):** Tuyệt đối không lưu token rõ trong SQL Server. CSDL chỉ lưu chuỗi Hex **SHA-256 (64 ký tự chữ thường)** vào cột `TokenHash` (có `UNIQUE INDEX`).
3. **So sánh An toàn & Chống Replay:** Khi xác thực/claim, tính lại SHA-256 của token gửi lên, tìm đúng bản ghi ở trạng thái hợp lệ chưa dùng và chuyển ngay sang `Consumed` kèm `ConsumedAt = DateTime.UtcNow`. Mọi yêu cầu dùng lại mã đã `Consumed` đều bị từ chối **`HTTP 400`**.

---

## 2. Cơ Sở Dữ Liệu (`database/01_BookShare_Schema.sql`)

### 2.1. Bảng `QrLoginSessions` (Đăng nhập QR 2 Chiều)
*(Lưu ý nâng cấp: nếu tồn tại bảng `QrLoginSessions` cũ không có cột `TokenHash`, tự động thay thế trước khi tạo schema mới.)*

| Cột | Kiểu | Ràng buộc / Mô tả |
| :--- | :--- | :--- |
| `Id` | `INT IDENTITY(1,1)` | Khóa chính |
| `TokenHash` | `NVARCHAR(128)` | `UNIQUE`, SHA-256 của token 32-byte |
| `FlowType` | `NVARCHAR(30)` | `CHECK IN ('WebLoginByMobile', 'MobileLoginByWeb')` |
| `Status` | `NVARCHAR(20)` | `Pending`, `Approved`, `Consumed`, `Rejected`, `Expired` |
| `InitiatorUserId` | `NVARCHAR(450)` NULL | Người tạo phiên ở Chiều 2 (`FK -> AspNetUsers`) |
| `ApprovedUserId` | `NVARCHAR(450)` NULL | Người xác nhận ở Chiều 1 (`FK -> AspNetUsers`) |
| `DeviceInfo` | `NVARCHAR(300)` NULL | Thông tin trình duyệt/thiết bị hiển thị lúc Preview |
| `IpAddress` | `NVARCHAR(64)` NULL | Địa chỉ IP khởi tạo phiên |
| `CreatedAt` | `DATETIME2(7)` | Mặc định `SYSUTCDATETIME()` |
| `ExpiresAt` | `DATETIME2(7)` | `WebLoginByMobile`: `+120s`; `MobileLoginByWeb`: `+60s` |
| `ConsumedAt` | `DATETIME2(7)` NULL | Thời điểm đổi thành công lấy Auth Cookie |

### 2.2. Bảng `BookHandoverQrs` (QR Bàn giao & Hoàn trả Sách Vật lý)

| Cột | Kiểu | Ràng buộc / Mô tả |
| :--- | :--- | :--- |
| `Id` | `INT IDENTITY(1,1)` | Khóa chính |
| `LoanRequestId` | `INT` | `FK -> LoanRequests(Id) ON DELETE CASCADE` |
| `LoanId` | `INT` NULL | `FK -> Loans(Id)` (bắt buộc có khi `HandoverType = 'Return'`) |
| `HandoverType` | `NVARCHAR(20)` | `CHECK IN ('Handover', 'Return')` |
| `TokenHash` | `NVARCHAR(128)` | `UNIQUE`, SHA-256 của token QR (`BOOKSHARE-HANDOVER:v1:<token>`) |
| `ShortCodeHash` | `NVARCHAR(128)` | SHA-256 của mã dự phòng 6 ký tự (nhập tay nếu thiếu camera) |
| `CreatedByUserId` | `NVARCHAR(450)` | Người phát hành mã QR (`FK -> AspNetUsers`) |
| `ConsumedByUserId` | `NVARCHAR(450)` NULL | Người quét/nhập mã xác nhận (`FK -> AspNetUsers`) |
| `Status` | `NVARCHAR(20)` | `Pending`, `Consumed`, `Expired`, `Cancelled` |
| `CreatedAt` | `DATETIME2(7)` | Mặc định `SYSUTCDATETIME()` |
| `ExpiresAt` | `DATETIME2(7)` | Hiệu lực ngắn **10 phút** (`+600s`) |
| `ConsumedAt` | `DATETIME2(7)` NULL | Thời điểm xác nhận bàn giao / thu hồi thành công |

---

## 3. Luồng API Đăng Nhập QR 2 Chiều (`/api/qr-login`)

### Chiều 1: Điện thoại đã đăng nhập quét QR để đăng nhập Web (PC — TTL `120 giây`)
1. `POST /api/qr-login/create` (Công khai, `[ValidateAntiForgeryToken]`, giới hạn tần suất theo IP):
   - Sinh phiên `WebLoginByMobile` (`Status = "Pending"`, `ExpiresAt = +120s`).
   - Trả về `{ sessionId, token, qrPayload: "BOOKSHARE-QRLOGIN:v1:<token>", expiresInSeconds: 120 }`.
2. `GET /api/qr-login/status/{sessionId}` (Công khai):
   - Trả về `{ sessionId, status, remainingSeconds }` (tự chuyển `Expired` nếu quá hạn; **tuyệt đối không rò rỉ thông tin user**).
3. `POST /api/qr-login/preview` (`[Authorize]`, `[ValidateAntiForgeryToken]`):
   - Nhận `{ qrPayload }` hoặc `{ token }`, xác minh phiên `Pending` chưa hết hạn.
   - Trả về `{ sessionId, deviceInfo, ipAddress, remainingSeconds }` để hiển thị xác nhận trên điện thoại.
4. `POST /api/qr-login/approve` (`[Authorize]`, `[ValidateAntiForgeryToken]`):
   - Kiểm tra `LockoutEnd` của user hiện tại (từ chối nếu tài khoản đang bị khóa).
   - Nhận `{ sessionId, approve: true|false }` -> cập nhật `ApprovedUserId = currentUserId`, `Status = "Approved"` hoặc `"Rejected"`.
5. `POST /api/qr-login/claim` (Công khai cho trình duyệt đang chờ, `[ValidateAntiForgeryToken]`):
   - Nhận `{ sessionId, token }`.
   - Xác minh `FlowType == "WebLoginByMobile"`, `Status == "Approved"`, `ExpiresAt > UtcNow`, khớp `TokenHash`.
   - Kiểm tra lại `ApprovedUser.LockoutEnd > UtcNow` (nếu bị khóa, đánh dấu `Rejected` và trả `403`/`400`, **không cấp Cookie**).
   - Đặt `Status = "Consumed"`, `ConsumedAt = UtcNow`, gọi `HttpContext.SignInAsync` cấp Cookie phiên chuẩn Identity, ghi 1 thông báo bảo mật trong `Notifications`.

### Chiều 2: Web đã đăng nhập tạo QR để Mobile App quét đăng nhập nhanh (TTL `60 giây`)
1. `POST /api/qr-login/create-mobile-link` (`[Authorize]`, `[ValidateAntiForgeryToken]`):
   - Kiểm tra user không bị khóa `LockoutEnd`, tạo phiên `MobileLoginByWeb` (`InitiatorUserId = currentUserId`, `Status = "Approved"`, `ExpiresAt = +60s`).
   - Trả về `{ sessionId, token, qrPayload: "BOOKSHARE-MOBILELINK:v1:<token>", expiresInSeconds: 60 }`.
2. `POST /api/qr-login/mobile-claim` (Công khai cho App chưa đăng nhập, `[ValidateAntiForgeryToken]`):
   - Nhận `{ qrPayload }` hoặc `{ token }`.
   - Xác minh `FlowType == "MobileLoginByWeb"`, `Status == "Approved"`, `ExpiresAt > UtcNow`, user không bị `LockoutEnd`.
   - Đặt `Status = "Consumed"`, `ConsumedAt = UtcNow`, gọi `HttpContext.SignInAsync` cấp Cookie trực tiếp cho WebView App và tạo thông báo bảo mật trong `Notifications`.

---

## 4. Luồng API QR Bàn Giao & Hoàn Trả Sách (`/api/handover-qr`)

1. `POST /api/handover-qr/create` (`[Authorize]`, `[ValidateAntiForgeryToken]`):
   - Đầu vào `{ loanRequestId, handoverType: "Handover" | "Return" }`.
   - **Kiểm tra quyền tạo mã (Anti-IDOR):**
     - `LoanType == "Library"`: Chỉ `Librarian` hoặc `Admin` (ngược lại **`403`**).
     - `LoanType == "P2P"`: Chỉ chủ bản sao (`OwnerUserId == currentUserId`), `Curator` hoặc `Admin` (ngược lại **`403`**).
   - **Kiểm tra trạng thái hợp lệ:** `Handover` yêu cầu `LoanRequest.Status == "Approved"` (hoặc bản sao `InTransit`) và chưa tạo `Loan`; `Return` yêu cầu đã có `Loan` ở trạng thái `Active` hoặc `Overdue`.
   - Vô hiệu các mã QR cũ cùng `LoanRequestId + HandoverType` đang `Pending`, phát hành mã mới (TTL `10 phút`) kèm `shortCode` 6 ký tự.
2. `POST /api/handover-qr/confirm` (`[Authorize]`, `[ValidateAntiForgeryToken]`):
   - Đầu vào `{ qrPayload, shortCode, loanRequestId, returnCondition }`.
   - Xác minh tồn tại mã `Pending`, chưa hết hạn, chưa `Consumed` (chống dùng lại `400`).
   - **Kiểm tra quyền xác nhận (Anti-IDOR):**
     - `Handover`: Chỉ đúng Người mượn (`BorrowerId == currentUserId`) hoặc `Admin` được phép xác nhận nhận sách — mọi tài khoản khác nhận **`403 Forbidden`**.
     - `Return`: Chỉ Người mượn, Chủ bản sao, `Curator`, `Librarian` hoặc `Admin` liên quan tới đơn mới được xác nhận — người ngoài cuộc nhận **`403 Forbidden`**.
   - Cập nhật đồng bộ `BookHandoverQr.Status = "Consumed"`, trạng thái `Loan` / `BookCopy` (`Borrowed` kèm `+1 LendQuotaBonus` / `+3 TrustScore` cho P2P khi giao; hoặc `Returned` + `ConditionReport` + tự động đôn hàng chờ khi trả), và gửi thông báo cho hai bên.
