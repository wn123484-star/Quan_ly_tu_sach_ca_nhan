# BẢNG KIỂM KÊ THƯ VIỆN NỘI BỘ (LOCAL ASSETS AUDIT)

> **Mục tiêu:** 100% thư viện JS, CSS, Font, QR Generator, QR/Barcode Scanner và ảnh dự phòng của BookShare được tự host nội bộ trong `src/BookShare.Web/wwwroot/` — **0 phụ thuộc CDN ngoài** (`cdn.jsdelivr.net`, `cdnjs.cloudflare.com`, `unpkg.com`, `api.qrserver.com`, `chart.googleapis.com`, `fonts.googleapis.com`) khi chạy Local/Offline hoặc trên VPS Docker.

---

## 1. Danh Mục Thư Viện Tự Host (`wwwroot/vendor/`, `wwwroot/fonts/`, `wwwroot/images/`)

| Nhóm | Thư viện | Đường dẫn nội bộ | Dung lượng thực tế | Ghi chú sử dụng | Trạng thái |
| :--- | :--- | :--- | ---: | :--- | :---: |
| UI Framework | Bootstrap 5 CSS | `/vendor/bootstrap/bootstrap.min.css` | 232 KB | Nạp tại `_Layout.cshtml` & precache SW | Đã tự host |
| UI Framework | Bootstrap 5 Bundle JS | `/vendor/bootstrap/bootstrap.bundle.min.js` | 80 KB | Dropdown, Tab, Modal, Collapse | Đã tự host |
| Icon Pack | Bootstrap Icons CSS & Fonts | `/vendor/bootstrap-icons/font/bootstrap-icons.css` (`woff`, `woff2`) | 370 KB | Hệ biểu tượng mặc định toàn trang | Đã tự host |
| Alert & Dialog | SweetAlert2 JS | `/vendor/sweetalert2/sweetalert2.all.min.js` | 73 KB | Hộp thoại xác nhận, Toast, Scanner Modal | Đã tự host |
| Biểu đồ | Chart.js UMD | `/vendor/chartjs/chart.umd.js` | 206 KB | Biểu đồ `S03`, `S22` | Đã tự host |
| Sinh mã QR | QRCode JS (Canvas) | `/vendor/qr/qrcode.min.js` | 19.9 KB | Sinh QR Đăng nhập 2 chiều & QR Bàn giao sách | Đã tự host |
| Quét Camera QR/ISBN | Html5-Qrcode | `/vendor/qr/html5-qrcode.min.js` | 375.4 KB | Quét Camera/Chọn ảnh QR & mã vạch ISBN | Đã tự host |
| Hiệu ứng chúc mừng | Canvas Confetti | `/vendor/confetti/confetti.browser.min.js` | 10.8 KB | Chúc mừng lên hạng uy tín `TrustScore` | Đã tự host |
| Xuất PDF Admin | jsPDF UMD | `/vendor/jspdf/jspdf.umd.min.js` | 364.5 KB | Xuất báo cáo PDF tại `S22` | Đã tự host |
| Chụp DOM PDF | html2canvas | `/vendor/html2canvas/html2canvas.min.js` | 198.7 KB | Chụp bảng/biểu đồ xuất PDF tại `S22` | Đã tự host |
| Typography | Roboto Fonts (`Regular`/`Bold`) | `/fonts/Roboto-Regular.ttf`, `/fonts/Roboto-Bold.ttf` | 342 KB | Phông chữ chính tiếng Việt | Đã tự host |
| Ảnh dự phòng | Placeholder Bìa sách | `/images/book-placeholder.svg` | 1.4 KB | Thay thế hoàn toàn liên kết Unsplash khi thiếu bìa | Đã tự host |
| Ảnh dự phòng | Placeholder Avatar | `/images/avatar-default.svg` | 0.5 KB | Thay thế hoàn toàn liên kết Unsplash khi thiếu ảnh đại diện | Đã tự host |

---

## 2. Kết Quả Kiểm Chứng 0 CDN Ngoài

- **Quét tĩnh Pha 1 (`Views/`, `wwwroot/js/`, `wwwroot/modules/`, `wwwroot/css/`, `Services/`):**
  - `cdn.jsdelivr.net`: **0 kết quả**
  - `cdnjs.cloudflare.com`: **0 kết quả**
  - `unpkg.com`: **0 kết quả**
  - `api.qrserver.com` / `chart.googleapis.com` / `fonts.googleapis.com`: **0 kết quả**
  - `images.unsplash.com` trong fallback Views/Services: **0 kết quả** (100% chuyển về `/images/book-placeholder.svg` và `/images/avatar-default.svg`)
  - `CLEAN STATE`, `02_BookShare_SeedData.sql`, `book-tracker-ver07` / `VER07` trong Views/JS/CSS: **0 kết quả**
- **Kiểm chứng tự động Lớp 7 (`scripts/qa_full_bot.py`):** **PASS 100%** — xác nhận 7/7 tệp JS/SVG nội bộ tồn tại & trả `HTTP 200`, 0 liên kết CDN ngoài / Unsplash fallback và 0 chuỗi legacy (`CLEAN STATE`, `02_BookShare_SeedData.sql`, `book-tracker-ver07`) trong `Views/`, `wwwroot/js/`, `wwwroot/modules/`, `wwwroot/css/`.
