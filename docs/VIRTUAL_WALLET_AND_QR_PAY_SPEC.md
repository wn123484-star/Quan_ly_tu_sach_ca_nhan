# ĐẶC TẢ KỸ THUẬT VÍ TIỀN ẢO, GÓI HỘI VIÊN 4 CẤP & QUÉT QR THANH TOÁN

> **Nguyên tắc cốt lõi:** BookShare sử dụng **100% Tiền Ảo nội bộ** (VNĐ ảo mô phỏng học thuật, nạp miễn phí thoải mái trên web, tuyệt đối không kết nối ngân hàng thật hay cổng VNPay thật) để vận hành gói hội viên mượn sách, phí trễ hạn và ký quỹ bảo lãnh sách giấy (0đ giá bán sách).

---

## 1. Bảng Phân Cấp 4 Gói Hội Viên (`MembershipTierCatalog`)

| Tiêu chí | Cấp 1: `Free` (Độc Giả Cơ Bản) | Cấp 2: `Standard` (Hội Viên Đọc Sâu) | Cấp 3: `Premium` (Hội Viên Học Thuật) | Cấp 4: `Scholar` (Nghiên Cứu VIP) |
| :--- | :--- | :--- | :--- | :--- |
| **Giá 1 tháng (tiền ảo)** | `0đ` (Trọn đời) | `29.000đ` | `49.000đ` | `89.000đ` |
| **Giá 6 tháng (tiền ảo)** | `0đ` | `149.000đ` | `249.000đ` | `449.000đ` |
| **Giá 12 tháng (tiền ảo)** | `0đ` | `249.000đ` | `399.000đ` | `699.000đ` |
| **Hạn mức mượn đồng thời** | **2 cuốn** (+ thưởng P2P) | **3 cuốn** (+ thưởng P2P) | **5 cuốn** (+ thưởng P2P) | **10 cuốn** (+ thưởng P2P) |
| **Thời hạn mượn tối đa** | **14 ngày / lượt** | **21 ngày / lượt** | **30 ngày / lượt** | **45 ngày / lượt** |
| **Số lần gia hạn tối đa** | 1 lần (+7 ngày) | 2 lần (+7 ngày) | 2 lần (+10 ngày) | 3 lần (+14 ngày) |
| **Loại sách được mượn** | Kho chung Thư viện & sách P2P phổ thông (không bao gồm sách hiếm/sách hot có hàng chờ đông ≥ 3 người) | Toàn bộ Kho chung Thư viện + Tủ sách P2P Cộng đồng & sách chuyên ngành | Toàn bộ Kho Thư viện + P2P + Sách Chuyên Khảo / Giáo Trình Hiếm / Sách Hot | Toàn hệ sinh thái + Sách Hiếm / Tài liệu Nghiên cứu + Ưu tiên Trạm CLB |
| **Ưu tiên Hàng chờ Sách Hot (`S11`)** | Thường (Bậc 0) | Ưu tiên Bạc (Bậc 1) | **Ưu tiên Vàng** — Nhảy đầu hàng chờ (Bậc 2) | **Ưu tiên Cao nhất VIP** (Bậc 3) |
| **Quyền lợi bảo lãnh P2P** | Chuẩn | Chuẩn | Giảm 50% cọc bảo lãnh | Miễn 100% cọc bảo lãnh |

---

## 2. Thiết Kế CSDL (`UserWallets`, `Memberships`, `Transactions`)

1. **Bảng mới `dbo.UserWallets`:**
   - `UserId NVARCHAR(450) PK FK -> AspNetUsers(Id) ON DELETE CASCADE`
   - `Balance DECIMAL(18,2) NOT NULL DEFAULT(0) CHECK (Balance >= 0)`
   - `TotalToppedUp DECIMAL(18,2) NOT NULL DEFAULT(0)`
   - `TotalSpent DECIMAL(18,2) NOT NULL DEFAULT(0)`
   - `UpdatedAt DATETIME2(7) NOT NULL DEFAULT(SYSUTCDATETIME())`
2. **Mở rộng `dbo.Memberships`:**
   - `CK_Memberships_Tier`: `CHECK ([Tier] IN (N'Free', N'Standard', N'Premium', N'Scholar'))`
3. **Mở rộng `dbo.Transactions`:**
   - `CK_Transactions_Type`: `CHECK ([TransactionType] IN (N'Membership', N'Fine', N'SimulatedDeposit', N'WalletTopUp'))`
   - `CK_Transactions_Gateway`: `CHECK ([PaymentGateway] IN (N'VirtualWallet', N'QrPay', N'VNPaySandbox', N'MoMoSandbox'))`
   - Cột bổ sung nullable: `[TargetTier] NVARCHAR(20) NULL`, `[DurationMonths] INT NULL`, `[Description] NVARCHAR(300) NULL`.
   - Cả `01_BookShare_Schema.sql` và `DatabaseBootstrapService.EnsureExtensionTablesAsync()` đều chạy nâng cấp idempotent để VPS Docker sẵn có tự cập nhật không mất dữ liệu.

