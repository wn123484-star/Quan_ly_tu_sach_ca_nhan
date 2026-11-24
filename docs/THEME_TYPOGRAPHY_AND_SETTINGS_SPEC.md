# ĐẶC TẢ HỆ THỐNG ĐA GIAO DIỆN BOOTSTRAP, FONT CHỮ NỘI BỘ, CÀI ĐẶT TÀI KHOẢN & TRỢ LÝ AI BỀN VỮNG (SPRINT 5)

## 1. HỆ THỐNG ĐA PHONG CÁCH & MÀU SẮC (100% OFFLINE LOCAL)

### 1.1. 5 Phong cách Giao diện Bootstrap (UI Style — nạp cục bộ không CDN)
Áp dụng bằng cách hoán đổi động tệp stylesheet `<link id="bsBootstrapThemeLink" />` và thuộc tính `html[data-ui-style]`:

| Mã Style | Tên hiển thị tiếng Việt | Tệp CSS cục bộ | Đặc điểm nổi bật |
| :--- | :--- | :--- | :--- |
| `standard` | Chuẩn Hiện Đại (Mặc định) | `/vendor/bootstrap/bootstrap.min.css` | Cân bằng, gọn gàng, tối ưu tốc độ đọc |
| `sketchy` | Nét Vẽ Nghệch Ngoặc (Sketchy) | `/vendor/bootswatch/sketchy.min.css` | Đường viền vẽ tay phá cách, phong cách sổ tay phác thảo |
| `flatly` | Phẳng Sắc Nét (Flatly) | `/vendor/bootswatch/flatly.min.css` | Khối màu phẳng, đường nét dứt khoát |
| `morph` | Kính Mờ Mềm Mại (Morph) | `/vendor/bootswatch/morph.min.css` | Khối nổi nhẹ nhàng (Neumorphism) hiện đại |
| `cyborg` | Đêm Công Nghệ (Cyborg) | `/vendor/bootswatch/cyborg.min.css` | Độ tương phản cao, sắc công nghệ tương lai |

> **Quy tắc 0 CDN:** Toàn bộ dòng `@import url(https://fonts.googleapis.com/...)` trong `wwwroot/vendor/bootswatch/*.min.css` phải bị gỡ bỏ hoàn toàn để ứng dụng không gửi yêu cầu font ra Internet.

### 1.2. 10 Bảng màu (Color Palette) & Header Đổ Màu Cân Đối
Thanh Header `.portal-header-wrapper` chuyển từ dải chéo `135deg` (gây lệch tối bên trái / chói bên phải) sang **dải chuyển dọc cân đối (`180deg`)** với độ tương phản chữ trắng đồng đều toàn chiều ngang:

| Mã Palette | Tên hiển thị | Tông màu chủ đạo |
| :--- | :--- | :--- |
| `blue` | Xanh Sapphire (Mặc định) | `#1e3a8a` → `#1d4ed8` |
| `ocean` | Xanh Đại Dương | `#0c4a6e` → `#0284c7` |
| `emerald` | Lục Bảo Thư Viện | `#065f46` → `#059669` |
| `gold` | **Vàng Mộng Mơ** | `#92400e` → `#d97706` (nền ấm `#fffbeb`) |
| `rose` | **Hồng Ấm Áp** | `#9d174d` → `#e11d48` (nền dịu `#fff1f2`) |
| `lavender` | Tím Oải Hương | `#5b21b6` → `#7c3aed` (nền nhẹ `#f5f3ff`) |
| `sepia` | Giấy Cổ Thư Viện | `#713f12` → `#a16207` (nền giấy `#fefce8`) |
| `coral` | Cam Hoàng Hôn | `#9a3412` → `#ea580c` |
| `royal` | Tím Hoàng Gia | `#3730a3` → `#4f46e5` |
| `midnight` | Đêm Ngân Hà | `#0f172a` → `#1e293b` |

### 1.3. 5 Bộ Font chữ tiếng Việt nội bộ (`wwwroot/fonts/`) & 4 Cỡ chữ
| Mã Font | Tên hiển thị | Tệp cục bộ (`wwwroot/fonts/`) |
| :--- | :--- | :--- |
| `roboto` | Roboto (Chuẩn hệ thống) | `Roboto-Regular.ttf`, `Roboto-Bold.ttf` |
| `bevietnam` | Be Vietnam Pro (Hiện đại) | `BeVietnamPro-Regular.ttf`, `BeVietnamPro-Bold.ttf` |
| `lora` | Lora (Sách giấy có chân) | `Lora-Regular.ttf`, `Lora-Bold.ttf` |
| `patrick` | Patrick Hand (Viết tay nghệch ngoặc) | `PatrickHand-Regular.ttf` |
| `jetbrains` | JetBrains Mono (Đơn cách) | `JetBrainsMono-Regular.ttf`, `JetBrainsMono-Bold.ttf` |

- **Cỡ chữ (`data-font-size`)**: `sm` (`14px`), `md` (`16px` — mặc định), `lg` (`18px`), `xl` (`20px`).
- **Lưu bền vững**: Lưu đồng thời vào `localStorage` (`bookshare_ui_prefs`) để áp dụng tức thì không nháy hình (Zero-FOUC) VÀ đồng bộ vào cột `AspNetUsers.UiPreferencesJson` khi người dùng đã đăng nhập (`POST /S01AuthOtpGoogle/SaveUiPreferences`).

