# KIẾN TRÚC ỨNG DỤNG ANDROID BOOKSHARE MOBILE & CI/CD GITHUB ACTIONS

> **Tài liệu Sprint 3 → v4.0.0 — Android WebView Shell Gốc Không Hỏi URL, Đồng Bộ `https://bookshare.castle-tech.cloud`, Chống Kẹt Trắng Trang & Xin Quyền Camera Theo Nhu Cầu**

---

## 1. Nguyên Tắc Thiết Kế Cốt Lõi

1. **Hiển thị 100% Giao diện Web BookShare Light Mode:**
   - Sử dụng trực tiếp `Theme.AppCompat.Light.NoActionBar` với nền gốc & màn hình chờ **`#F8FAFC`**, thanh trạng thái (Status Bar) icon tối trên nền sáng (`android:windowLightStatusBar`).
   - **Tuyệt đối không hiện thanh tiêu đề/thanh địa chỉ URL trên App**, và **tuyệt đối không hiện hộp thoại (`AlertDialog` / `EditText`) hỏi nhập URL khi mở App**.
   - Khi khởi động, App hiển thị lớp chờ Native `#loadingOverlay` (logo BookShare + trạng thái kết nối + nút **Thử tải lại** nếu mạng chậm sau 7 giây) và tự tắt ngay tại `onPageCommitVisible` (pixel đầu tiên) cùng cơ chế **Timeout Watchdog 20 giây** chuyển an toàn sang `offline.html` có nút thử lại trang vừa mở — triệt tiêu hoàn toàn lỗi trắng tinh kẹt thanh tiến trình.
   - Khi phát hiện User-Agent `BookShareAndroidApp/1.0`, `_Layout.cshtml` tự tắt `#appSplash` web để tránh hiện 2 màn hình chờ chồng nhau.
2. **Khóa Cứng URL Lúc Biên Dịch (`BuildConfig.BASE_URL`):**
   - Tệp `mobile/bookshare-android/gradle.properties`, `mobile/bookshare-android/app/build.gradle` và workflow GitHub Actions đều đồng bộ mặc định tới **`https://bookshare.castle-tech.cloud`** (vẫn cho phép ghi đè qua `-PBOOKSHARE_BASE_URL=...` hoặc biến môi trường khi cần kiểm thử nội bộ), đưa vào `buildConfigField("String", "BASE_URL", ...)`.
   - `MainActivity.java` tải thẳng `BuildConfig.BASE_URL` ngay khi khởi động và hỗ trợ kéo xuống làm mới (tự tải lại `lastAttemptedUrl` nếu đang ở `offline.html`, đồng thời khóa kéo làm mới khi đang mở camera quét QR).
3. **Nhận diện App & Thanh Điều Hướng Đáy (Bottom Navigation Bar):**
   - `WebSettings` gắn hậu tố **`BookShareAndroidApp/1.0`** vào chuỗi `User-Agent`.
   - Backend `_Layout.cshtml` kiểm tra `User-Agent` chứa `BookShareAndroidApp` để gắn class `is-mobile-app` lên `<body>`, kích hoạt Bottom Navigation Bar 5 mục nền sáng (**Trang chủ**, **Tủ sách**, nút tròn giữa **Quét QR đa năng / QR Thanh toán**, **Thông báo** kèm badge số chưa đọc, **Tài khoản**) và chừa khoảng đệm đáy hợp lý.

---

## 2. Cấu Trúc Thư Mục Mã Nguồn Android (`mobile/bookshare-android/`)

```plaintext
mobile/bookshare-android/
├── settings.gradle
├── build.gradle
├── gradle.properties
└── app/
    ├── build.gradle
    └── src/main/
        ├── AndroidManifest.xml
        ├── assets/
        │   └── offline.html              # Giao diện mất mạng nền sáng #F8FAFC + nút thử lại
        ├── java/vn/bookshare/mobile/
        │   ├── MainActivity.java         # WebView, Custom Tabs Google OAuth, Camera/FileChooser, JS Bridge
        │   └── NotificationPoller.java   # Periodic background poller gắn CookieManager + Android 13+ Channel
        └── res/
            ├── drawable/                 # Nền splash sáng & launcher foreground
            ├── mipmap-anydpi-v26/        # Adaptive Icon BookShare (#2563EB)
            ├── mipmap-mdpi .. xxxhdpi/   # Bộ icon sắc nét chuyển từ wwwroot/images/icons/
            ├── values/                   # colors.xml (#F8FAFC, #2563EB), strings.xml, styles.xml (NoActionBar)
            └── xml/
                └── network_security_config.xml
```

---

## 3. Xử Lý Nghiệp Vụ Đặc Thù Trên Mobile

### 3.1. Đăng nhập Google OAuth 2.0 Không Bị Lỗi `403 disallowed_useragent` / `405` & Đồng Bộ Phiên Về WebView
- Khi người dùng bấm **Tiếp tục bằng Google** trên Android App, `core-api.js` chuyển luồng qua `BookShareAndroidBridge.openExternalAuth` tới `GET /S01AuthOtpGoogle/GoogleLoginMobile` mở bằng **Android Custom Tabs** (`androidx.browser.customtabs.CustomTabsIntent`).
- Sau khi Google xác thực thành công ở `GoogleCallback(mobileApp: true)`, máy chủ tạo mã liên kết 1 lần (hiệu lực 60 giây, băm SHA-256 trong `QrLoginSessions`) và chuyển về ứng dụng qua Deep Link **`bookshare://open?path=/S01AuthOtpGoogle/CompleteMobileOAuth?code=...`**.
- `MainActivity` mở `CompleteMobileOAuth` trực tiếp trong `WebView` (được loại trừ khỏi `shouldOpenExternalOAuth`), tiêu thụ mã 1 lần nguyên tử và gắn `BookShare.AuthCookie` thẳng vào phiên WebView.

