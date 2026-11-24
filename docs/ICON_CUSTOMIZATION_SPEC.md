# ĐẶC TẢ KỸ THUẬT HỆ THỐNG TÙY BIẾN BIỂU TƯỢNG TOÀN TRANG (ADMIN ICON MANAGER)

> **Phạm vi:** Cho phép Quản trị viên (`Admin`) tại phân hệ `/S22AdminAnalytics` tải ảnh tùy chỉnh cho từng biểu tượng trên toàn cổng BookShare và chuyển đổi tức thì giữa **Mặc định hệ thống (Gạt Trái)** và **Ảnh tùy chỉnh (Gạt Phải)** mà không cần khởi động lại máy chủ.

---

## 1. Thiết Kế Cơ Sở Dữ Liệu (`SystemIconSettings`)

Bảng `dbo.SystemIconSettings` được bổ sung vào `database/01_BookShare_Schema.sql` (kèm đảm bảo tự tạo an toàn trong `DatabaseBootstrapService` khi nâng cấp CSDL sẵn có trên Docker VPS):

```sql
IF OBJECT_ID(N'[dbo].[SystemIconSettings]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SystemIconSettings] (
        [IconKey]          NVARCHAR(64)  NOT NULL,
        [DisplayName]      NVARCHAR(120) NOT NULL,
        [CategoryGroup]    NVARCHAR(60)  NOT NULL,
        [DefaultIconClass] NVARCHAR(100) NOT NULL,
        [CustomImageUrl]   NVARCHAR(300) NULL,
        [UseCustomImage]   BIT           NOT NULL CONSTRAINT [DF_SystemIconSettings_UseCustom] DEFAULT (0),
        [UpdatedAt]        DATETIME2(7)  NOT NULL CONSTRAINT [DF_SystemIconSettings_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
        [UpdatedByUserId]  NVARCHAR(450) NULL,
        CONSTRAINT [PK_SystemIconSettings] PRIMARY KEY CLUSTERED ([IconKey] ASC)
    );
END
```

- **Quy ước trạng thái công tắc gạt (`UseCustomImage`):**
  - `0` (**Gạt Trái — Mặc định**): Hiển thị biểu tượng Bootstrap Icons (`DefaultIconClass`) hoặc ảnh nền mặc định cục bộ. Vẫn giữ nguyên đường dẫn `CustomImageUrl` đã tải lên để Admin có thể gạt lại sang phải mà không phải upload lại.
  - `1` (**Gạt Phải — Ảnh tùy chỉnh**): Chỉ cho phép bật khi `CustomImageUrl` hợp lệ (khác rỗng). Nếu chưa có ảnh tải lên mà cố gạt sang phải -> trả `HTTP 400 Bad Request` kèm thông báo *"Vui lòng tải ảnh tùy chỉnh lên trước khi gạt sang chế độ Ảnh tùy chỉnh."*

---

## 2. Danh Mục 18 Biểu Tượng Hệ Thống Mặc Định

| STT | `IconKey` | Tên hiển thị tiếng Việt | Nhóm (`CategoryGroup`) | Lớp mặc định (`DefaultIconClass`) | Vị trí áp dụng |
| :---: | :--- | :--- | :--- | :--- | :--- |
| 1 | `brand_logo` | Logo chính BookShare | Thương hiệu | `bi-book-half` | Header chính & Splash Screen |
| 2 | `brand_favicon` | Biểu tượng tab trình duyệt (Favicon) | Thương hiệu | `bi-bookmark-star-fill` | `<link rel="icon">` trong `_Layout.cshtml` |
| 3 | `nav_home` | Trang Chủ | Thanh điều hướng | `bi-compass` | Utility bar & Bottom Nav |
| 4 | `nav_discover` | Khám Phá | Thanh điều hướng | `bi-globe2` | Menu Khám Phá trên Header |
| 5 | `nav_shelf` | Tủ Sách & Đọc | Thanh điều hướng | `bi-bookshelf` | Menu Tủ Sách & Bottom Nav |
| 6 | `nav_loan` | Mượn Trả & P2P | Thanh điều hướng | `bi-arrow-left-right` | Menu Mượn Trả trên Header |
| 7 | `nav_community` | Ví & Cộng Đồng | Thanh điều hướng | `bi-gem` | Menu Ví & Cộng Đồng |
| 8 | `nav_admin` | Điều Hành Hệ Thống | Thanh điều hướng | `bi-shield-lock-fill` | Menu Điều Hành (Admin/Librarian/Curator) |
| 9 | `nav_cart_shelf` | Tủ Sách & Phiếu Mượn | Thanh điều hướng | `bi-journal-bookmark-fill` | Cụm Kệ Sách góc phải Header chính |
| 10 | `quick_library` | Kho Chung Thư Viện | Lối tắt & Kho sách | `bi-buildings-fill` | Lối tắt nhanh dưới thanh tìm kiếm |
| 11 | `quick_p2p` | Tủ Sách P2P Cộng Đồng | Lối tắt & Kho sách | `bi-people-fill` | Lối tắt nhanh dưới thanh tìm kiếm |
| 12 | `quick_filter` | Bộ Lọc Đa Tiêu Chí | Lối tắt & Kho sách | `bi-funnel-fill` | Lối tắt nhanh & Trang chủ |
| 13 | `quick_isbn` | Biên Mục Mã ISBN | Lối tắt & Kho sách | `bi-upc-scan` | Lối tắt nhanh & Trang chủ |
| 14 | `quick_hot` | Hàng Chờ Sách Hot | Lối tắt & Kho sách | `bi-fire` | Lối tắt nhanh & Trang chủ |
| 15 | `ai_fab` | Trợ Lý AI Thủ Thư | AI & Tiện ích | `bi-stars` | Nút nổi tròn & đầu khung chat AI |
| 16 | `qr_scan` | Quét Mã QR Đa Năng | AI & Tiện ích | `bi-qr-code-scan` | Nút trung tâm Bottom Nav & Tab QR |
| 17 | `book_placeholder` | Ảnh Bìa Sách Mặc Định | Lối tắt & Kho sách | `bi-book` | Ảnh dự phòng bìa sách khi chưa có bìa |
| 18 | `avatar_default` | Ảnh Đại Diện Mặc Định | AI & Tiện ích | `bi-person-circle` | Ảnh dự phòng tài khoản thành viên |

