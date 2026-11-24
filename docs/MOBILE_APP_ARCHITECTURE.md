# KIẾN TRÚC ỨNG DỤNG ANDROID BOOKSHARE MOBILE & CI/CD GITHUB ACTIONS

> **Tài liệu Sprint 3 — Phương Án 1: Android WebView Shell Gốc Không Hỏi URL + Thông Báo Nền CookieManager**

---

## 1. Nguyên Tắc Thiết Kế Cốt Lõi

1. **Hiển thị 100% Giao diện Web BookShare Light Mode:**
   - Sử dụng trực tiếp `Theme.AppCompat.Light.NoActionBar` với nền gốc & màn hình chờ **`#F8FAFC`**, thanh trạng thái (Status Bar) icon tối trên nền sáng (`SYSTEM_UI_FLAG_LIGHT_STATUS_BAR`).
   - **Tuyệt đối không hiện thanh tiêu đề/thanh địa chỉ URL trên App**, và **tuyệt đối không hiện hộp thoại (`AlertDialog` / `EditText`) hỏi nhập URL khi mở App**.
2. **Khóa Cứng URL Lúc Biên Dịch (`BuildConfig.BASE_URL`):**
   - Tệp `mobile/bookshare-android/app/build.gradle` đọc giá trị `BOOKSHARE_BASE_URL` từ Gradle Property (`-PBOOKSHARE_BASE_URL=...`) hoặc biến môi trường (`System.getenv("BOOKSHARE_BASE_URL")`), mặc định cục bộ `http://10.0.2.2:5087`, đưa vào `buildConfigField("String", "BASE_URL", ...)`.
   - `MainActivity.java` tải thẳng `BuildConfig.BASE_URL` ngay khi khởi động.
3. **Nhận diện App & Thanh Điều Hướng Đáy (Bottom Navigation Bar):**
   - `WebSettings` gắn hậu tố **`BookShareAndroidApp/1.0`** vào chuỗi `User-Agent`.
   - Backend `_Layout.cshtml` kiểm tra `User-Agent` chứa `BookShareAndroidApp` để gắn class `is-mobile-app` lên `<body>`, kích hoạt Bottom Navigation Bar 5 mục nền sáng (**Trang chủ**, **Tủ sách**, nút tròn giữa **Quét QR**, **Thông báo** kèm badge số chưa đọc, **Tài khoản**) và chừa khoảng đệm đáy hợp lý.

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

### 3.1. Đăng nhập Google OAuth 2.0 Không Bị Google Chặn `403 disallowed_useragent`
- Trong `shouldOverrideUrlLoading`:
  - Mọi URL thuộc cùng host với `BuildConfig.BASE_URL` (trừ `/S01AuthOtpGoogle/GoogleLogin` và `/signin-google` nếu cần thoát WebView) đều load trực tiếp trong WebView.
  - Khi gặp đường dẫn `/S01AuthOtpGoogle/GoogleLogin` hoặc `accounts.google.com`, `MainActivity` mở bằng **Android Custom Tabs** (`androidx.browser.customtabs.CustomTabsIntent`) và hỗ trợ Deep Link **`bookshare://open`** quay về App.

### 3.2. Camera Quét QR/ISBN & Upload Ảnh Biên Bản `S10`
- Cấp quyền runtime `CAMERA` và `POST_NOTIFICATIONS` (Android 13+, `API 33+`) ngay khi cần.
- `WebChromeClient`:
  - `onPermissionRequest`: chấp thuận `PermissionRequest.RESOURCE_VIDEO_CAPTURE` từ origin của máy chủ BookShare để hộp thoại quét QR/ISBN chạy trực tiếp trong WebView.
  - `onShowFileChooser`: mở bộ chọn ảnh / camera hệ thống trả `ValueCallback<Uri[]>` phục vụ tải ảnh biên bản `S10` và chọn ảnh mã QR từ thư viện.

### 3.3. Đồng Bộ Thông Báo Nền Với `CookieManager` (`NotificationPoller.java`)
- Tạo `NotificationChannel` (`bookshare_alerts`, độ ưu tiên `IMPORTANCE_HIGH`, `VISIBILITY_PUBLIC` hiển thị ngoài màn hình khóa).
- Lên lịch bằng `WorkManager` (`PeriodicWorkRequest`, chu kỳ tối thiểu 15 phút khi nền) và luồng nhẹ khi App mở:
  - Lấy chuỗi Cookie hiện hành: `String cookies = CookieManager.getInstance().getCookie(BuildConfig.BASE_URL);`
  - Nếu rỗng (chưa đăng nhập), bỏ qua an toàn.
  - Gọi `GET {BASE_URL}/api/mobile/notifications/unread?sinceId={lastId}` đính kèm Header `Cookie: <cookies>` và `Accept: application/json`.
  - Khi có thông báo mới, đẩy thông báo Native lên thanh trạng thái; khi người dùng chạm vào thông báo, mở `MainActivity` kèm `targetUrl` trong WebView.

### 3.4. Giao Tiếp An Toàn `@JavascriptInterface` (`BookShareAndroidBridge`)
- `isNativeApp()` -> `true`
- `getAppInfo()` -> JSON `{"platform":"android","version":"3.0.0"}`
- `vibrateShort()` -> rung phản hồi ngắn khi quét QR thành công
- `syncNotificationsNow()` -> kích hoạt WorkManager kiểm tra ngay thông báo chưa đọc
- `pushLocalNotification(title, message, targetUrl)` -> đẩy ngay thông báo nội bộ khi đang foreground
- `openExternalAuth(url)` -> mở URL xác thực ngoài qua Custom Tabs
- `reloadHome()` -> tải lại trang chủ `BuildConfig.BASE_URL` (dùng từ `offline.html`)

---

## 4. Pipeline Tự Động Đóng Gói APK Trên GitHub Actions

Tệp workflow: **`.github/workflows/build-bookshare-android-apk.yml`**
- **Trigger:**
  - `push` vào `main`, `mainforSprint2`, `mainforSprint3` (khi thay đổi `mobile/bookshare-android/**` hoặc chính file workflow).
  - `workflow_dispatch` kèm input `base_url` (cho phép nhập URL VPS/Cloud hoặc IP LAN ngay khi chạy tay).
- **Quy trình Build (`ubuntu-latest`):**
  1. `actions/checkout@v4` & `actions/setup-java@v4` (Temurin JDK 17).
  2. Chấp nhận Android SDK licenses & thiết lập Gradle `8.7` qua `gradle/actions/setup-gradle@v4`.
  3. Chọn `TARGET_URL` theo thứ tự: `github.event.inputs.base_url` -> `vars.BOOKSHARE_BASE_URL` -> fallback `http://10.0.2.2:5087`.
  4. Chạy `gradle assembleDebug --no-daemon -PBOOKSHARE_BASE_URL="$TARGET_URL"` trong `mobile/bookshare-android`.
  5. Đổi tên đầu ra thành **`BookShare-Mobile-Android.apk`** và tải lên Artifact (`actions/upload-artifact@v4`, `retention-days: 30`).