### 3.2. Xin Quyền Camera Theo Nhu Cầu (On-Demand) & Upload Ảnh Biên Bản `S10`
- **Không bao giờ xin quyền `CAMERA` lúc mở ứng dụng** (`onCreate` không gọi `CAMERA`; quyền `POST_NOTIFICATIONS` trên Android 13+ cũng chỉ hỏi nhẹ 1 lần sau khi trang chính tải xong).
- Chỉ khi người dùng bấm mở chức năng quét **QR Thanh toán**, **QR Đăng nhập / Bàn giao**, **Quét ISBN** hoặc **Quét Barcode**, `core-api.js` khởi chạy `scanner.start({ facingMode: 'environment' })` (mở trực tiếp camera sau trong 1 lần xin quyền duy nhất).
- `WebChromeClient`:
  - `onPermissionRequest`: kiểm tra cùng origin, chỉ xin quyền `Manifest.permission.CAMERA` khi có `RESOURCE_VIDEO_CAPTURE`, chỉ cấp đúng `new String[]{ PermissionRequest.RESOURCE_VIDEO_CAPTURE }` (tránh lỗi âm thanh không có quyền `RECORD_AUDIO`), xử lý sạch `onPermissionRequestCanceled`, và hiển thị nút mở Cài đặt hệ thống (`openAppSettings`) nếu người dùng từng từ chối.
  - `onShowFileChooser`: mở bộ chọn ảnh hệ thống (`ACTION_GET_CONTENT` với `image/*`) phục vụ tải ảnh biên bản `S10`, icon quản trị `S22` và chọn ảnh mã QR từ thư viện mà **không đòi quyền Camera**.

### 3.3. Đồng Bộ Thông Báo Nền Với `CookieManager` (`NotificationPoller.java`)
- Tạo `NotificationChannel` (`bookshare_alerts`, độ ưu tiên `IMPORTANCE_HIGH`, `VISIBILITY_PUBLIC` hiển thị ngoài màn hình khóa).
- Lên lịch bằng `WorkManager` (`PeriodicWorkRequest`, chu kỳ tối thiểu 15 phút khi nền) và luồng nhẹ khi App mở:
  - Lấy chuỗi Cookie hiện hành từ `BuildConfig.BASE_URL` (có fallback thêm `/`).
  - Nếu rỗng (chưa đăng nhập), bỏ qua an toàn.
  - Gọi `GET {BASE_URL}/api/mobile/notifications/unread?sinceId={lastId}` đính kèm Header `Cookie: <cookies>` và `Accept: application/json`.
  - Tự động đặt lại mốc `lastId = 0` nếu CSDL vừa được khởi tạo lại (có `unreadCount > 0` nhưng mảng mới rỗng), tránh mất thông báo.
  - Khi có thông báo mới, đẩy thông báo Native lên thanh trạng thái; khi người dùng chạm vào thông báo, mở `MainActivity` kèm `targetUrl` trong WebView.

### 3.4. Giao Tiếp An Toàn `@JavascriptInterface` (`BookShareAndroidBridge`)
- `isNativeApp()` -> `true`
- `getAppInfo()` -> JSON `{"platform":"android","version":"4.0.0","baseUrl":"https://bookshare.castle-tech.cloud"}`
- `vibrateShort()` -> rung phản hồi ngắn khi quét QR thành công
- `syncNotificationsNow()` -> kích hoạt WorkManager kiểm tra ngay thông báo chưa đọc
- `pushLocalNotification(title, message, targetUrl)` -> đẩy ngay thông báo nội bộ khi đang foreground
- `openExternalAuth(url)` -> mở URL xác thực ngoài qua Custom Tabs
- `openAppSettings()` -> mở trang Cài đặt quyền ứng dụng Android nếu người dùng cần cấp lại quyền Máy ảnh
- `setPullToRefreshEnabled(boolean)` -> khóa kéo làm mới khi đang mở hộp thoại camera quét QR
- `retryLastPage()` / `reloadHome()` -> tải lại trang gần nhất hoặc trang chủ `BuildConfig.BASE_URL` (dùng từ `offline.html`)

---

## 4. Pipeline Tự Động Đóng Gói APK Trên GitHub Actions

Tệp workflow: **`.github/workflows/build-bookshare-android-apk.yml`**
- **Trigger:**
  - `push` vào `main`, `mainforSprint2`, `mainforSprint3` (khi thay đổi `mobile/bookshare-android/**` hoặc chính file workflow).
  - `workflow_dispatch` kèm input `base_url` (mặc định **`https://bookshare.castle-tech.cloud`**, vẫn hỗ trợ nhập URL khác khi chạy tay).
- **Quy trình Build (`ubuntu-latest`):**
  1. `actions/checkout@v4` & `actions/setup-java@v4` (Temurin JDK 17).
  2. Chấp nhận Android SDK licenses & thiết lập Gradle `8.7` qua `gradle/actions/setup-gradle@v4`.
  3. Chọn `TARGET_URL` theo thứ tự: `github.event.inputs.base_url` -> `vars.BOOKSHARE_BASE_URL` -> mặc định **`https://bookshare.castle-tech.cloud`**.
  4. Chạy `gradle assembleDebug --no-daemon -PBOOKSHARE_BASE_URL="$TARGET_URL"` trong `mobile/bookshare-android`.
  5. Đổi tên đầu ra thành **`BookShare-Mobile-Android.apk`** và tải lên Artifact (`actions/upload-artifact@v4`, `retention-days: 30`).