---

## 3. Luồng Nạp Tiền Ảo Miễn Phí & Quét QR Thanh Toán (Đồng Bộ Web ↔ Web App)

### 3.1 Nạp Tiền Ảo Miễn Phí (`POST /S17SandboxPayment/TopUpVirtualWallet`)
- Yêu cầu `[Authorize]` + `[ValidateAntiForgeryToken]`.
- Chấp nhận số tiền ảo từ `1.000đ` đến `10.000.000đ` mỗi lần nạp (không giới hạn số lần nạp, hoàn toàn miễn phí).
- Cộng vào `UserWallets.Balance` & `TotalToppedUp`, lưu 1 dòng `Transactions` (`TransactionType = "WalletTopUp"`, `PaymentGateway = "VirtualWallet"`, `PaymentStatus = "Completed"`).

### 3.2 Quy Trình Quét QR Thanh Toán Giống Ứng Dụng Ngân Hàng
1. **Tạo hóa đơn & Mã QR:**
   - Tại `S16` (chọn gói hội viên + nút **"Thanh toán ngay bằng Ví"** hoặc **"Tạo Mã QR Thanh Toán"**) hoặc `S17` (tạo hóa đơn phí phạt / ký quỹ / gói hội viên).
   - Định dạng mã QR nội bộ: `BOOKSHARE:PAY:{TransactionId}` (sinh hình bằng `/vendor/qr/qrcode.min.js` có `quietZone = 12px`).
   - Màn hình hiển thị mã QR trên Web gọi `GET /S17SandboxPayment/InvoiceStatus?txId={id}` mỗi `2.5s` để tự động nhận biết khi Web App / thiết bị khác hoàn tất thanh toán.
2. **Mở Camera Quét QR (Web & Android Web App):**
   - Nút **"Quét QR Thanh Toán"** trên Header, `S16`, `S17` và nút trung tâm `#btnMobileCenterQr` trên Mobile Bottom Nav đều nhận diện mã `BOOKSHARE:PAY:{txId}` (hỗ trợ Camera trực tiếp qua `/vendor/qr/html5-qrcode.min.js`, chọn ảnh chứa mã QR hoặc nhập mã `#TX-...`).
3. **Xem Hóa Đơn Trước Khi Thanh Toán (`POST /S17SandboxPayment/PreviewQrInvoice`):**
   - Kiểm tra đăng nhập & Anti-IDOR (chỉ chủ hóa đơn hoặc `Admin` xem/thanh toán).
   - Trả về hộp thoại **Hóa Đơn Thanh Toán** gồm: Mã hóa đơn, Nội dung dịch vụ, Hạng gói & kỳ hạn (nếu là gói hội viên), Số tiền cần trả, **Số dư Ví Tiền Ảo hiện tại**, **Số dư dự kiến sau thanh toán** và cờ `hasSufficientBalance`.
4. **Xác Nhận Thanh Toán (`POST /S17SandboxPayment/ConfirmQrInvoice`):**
   - Chỉ thực thi sau khi người dùng chủ động bấm nút **"Xác Nhận Thanh Toán"** trên màn hình hóa đơn.
   - Kiểm tra `PaymentStatus == "Pending"` (chặn thanh toán lặp `400`), kiểm tra `Balance >= Amount` (nếu thiếu trả `400` kèm hướng dẫn nạp thêm tiền ảo miễn phí).
   - Trừ `UserWallets.Balance`, cộng `TotalSpent`, cập nhật `Transactions.PaymentStatus = "Completed"`, ký mã đối soát nội bộ, kích hoạt ngay quyền lợi gói hội viên (`Standard`/`Premium`/`Scholar`) hoặc gạch nợ phí trễ `S12`/bồi thường `S19` trong cùng 1 thao tác lưu CSDL.

---

## 4. Dọn Cấu Hình Ngân Hàng Thật (`.env.example`)
- Xóa sạch 4 dòng `VNPay__TmnCode`, `VNPay__HashSecret`, `VNPay__BaseUrl`, `VNPay__ReturnUrl` khỏi `.env.example` (thay bằng ghi chú hệ thống Ví Tiền Ảo nội bộ không cần khóa ngân hàng ngoài).
- Loại bỏ hoàn toàn việc đọc khóa `VNPay:*` trong `S17SandboxPaymentService.cs` (dùng khóa ký nội bộ `BookShare:InternalInvoiceSecret` cục bộ của ứng dụng).
