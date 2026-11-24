# HƯỚNG DẪN TRIỂN KHAI & CẤU HÌNH GOOGLE OAUTH 2.0 — BOOKSHARE

Tài liệu hướng dẫn thiết lập môi trường chạy cục bộ (Windows/LocalDB), triển khai Docker Compose (`SQL Server 2022` + `.NET 10`) và kích hoạt đầy đủ **Google OAuth 2.0**, **Resend Email API**, **Gemini AI**, **reCAPTCHA v3**.

---

## 1. Biến Môi trường (`.env`)

`Program.cs` gọi `DotNetEnv.Env.TraversePath().Load()` trước khi build cấu hình ASP.NET Core, ánh xạ dấu `__` sang phân cấp cấu hình chuẩn (`Section:Key`). Tạo file `.env` tại thư mục gốc dựa trên `.env.example`:

```ini
# Cổng và mật khẩu SQL Server (dùng cho Docker Compose)
ASPNETCORE_URLS="http://+:5087"
SA_PASSWORD="BookShare@2026StrongPass!"

# Chuỗi kết nối SQL Server (chạy Local trên Windows)
ConnectionStrings__DefaultConnection="Server=(localdb)\\MSSQLLocalDB;Database=BookShareDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;"

# Gemini AI (Dịch vụ S13 & S14 - để trống dùng chế độ RAG cục bộ thông minh)
Gemini__ApiKey=""
Gemini__Model="gemini-2.5-flash"

# Resend Email API (OTP S01 & Nhắc hạn S18)
Resend__ApiKey=""
Resend__FromEmail="BookShare <onboarding@resend.dev>"

# SMTP dự phòng nếu không dùng Resend
Smtp__Host=""
Smtp__Port="587"
Smtp__User=""
Smtp__Pass=""
Smtp__FromEmail=""

# Google OAuth 2.0 thật (S01)
Authentication__Google__ClientId=""
Authentication__Google__ClientSecret=""

# VNPay Sandbox HMAC-SHA512 (S16 & S17)
VNPay__TmnCode=""
VNPay__HashSecret=""
VNPay__BaseUrl="https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"
VNPay__ReturnUrl="http://localhost:5087/S17SandboxPayment"

# Google reCAPTCHA v3 (Chống bot đăng ký, đăng nhập, OTP, đặt mượn)
ReCaptcha__SiteKey=""
ReCaptcha__SecretKey=""
```

> **Lưu ý:** Tuyệt đối không commit tệp `.env` thật lên Git (`.gitignore` đã loại trừ `.env`).

---

## 2. Chạy Cục bộ với SQL Server LocalDB (Windows)

```powershell
# 1. Nạp Schema 22 bảng + dữ liệu mẫu khởi tạo
sqlcmd -S "(localdb)\MSSQLLocalDB" -i "database/01_BookShare_Schema.sql"
sqlcmd -S "(localdb)\MSSQLLocalDB" -i "database/02_BookShare_SeedData.sql"

# 2. Biên dịch & khởi chạy ứng dụng
dotnet build BookShare.sln --nologo
dotnet run --project src/BookShare.Web/BookShare.Web.csproj
```

---

## 3. Triển khai bằng Docker Compose & Nạp Dữ Liệu Mẫu 1 Chạm

Tệp [`docker-compose.yml`](../docker-compose.yml) và [`Dockerfile`](../Dockerfile) đã tích hợp sẵn:
- **`sqlserver`**: SQL Server 2022 Express (cổng host `127.0.0.1:1437`, volume `bookshare_sql_data`).
- **`db-init`**: Mặc định chạy **chỉ schema (`01_BookShare_Schema.sql`)** giúp khởi tạo hệ thống sạch sẽ khi triển khai thực tế; `EnsureCoreAdminBootstrapAsync()` tự động khởi tạo 4 vai trò chuẩn và tài khoản Quản trị viên gốc `admin@bookshare.vn`.
- **`bookshare-web`**: Ứng dụng .NET 10 chạy cổng `5087`, mount `./uploads:/app/wwwroot/uploads` (giữ ảnh kiểm định `S10`) và `./database:/sql:ro`.

```powershell
docker compose up -d --build
```
Truy cập hệ thống tại `http://127.0.0.1:5087`:
- **Nếu cần nạp bộ dữ liệu mẫu (35 đầu sách, 55 bản sao, 6 tài khoản):** Đăng nhập bằng `admin@bookshare.vn`, vào mục **Quản trị Hệ thống** (`/S22AdminAnalytics`) và bấm nút **"Nạp Dữ Liệu Mẫu Cộng Đồng (1 Chạm)"** (`ManageSampleData: SeedSampleData`). Hệ thống tự động nạp `02_BookShare_SeedData.sql` qua mount `/sql:ro`.

---

## 4. Hướng dẫn Cấu hình Google OAuth 2.0 Thật (Fail-Closed)

Mặc định khi chưa có khóa thật, BookShare khóa cứng nút Google trên `/S01AuthOtpGoogle` và trả về `HTTP 400` cho `/S01AuthOtpGoogle/GoogleLogin`. Để kích hoạt Đăng nhập Google thật:

