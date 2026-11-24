# BÁO CÁO KIỂM THỬ TỰ ĐỘNG E2E & BẢO MẬT — BOOKSHARE PORTAL

Báo cáo tổng hợp kết quả biên dịch, kiến trúc kiểm thử tự động đa tầng (bao gồm nâng cấp Sprint 3) và các lỗi thực tế đã được phát hiện và khắc phục triệt để trong quá trình kiểm toán hệ thống BookShare (.NET 10 MVC + LocalDB).

---

## 1. Tổng Quan Bộ Công Cụ Kiểm Thử Tự Động

Hệ thống được xác minh đồng thời bằng 2 kịch bản Python chạy trực tiếp trên tiến trình **ASP.NET Core 10 Kestrel thật** kết nối cơ sở dữ liệu **SQL Server (`(localdb)\MSSQLLocalDB` — `BookShareDb`)**:

1. **`scripts/qa_full_bot.py` (Kiểm thử E2E 6 Tầng — 315 Assertions):**
   - **Tầng 1 (`L1` — 8 checks):** Kiểm tra cấu trúc mã nguồn, xóa sạch các tệp mẫu thừa (`site.css`, `site.js`, `_Layout.cshtml.css`), Strict Service Layer (0 tham chiếu `BookShareDbContext` tại Controllers), 0 User ID fallback, mặc định Light Mode trong `_Layout.cshtml`, nền sáng `#f8fafc` tại `.app-splash` và `manifest.json`, `service-worker.js` bản `bookshare-static-v2`, và kiểm tra `s01-auth-otp-google.js` không còn tài khoản mẫu/mật khẩu.
   - **Tầng 2 (`L2` — 141 checks):** Quét toàn bộ 23 trang (`/` + `S01` → `S22`) bảo đảm `HTTP 200`, Light Mode mặc định trên HTML trả về, 0 thuật ngữ kỹ thuật nội bộ, 0 nội dung thương mại bán sách, 100% thẻ `<img>` có `alt` + `onerror`, kiểm tra UI `/S01AuthOtpGoogle` (ẩn tài khoản mẫu, vô hiệu hóa nút Google), xác nhận `POST /S01AuthOtpGoogle/GoogleLogin` fail-closed `HTTP 400`, kiểm tra `/S21` & `/S22` không rò rỉ PII với Guest, `/api/ai-chat/quick-ask` phân tách khách và thành viên, và chặn `HTTP 401` đối với toàn bộ 18 endpoint ghi dữ liệu khi chưa đăng nhập.
   - **Tầng 3 (`L3` — 59 checks):** Kiểm thử đăng nhập 6 tài khoản 4 vai trò (`Admin`, `Librarian`, `Curator`, `User1`, `User2`, `User3`), chặn mật khẩu yếu, chặn leo thang đặc quyền `SelectedRole=Admin` khi đăng ký công khai, kiểm tra đổi mật khẩu Admin & vô hiệu hóa hoàn toàn mật khẩu cũ (0 backdoor), kiểm tra khóa/mở khóa tài khoản thực thi `LockoutEnd`, ma trận phân quyền `403 Forbidden` cho toàn bộ API đặc quyền (`User` & `Curator`), và kiểm tra chặn chéo người dùng `403` (IDOR) trên `S02`, `S03`, `S04`, `S18`, `S20`.
   - **Tầng 4 (`L4` — 66 checks):** Chạy khép kín các vòng đời nghiệp vụ đa phân hệ (`S05->S02->S06->S09->S10->S15`, `S08->S12->S21->S11`, `S16<->S17<->S12<->S19`, `S01/S18`, `S20`, `S13/S14`, `S22`).
   - **Tầng 6 (`L6` — 38 checks Sprint 3 & Global Error Handling):** Kiểm thử toàn bộ tính năng Sprint 3 và hệ thống bắt ngoại lệ/trang báo lỗi trên server thật:
     - **QR Login 2 Chiều (`/api/qr-login/*`):** Chiều 1 (`create -> status Pending -> preview -> approve -> claim` kiểm chứng Cookie hoạt động -> chặn replay `400` -> chặn tài khoản đang `LockoutEnd` phê duyệt `400`); Chiều 2 (`create-mobile-link -> mobile-claim` -> chặn replay `400`).
     - **QR Bàn giao & Hoàn trả Sách (`/api/handover-qr/*`):** Thủ thư tạo QR -> chặn bên thứ ba xác nhận (`403 Anti-IDOR`) -> người mượn xác nhận kích hoạt mượn (`200`) -> chặn quét lại (`400`) -> tạo & xác nhận QR hoàn trả đóng hợp đồng (`200`).
     - **Khép kín Tài chính Tranh chấp (`S19 <-> S17`):** Phân xử `Resolved` kèm khoản bồi thường `25.000đ` giữ nguyên trạng thái khóa mượn (`loanUnlocked=false`, `S08 CreateRequest` vẫn trả `400`), phát hành lệnh `Pending` ký `HMAC-SHA512`; sau khi thanh toán qua `S17/VerifyCallback`, quyền mượn được mở lại (`200`).
     - **Nâng quyền `Curator` (`S01 -> S22`):** Gửi đề nghị -> chặn gửi trùng (`400`) -> Admin duyệt -> đăng nhập lại xác minh vai trò `Curator`.
     - **AI Copilot 2 Tầng (`S13` & Widget):** Lệnh `clientCommands` (`set_theme=dark`), `ExecuteCopilotAction` chặn `User` gọi `send_due_reminders_batch` (`403 RBAC`) và cho phép tự gửi email nhắc hạn (`200`) / Thủ thư gửi hàng loạt (`200`).
     - **Mobile Companion API, WebView Shell & Static CI/Docker:** `GET /api/mobile/notifications/unread` trả `401` JSON khi chưa đăng nhập và `200` khi đã đăng nhập; `User-Agent: BookShareAndroidApp/1.0` kích hoạt `is-mobile-app` + `#mobileBottomNav`; xác minh tĩnh `MainActivity.java` (không có URL `AlertDialog`/`EditText`, có `BuildConfig.BASE_URL` & Custom Tabs), `NotificationPoller.java` (`CookieManager`), workflow APK GitHub Actions và `docker-compose.yml` chạy schema-only mặc định.
     - **Xử lý Ngoại lệ Toàn cục & Trang Báo Lỗi Thân Thiện (`400`/`401`/`403`/`404`/`405`/`500`):** `GET /S02Pers` trả `HTTP 404` kèm nút gợi ý `/S02PersonalShelf` ("Tủ Sách Cá Nhân") và không lộ `Development Mode`/`ASPNETCORE_ENVIRONMENT`; `GET /duong-dan-khong-ton-tai-xyz` trả trang 404 BookShare; `GET /api/khong-ton-tai-xyz` (`Accept: application/json`) trả JSON `404`; `User` `GET /S22AdminAnalytics` qua trình duyệt trả trang lỗi `403` thân thiện kèm nút Đăng nhập/Trang chủ (trong khi `/S22AdminAnalytics/ChartData` giữ nguyên JSON `403`); file tĩnh thiếu `/uploads/khong-ton-tai.png` trả `404` trực tiếp không redirect vòng lặp.
   - **Tầng 5 (`L5` — 3 checks):** Gọi `POST /S22AdminAnalytics/ManageSampleData (SeedSampleData)` khôi phục sạch CSDL gốc về đúng mốc **6 Thành viên, 55 Bản sao** và xác minh `User1` đăng nhập lại thành công với `User@123`.