---

## 2. CÀI ĐẶT TÀI KHOẢN MỌI CẤP — PHÂN TÁCH `Tên hiển thị` vs `Email`

- **Tên đăng nhập (`UserName`)**: Định danh đăng nhập cố định (hiển thị riêng, không ghi đè khi đổi Email nhận thư để Quản trị viên/Thành viên không mất tên đăng nhập quen thuộc).
- **Tên hiển thị (`FullName`)**: Tên gọi hiển thị trên Header, bình luận, phiếu mượn, hội thoại AI — tách bạch hoàn toàn với địa chỉ Email (không cho phép để trống).
- **Email nhận thư & thông báo (`Email`)**: Mọi cấp (`User`, `Curator`, `Librarian`, `Admin`) đều có thể cập nhật trong trang **Hồ Sơ & Cài Đặt** (`/S01AuthOtpGoogle`).
  - Kiểm tra đúng cú pháp Email hợp lệ và không trùng với tài khoản khác.
  - Khi đổi sang Email mới: cập nhật `Email` & `NormalizedEmail`, đặt `EmailConfirmed = false` để khuyến khích xác minh OTP, đồng thời làm mới Cookie Claim ngay lập tức.
- **Chốt chặn bắt buộc Email khi gửi thư**:
  - Trước mọi thao tác gửi Email (OTP, thông báo nhắc hạn cá nhân, gửi tóm tắt hội thoại AI), hệ thống kiểm tra `Email` của người nhận phải khác rỗng và đúng định dạng hợp lệ.
  - Nếu thiếu hoặc không hợp lệ -> trả về `HTTP 400` kèm `requiresEmailSetup: true` và nút mở nhanh trang **Cài Đặt Tài Khoản** để bổ sung Email trước khi gửi.

---

## 3. LỊCH SỬ CHAT AI BỀN VỮNG, NÚT RESET CHAT & GỬI EMAIL TỪ CHAT

1. **Tải lịch sử bền vững khi mở/tải lại trang (`GET /api/ai-chat/history`)**:
   - Người dùng đã đăng nhập: tự động nạp 30 tin nhắn gần nhất của phiên hoạt động thuộc đúng `UserId` từ `dbo.AiChatMessages`, kèm nhãn vai trò (`User`/`Curator`/`Librarian`/`Admin`) và trạng thái Email.
   - Khách chưa đăng nhập: lưu tạm trên `sessionStorage` riêng của phiên khách để tải lại trang vẫn giữ hội thoại.
2. **Nút Đặt lại cuộc trò chuyện (Reset Chat)**:
   - Endpoint `POST /api/ai-chat/reset` (và `POST /S13GeminiAiAssistant/ResetChat`): xóa sạch tin nhắn cũ của phiên hiện tại thuộc `UserId` (Anti-IDOR), tạo lời chào mới phù hợp vai trò và làm sạch khung chat ngay lập tức.
   - Có mặt trên cả **Widget Trợ lý AI góc phải** lẫn trang toàn màn hình **`/S13GeminiAiAssistant`**.
3. **Nâng cấp trải nghiệm Trợ lý AI**:
   - Thanh công cụ chọn nhanh **Style / Bảng màu / Font / Cỡ chữ** trực tiếp hoặc ra lệnh bằng tiếng Việt trong khung chat (VD: *"đổi sang nét vẽ nghệch ngoặc"*, *"chọn màu vàng mộng mơ"*, *"đổi màu hồng ấm áp"*, *"tăng cỡ chữ lớn"*, *"đổi font viết tay"*).
   - Nút **Gửi tóm tắt hội thoại qua Email** (`POST /S13GeminiAiAssistant/SendChatSummaryEmail`): kiểm tra Email hợp lệ trước khi gửi qua `IResendEmailService`, nếu chưa có Email sẽ yêu cầu bổ sung trong Cài Đặt.

---

## 4. POPUP THÔNG BÁO NHANH & KHẮC PHỤC HUY HIỆU CHỐNG BOT reCAPTCHA V3

- **Popup Thông Báo Nhanh**:
  - Endpoint `GET /S18NotificationHub/RecentPopup`: trả về `unreadCount` + 8 thông báo mới nhất của chính người đăng nhập, mỗi bản ghi gắn sẵn `detailUrl` tương ứng loại thông báo (`DueReminder`/`LoanApproved` -> `/S12LoanRenewalFine`, `QueueAvailable` -> `/S11HotBookQueue`, `Dispute` -> `/S19DisputeResolution`, `Membership` -> `/S16MembershipTier`, `Wallet`/`Payment` -> `/S17SandboxPayment`, mặc định `/S18NotificationHub?highlight={id}`).
  - Bấm chuông trên Header mở ngay Popover gọn đẹp; bấm vào từng thông báo sẽ tự đánh dấu đã đọc, mở modal xem nhanh đầy đủ nội dung kèm nút **"Đi tới trang chi tiết"** và nút **"Xem tất cả thông báo"**.
- **Huy hiệu chống bot Google reCAPTCHA v3**:
  - Thu gọn tỷ lệ `scale(0.76)` và neo gọn vào cạnh **góc trái dưới** (`left: -186px`, trượt ra nhẹ khi di chuột vào) thay vì góc phải dưới, bảo đảm **0% chồng đè** lên nút Trợ lý AI (`#aiFloatingWidget`) trên cả Desktop, Mobile và Android Web App.
