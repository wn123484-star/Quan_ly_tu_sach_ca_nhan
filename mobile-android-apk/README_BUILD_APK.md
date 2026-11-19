# 📱 Hướng Dẫn Đóng Gói Android APK — BookShare Mobile Super-App (`[BS-MOBILE-APK]`)

Bộ khung này sử dụng **Ionic Capacitor 6** để đóng gói trực tiếp giao diện `/MobileApp` (5 Tab chuẩn native: Trang Chủ, Reading Studio Im Lặng, Camera Quét QR/ISBN/Barcode, Ví Pay 3D, AI Copilot & Chat Thủ Thư) thành tệp cài đặt **Android `.apk`** chạy mượt trên điện thoại thật.

---

## 1. Yêu cầu môi trường
- **Node.js 20 LTS** trở lên (`node -v`)
- **Java JDK 17** (`java -version`)
- **Android Studio** (có Android SDK Platform 34 & Build-Tools)

---

## 2. Cấu hình địa chỉ máy chủ (Production / Local Test)
Mở tệp `capacitor.config.json`:
- Mặc định trỏ sẵn về môi trường Production HTTPS:
  ```json
  "server": {
    "url": "https://bookshare.castle-tech.cloud/MobileApp",
    "androidScheme": "https"
  }
  ```
- Nếu test nội bộ trên máy ảo Android Emulator cùng mạng LAN, có thể tạm đổi sang `http://10.0.2.2:5087/MobileApp` và đặt `"cleartext": true`.

---

## 3. Các bước biên dịch tệp `BookShare-Mobile.apk`

```bash
# Bước 1: Di chuyển vào thư mục bộ khung APK
cd mobile-android-apk

# Bước 2: Cài đặt thư viện Capacitor & plugin Camera / Local Notifications
npm install

# Bước 3: Khởi tạo dự án Android native
npx cap add android

# Bước 4: Sao chép AndroidManifest.xml đã cấu hình sẵn quyền CAMERA, RECORD_AUDIO, POST_NOTIFICATIONS
cp android-template/AndroidManifest.xml android/app/src/main/AndroidManifest.xml

# Bước 5: Đồng bộ cấu hình & plugin vào dự án Android
npx cap sync android

# Bước 6A: Build nhanh file APK Debug (cài trực tiếp qua cáp USB / chia sẻ demo)
npm run apk:debug
# File xuất ra tại: android/app/build/outputs/apk/debug/app-debug.apk

# Bước 6B: Hoặc mở Android Studio để ký số Release APK / AAB
npx cap open android
```

---

## 4. Quyền hạn Android đã khai báo sẵn
- `INTERNET`, `ACCESS_NETWORK_STATE`: kết nối HTTPS tới máy chủ BookShare.
- `CAMERA`: quét mã QR đăng nhập Web PC (`BSQR-*`), quét ISBN/Barcode sách và chụp ảnh kiểm định tình trạng sách giấy.
- `RECORD_AUDIO`: nhận diện giọng nói tiếng Việt (`vi-VN`) cho tính năng **Voice-to-Quote** trong Reading Studio.
- `POST_NOTIFICATIONS`: hiển thị thông báo nhắc hạn thuê sách & giải ngân cọc Escrow.

> **Lưu ý bảo mật:** Khi dùng WebView Capacitor trên Android, để `html5-qrcode` truy cập được Camera bên trong WebView, hãy đảm bảo máy chủ chạy qua **HTTPS** (`https://bookshare.castle-tech.cloud/MobileApp`) và cấp quyền Camera trong lần mở App đầu tiên.