2. **`scripts/smoke_test.py` (Kiểm thử Khói 3 Lớp):**
   - Kiểm tra cấu hình Docker mount (`docker-compose.yml`, `Dockerfile`), quét tĩnh `Views/*.cshtml` bảo đảm 0 nhãn `S01`–`S22` / `Ver07`, kiểm tra HTTP 200 cho `/` + 22 phân hệ, và xác nhận `401` cho các API `Admin` / `Librarian` / `Curator` khi chưa đăng nhập (cả có và không có CSRF Token).

---

## 2. Kết Quả Xác Minh Đạt Được (Observed Outputs)

- **Biên dịch giải pháp (`dotnet build BookShare.sln --nologo`):** `Build succeeded. 0 Error(s)` (4 cảnh báo NuGet `NU1902`/`NU1903` từ package `System.Security.Cryptography.Pkcs` 10.0.5).
- **Bot E2E 6 Tầng (`python scripts/qa_full_bot.py --autostart`):**

| Tầng kiểm thử | Số lượng Assertions | Đạt (`PASS`) | Thất bại (`FAIL`) | Tỷ lệ |
| :--- | :---: | :---: | :---: | :---: |
| **L1** — Kiến trúc tĩnh, Light Mode & Tài nguyên | 8 | 8 | 0 | **100%** |
| **L2** — Giao diện Guest, Light Theme, OAuth 400 & Chặn 401 | 141 | 141 | 0 | **100%** |
| **L3** — Auth, Đổi MK không backdoor, Lockout, RBAC 403 & IDOR | 59 | 59 | 0 | **100%** |
| **L4** — Liên thông nghiệp vụ đa phân hệ (`S02` → `S22`) | 66 | 66 | 0 | **100%** |
| **L6** — Sprint 3 & Hệ thống Xử lý Ngoại lệ / Trang Lỗi (400/401/403/404/405/500) | 38 | 38 | 0 | **100%** |
| **L5** — Reset CSDL mẫu & Khôi phục nguyên trạng 6 Users / 55 Copies | 3 | 3 | 0 | **100%** |
| **TỔNG CỘNG (`qa_full_bot.py`)** | **315** | **315** | **0** | **100% PASS** |