1. Truy cập **Google Cloud Console** -> **APIs & Services** -> **Credentials**.
2. Cấu hình **OAuth consent screen** (External hoặc Internal theo tên miền trường ĐH CMC), thêm scope `email`, `profile`.
3. Tạo **OAuth 2.0 Client ID** (loại *Web application*):
   - **Authorized JavaScript origins**:
     - Local dev: `https://localhost:7195`, `http://localhost:5062`, `http://localhost:5087`
     - Production: `https://<ten-mien-cua-ban>`
   - **Authorized redirect URIs** (bắt buộc khớp CallbackPath mặc định của ASP.NET Core Google Auth `/signin-google`):
     - Local dev: `https://localhost:7195/signin-google`, `http://localhost:5062/signin-google`, `http://localhost:5087/signin-google`
     - Production: `https://<ten-mien-cua-ban>/signin-google`
4. Điền `Client ID` và `Client Secret` thật (không chứa từ giữ chỗ `demo`, `xxxx`, `your-`, `DIEN_KEY`) vào `.env`:
   ```ini
   Authentication__Google__ClientId="1234567890-abcdefg.apps.googleusercontent.com"
   Authentication__Google__ClientSecret="GOCSPX-your-real-secret-value"
   ```
5. Khởi động lại ứng dụng (`dotnet run` hoặc `docker compose restart bookshare-web`):
   - Nút **"Tiếp tục bằng Google"** tại `/S01AuthOtpGoogle` sẽ tự động kích hoạt.
   - Người dùng đăng nhập qua Google sẽ tự liên kết bảng `AspNetUserLogins` và tự động tạo thành viên role `"User"` nếu email chưa tồn tại (vẫn thực thi kiểm tra `LockoutEnd` trước khi cấp Cookie).

---

## 5. Checklist Trước Khi Đưa Lên Môi Trường Thực Tế (Production)

- [ ] Đổi toàn bộ mật khẩu tài khoản khởi tạo (`Admin@123`, `Librarian@123`, `Curator@123`, `User@123`) ngay sau khi triển khai.
- [ ] Đặt `VNPay__HashSecret` đủ dài và ngẫu nhiên (tối thiểu 32 ký tự); cập nhật `VNPay__ReturnUrl` theo tên miền HTTPS thật.
- [ ] Bật HTTPS tại Reverse Proxy (Nginx/Caddy) kèm `X-Forwarded-Proto` và header bảo mật TLS.
- [ ] Cấu hình `Resend__ApiKey` (hoặc SMTP) để gửi OTP xác minh email thật qua `S01`/`S18` thay vì chế độ hiển thị mã thử nghiệm ở môi trường phát triển.
- [ ] Cấu hình `Gemini__ApiKey` nếu muốn dùng LLM trực tuyến (nếu chưa cấu hình, trợ lý AI tự chuyển sang tra cứu dữ liệu sách nội bộ).

---

## 6. Đóng gói & Tải Ứng Dụng Android (`BookShare-Mobile-Android.apk`) Qua GitHub Actions

Ứng dụng Android Native WebView Shell nằm trong `mobile/bookshare-android/`, hiển thị 100% giao diện BookShare Light Mode (`#F8FAFC`), **không hiện thanh URL** và **không hiển thị hộp thoại hỏi URL khi mở ứng dụng** (URL máy chủ được nhúng trực tiếp lúc biên dịch vào `BuildConfig.BASE_URL`).

### Bước 1: Thiết lập URL Máy chủ cho Bản APK
Có 2 cách truyền URL máy chủ BookShare khi đóng gói APK:
1. **Truyền trực tiếp lúc chạy tay (`workflow_dispatch`):** Vào tab **Actions** trên GitHub -> chọn workflow **Build BookShare Android APK** -> bấm **Run workflow** -> điền ô `base_url` (ví dụ `https://bookshare.tenmien.vn` hoặc IP LAN `http://192.168.1.25:5087`, mặc định `http://10.0.2.2:5087` cho Android Emulator).
2. **Đặt biến cố định cho mọi lần push:** Trong GitHub Repo -> **Settings** -> **Secrets and variables** -> **Actions** -> tab **Variables** -> tạo biến tên `BOOKSHARE_BASE_URL` mang URL máy chủ thật.

### Bước 2: Tải Tệp APK Sau Khi Build
1. Sau khi workflow **Build BookShare Android APK** (`.github/workflows/build-bookshare-android-apk.yml`) chạy xong với trạng thái xanh, mở chi tiết Run tương ứng.
2. Kéo xuống mục **Artifacts** cuối trang và tải gói **`BookShare-Mobile-Android`** (lưu giữ 30 ngày), giải nén lấy tệp `BookShare-Mobile-Android.apk` để cài đặt trên thiết bị Android (`Android 8.0+`, API `26+`).
3. Trên thiết bị thật, bật **Cài đặt ứng dụng từ nguồn không xác định** cho trình quản lý tệp, cài APK và cấp quyền Camera / Thông báo khi app đề nghị.