---

## 3. Quy Chuẩn Bảo Mật Upload & Bộ Nhớ Đệm (`IMemoryCache`)

1. **Phân quyền & CSRF:** Toàn bộ API quản lý biểu tượng trong `S22AdminAnalyticsController` bắt buộc gắn `[Authorize(Roles = "Admin")]` và `[ValidateAntiForgeryToken]`:
   - `GET /S22AdminAnalytics/GetIconSettings`
   - `POST /S22AdminAnalytics/UploadCustomIcon` (`multipart/form-data`: `iconKey`, `file`)
   - `POST /S22AdminAnalytics/ToggleIconMode` (`{ iconKey, useCustomImage }`)
   - `POST /S22AdminAnalytics/ResetIconToDefault` (`{ iconKey }` — chấp nhận 1 mã hoặc `"ALL"`)
2. **Xác thực tệp tải lên (`<= 2MB`):**
   - Phần mở rộng cho phép: `.png`, `.jpg`, `.jpeg`, `.webp`, `.svg`.
   - Kiểm tra **magic bytes** với ảnh raster (`PNG`: `89 50 4E 47`, `JPEG`: `FF D8 FF`, `WEBP`: `RIFF....WEBP`).
   - Với `.svg`: đọc văn bản UTF-8, bắt buộc chứa thẻ `<svg`, chặn tuyệt đối `<script`, `<iframe`, `<object`, `<embed`, `<foreignObject`, `javascript:`, và thuộc tính sự kiện `on[a-z]+\s*=`.
   - Lưu tại `wwwroot/uploads/icons/{safeIconKey}_{yyyyMMddHHmmssfff}.{ext}` (nằm trọn trong Docker volume `./uploads:/app/wwwroot/uploads`), tự động xóa tệp cũ của cùng `IconKey`.
3. **Cache & Hiển thị Tức Thì (`IMemoryCache`):**
   - `SystemIconService` lưu dictionary biểu tượng vào `IMemoryCache` (`bookshare:system-icons`, TTL 15 phút), lập tức xóa cache (`_cache.Remove(...)`) sau mỗi thao tác Upload / Toggle / Reset.
   - Helper `RenderIconHtml(iconKey, fallbackClass, extraCss)`:
     - Khi dùng ảnh tùy chỉnh: render `<img src="..." alt="..." class="bs-sys-icon ..." onerror="this.style.display='none';if(this.nextElementSibling)this.nextElementSibling.classList.remove('d-none');" /><i class="bi ... d-none" aria-hidden="true"></i>` (bảo đảm 100% thẻ `<img>` luôn có `alt` và `onerror` fallback an toàn).
     - Khi dùng mặc định: render `<i class="bi {DefaultIconClass} {extraCss}" aria-hidden="true"></i>`.

---

## 4. Kết Quả Kiểm Chứng Tự Động (Lớp 7 — `scripts/qa_full_bot.py`)

- **RBAC:** Tài khoản `User` bị chặn `HTTP 403` trên cả `GET GetIconSettings`, `POST ToggleIconMode`, `POST ResetIconToDefault`.
- **Danh mục 18 biểu tượng:** `GET GetIconSettings` trả đúng `18` mục chuẩn.
- **Chặn bật khi chưa có ảnh:** `ToggleIconMode("brand_logo", true)` trước khi upload trả `HTTP 400`.
- **Chống tệp nguy hiểm & SVG XSS:** Upload `.exe` giả mạo và `.svg` chứa `<script>` bị chặn `HTTP 400`.
- **Áp dụng tức thì & Công tắc gạt 2 chiều:** Upload PNG hợp lệ -> `HTTP 200`, trang chủ `/` hiển thị ngay `<img src="/uploads/icons/brand_logo_..."` có `alt` + `onerror`; gạt **Trái (`false`)** lập tức trả về biểu tượng mặc định; gạt **Phải (`true`)** lập tức khôi phục ảnh tùy chỉnh mà không cần tải lại; `ResetIconToDefault("ALL")` dọn sạch về mặc định `HTTP 200`.