- **Kiểm thử Khói 3 Lớp (`python scripts/smoke_test.py --autostart --skip-build`):** **100% PASS (0 lỗi)** — toàn bộ 23 routes trả về `HTTP 200 OK`, 0 lỗi UI, 8/8 lần gọi POST nhạy cảm bị chặn `HTTP 401`.

---

## 3. Các Lỗi Thực Tế Đã Phát Hiện & Vá Triệt Để Nhờ Bot Kiểm Thử

1. **Lỗi khóa ngoại khi Reset dữ liệu mẫu (`S22AdminAnalyticsService.ManageSampleDataAsync`):**
   - *Hiện tượng:* Lỗi HTTP 500 do các bảng phụ trợ kế thừa trong CSDL LocalDB (`SupportChatMessages`, `QrLoginSessions`, `ReadingSessions`, `UserWallets`) giữ khóa ngoại trỏ về `AspNetUsers` và `Books` khiến `DELETE FROM AspNetUsers` thất bại; ngoài ra SQL script seed chứa `USE [BookShareDb]` / `PRINT` và ký tự `{}` không hợp lệ với `ExecuteSqlRawAsync`.
   - *Cách khắc phục:* Triển khai `ExecuteRawBatchAsync` dùng ADO.NET `DbCommand` trực tiếp, tự động xóa có điều kiện các bảng mở rộng trước khi dọn sạch và nạp lại chuẩn `02_BookShare_SeedData.sql`.
2. **Lỗi logic quét xuất kho trùng phiếu cũ tại Quầy Thủ thư (`S21LibrarianDeskService.cs`):**
   - *Hiện tượng:* Khi một bản sao đã từng được mượn và trả qua quầy (`Loan.Status = "Returned"`), `LoanRequest.Status` vẫn là `"Approved"`. Ở lượt quét `Checkout` kế tiếp cho độc giả mới, `pendingReq` lại khớp nhầm phiếu cũ đã hoàn thành và bỏ qua `request.BorrowerUserId` mới.
   - *Cách khắc phục:* Bổ sung điều kiện bắt buộc `r.Loan == null && (r.Status == "Pending" || r.Status == "Approved")` tại cả danh sách hiển thị quầy và luồng `ProcessQuickBarcodeScanAsync`.
3. **Lộ dữ liệu mượn của thành viên khác & thiếu catch `403` trên `S12LoanRenewalFine`:**
   - *Hiện tượng:* `GetLoanRenewalFinePageAsync` hiển thị toàn bộ hợp đồng/phiếu phạt cho người dùng thường và `RenewLoan` chưa chuyển `UnauthorizedAccessException` sang HTTP `403` JSON tường minh.
   - *Cách khắc phục:* Truyền `currentUserRole` từ `S12LoanRenewalFineController` xuống Service, lọc dữ liệu theo `currentUserId` nếu không phải `Librarian`/`Admin`, trả rỗng với Guest và bắt `UnauthorizedAccessException` trả HTTP `403`.
4. **Lỗ hổng cộng điểm uy tín nhiều lần ở kiểm định Hub (`S20ExchangeHubClubService.cs`):**
   - *Hiện tượng:* Gọi `VerifyPhysicalBook` lặp lại trên cùng 1 bản sao cộng dồn `+2 TrustScore` vô hạn.
   - *Cách khắc phục:* Lưu dấu vết kiểm định bản sao và chỉ cộng `+2 TrustScore` duy nhất lần đầu cho mỗi `BookCopy`.
5. **Lỗi khóa ngoại `LoanId` không tồn tại ở đánh giá `S15TrustScoreReviewService.cs`:**
   - *Cách khắc phục:* Kiểm tra `_db.Loans.AnyAsync(l => l.Id == request.LoanId.Value)` trả về HTTP `400` thay vì phát sinh ngoại lệ SQL.
