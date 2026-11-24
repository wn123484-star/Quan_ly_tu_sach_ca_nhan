# BẢO MẬT, PHÂN QUYỀN RBAC & CHỐNG IDOR — BOOKSHARE PORTAL

Tài liệu này mô tả cơ chế kiểm soát truy cập, xác thực, phòng chống lỗ hổng IDOR và bảo vệ dữ liệu đang thực thi trên toàn bộ 23 Controller và các tầng Service của BookShare (.NET 10 MVC).

---

## 1. Ma trận 4 Vai trò (RBAC)

Hệ thống sử dụng ASP.NET Core Identity (`AspNetRoles`, `AspNetUsers`, `AspNetUserRoles`) với Cookie Authentication (`BookShare.Auth`) và 4 vai trò chuẩn:

| Vai trò | Định danh Seed | Phạm vi nghiệp vụ được phép |
| :--- | :--- | :--- |
| **`Guest`** (Chưa đăng nhập) | — | Xem trang chủ, tra cứu danh mục công khai (`S06`, `S07`), hỏi trợ lý sách công khai (`/api/ai-chat/quick-ask`), đăng nhập/đăng ký (`S01`). Bị chặn `401 Unauthorized` trên toàn bộ endpoint ghi dữ liệu. |
| **`User`** | `u-user-01`, `u-user-02`, `u-user-03` | Quản lý tủ sách cá nhân (`S02`), tiến độ đọc (`S03`), ghi chú (`S04`), tự biên mục bản sao cá nhân `User` (`S05`), mượn/hủy/gia hạn (`S06`, `S08`, `S09`, `S11`, `S12`), báo cáo ảnh kiểm định (`S10`), chat AI/gợi ý (`S13`, `S14`), đánh giá (`S15`), nâng cấp gói & thanh toán ví bảo lãnh (`S16`, `S17`), thông báo (`S18`), tạo khiếu nại (`S19`), đăng ký ký gửi trạm (`S20`). |
| **`Curator`** | `u-curator-01` | Toàn bộ quyền `User` + Điều phối Trạm giao nhận CLB (`S20`: kiểm định sách thực tế, xử lý ký gửi/giao nhận/thu hồi tại Hub, cập nhật thông tin trạm) + chuyển trạng thái giao nhận P2P (`S09`). |
| **`Librarian`** | `u-librarian-01` | Toàn bộ quyền `User` + Biên mục sửa/xóa đầu sách & tạo bản sao kho chung `System` (`S05`), định vị kệ thư viện (`S07`), duyệt/giao/thu hồi phiếu mượn kho chung (`S08`), kích hoạt thông báo hàng chờ (`S11`), tính phí phạt quá hạn (`S12`), hoàn tiền cọc bảo lãnh (`S17`), phát chiến dịch email nhắc hạn (`S18`), thao tác Bàn Thủ thư (`S21`). |
| **`Admin`** | `u-admin-01` | Toàn quyền vận hành hệ thống: toàn bộ quyền `Curator` & `Librarian` + phân xử tranh chấp (`S19`: `ResolveDispute`), quản lý người dùng (đổi vai trò, khóa/mở khóa tài khoản `LockoutEnd`), xem Dashboard thống kê tổng thể, chạy Hangfire Daily Jobs và nạp/reset dữ liệu mẫu SQL (`S22`). |

---

## 2. Xử lý `401 Unauthorized` / `403 Forbidden` cho API & UI

Trong `src/BookShare.Web/Program.cs`, sự kiện Cookie Authentication được cấu hình phân biệt rõ luồng trình duyệt và luồng gọi API JSON (`Accept: application/json` hoặc `X-Requested-With: XMLHttpRequest`):
- **Chưa đăng nhập gọi POST API / JSON endpoint `[Authorize]`:** Trả về **`HTTP 401 Unauthorized`** kèm payload JSON `{"message": "Vui lòng đăng nhập tài khoản BookShare trước khi thực hiện thao tác này."}`.
- **Đã đăng nhập nhưng thiếu Role trên `[Authorize(Roles = "...")]`:** Trả về **`HTTP 403 Forbidden`** kèm payload JSON `{"message": "Tài khoản của bạn không có quyền thực hiện thao tác nghiệp vụ này."}`.
- **Lỗ hổng IDOR phát hiện ở tầng Service:** Ném `UnauthorizedAccessException` và được Controller chuyển thành **`HTTP 403 Forbidden`**.

---

## 3. Bảo mật Xác thực & Google OAuth 2.0 (`S01AuthOtpGoogle`)

1. **Khóa Đăng ký Công khai:** `S01AuthOtpService.RegisterUserAsync` luôn bỏ qua vai trò gửi lên từ client (nếu có) và cưỡng bức gán role `"User"`; form đăng ký trên UI không cho phép chọn role.
2. **Nâng cấp Mật khẩu & Chống Backdoor:** Đã loại bỏ nhánh so sánh mật khẩu đặc quyền cố định cho `u-admin-01`. Khi tài khoản mẫu đăng nhập lần đầu với mật khẩu khởi tạo khớp quy chuẩn, hệ thống tự băm lại theo chuẩn `PasswordHasher<AspNetUser>` của Identity; sau khi đổi mật khẩu, mật khẩu cũ bị vô hiệu hóa hoàn toàn.
3. **Thực thi Khóa Tài khoản (`LockoutEnd`):** Cả `AuthenticateAsync` (Email/Password) và `UpsertGoogleLoginAsync` (Google OAuth) đều kiểm tra `user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow` và từ chối phiên đăng nhập.
4. **Google OAuth 2.0 Fail-Closed:**
   - Helper `IsGoogleOAuthConfigured()` xác minh `Authentication:Google:ClientId` và `Authentication:Google:ClientSecret` khác rỗng và không chứa chuỗi giữ chỗ (`demo`, `xxxx`, `your-`, `DIEN_KEY`).
   - Khi chưa cấu hình khóa thật: nút Google trên `/S01AuthOtpGoogle` bị `disabled`, và mọi POST tới `/S01AuthOtpGoogle/GoogleLogin` trả về **`HTTP 400`** với thông báo nghiệp vụ rõ ràng (không tự động đăng nhập Sandbox).
   - Trong `GoogleCallback`, nếu liên kết thất bại hoặc tài khoản đang bị khóa, hệ thống gọi `SignOutAsync` ngay lập tức.
5. **Bootstrap Idempotent (`EnsureCoreAdminBootstrapAsync`):** Chỉ khởi tạo các role hoặc tài khoản quản trị cơ bản nếu bảng đang trống; tuyệt đối không tự xóa dữ liệu nghiệp vụ đã nạp từ SQL seed.

---

## 4. Kiểm soát Đặc quyền Controller (`[Authorize(Roles = "...")]`)

| Controller / Endpoint | Yêu cầu Vai trò | Ghi chú bổ sung |
| :--- | :--- | :--- |
| `S05IsbnAutoCatalog/SaveBookAndCopy` | `[Authorize]` | Nếu `User`/`Curator` truyền `TargetOwnerType = "System"`, Service ném `UnauthorizedAccessException` (`403`). |
| `S05IsbnAutoCatalog/UpdateCatalogBook`, `DeleteCatalogBook` | `Librarian,Admin` | — |
| `S07LibraryCatalog/UpdateShelfLocation` | `Librarian,Admin` | — |
| `S08LibraryLoanFlow/ApproveOrReject`, `ConfirmHandover`, `ConfirmReturn` | `Librarian,Admin` | — |
| `S11HotBookQueue/NotifyTop` | `Librarian,Admin` | — |
| `S12LoanRenewalFine/AssessFine` | `Librarian,Admin` | — |
| `S17SandboxPayment/RefundDeposit` | `Librarian,Admin` | — |
| `S18NotificationHub/TriggerResendEmail` | `Librarian,Admin` | — |
| `S19DisputeResolution/ResolveDispute` | `Admin` | Chặn phân xử lặp lại đối với hồ sơ không còn `Open`/`UnderReview`. |
| `S20ExchangeHubClub/VerifyPhysicalBook`, `ProcessHubHandover`, `UpdateHubStation` | `Curator,Admin` | Kiểm tra thưởng kiểm định chỉ cộng 1 lần cho mỗi bản sao. |
| `S21LibrarianDesk/*` (mọi POST) | `Librarian,Admin` | — |
| `S22AdminAnalytics/*` (`ChartData` & mọi POST) | `Admin` | GET `Index` tự ẩn danh sách PII và bảng điều hành khi không phải `Admin`. |

---

## 5. Bảo vệ Chống IDOR Tầng Service (`S02` → `S20`)

Mọi thao tác sửa, xóa, duyệt hoặc xem dữ liệu riêng tư đều xác minh quyền sở hữu bằng `currentUserId` lấy từ `ClaimTypes.NameIdentifier` (đã loại bỏ 100% giá trị mặc định `?? "u-..."`):

- **S02 (`PersonalShelf`)**: `ToggleLendable`, `AddBookToShelf`, `UpdateCopy`, `DeleteCopy`, `UpdateShelf`, `DeleteShelf` yêu cầu `OwnerUserId == currentUserId` / `shelf.UserId == currentUserId`.
- **S03 (`ReadingTracker`)**: `UpdateProgress`, `DeleteProgress` yêu cầu `progress.UserId == currentUserId`.
- **S04 (`BookNotesQuotes`)**: `TogglePublic`, `DeleteNote` yêu cầu `note.ReadingProgress.UserId == currentUserId`.
- **S08 (`LibraryLoanFlow`)**: `CancelRequest` chỉ cho phép người tạo đơn `BorrowerId == currentUserId` hoặc `Librarian`/`Admin`.
- **S09 (`P2PLoanFlow`)**: `ApproveOrReject` chỉ cho phép chủ bản sao `copy.OwnerUserId == currentUserId` (hoặc `Admin`); `AdvanceStage` phân quyền chặt theo từng bước (`InTransit`: Chủ sách/`Curator`/`Admin`; `HandoverBorrowed`: yêu cầu `Approved` và thuộc về Chủ sách/Người mượn/`Curator`/`Admin`; `Returned`: Chủ sách/`Curator`/`Admin`).
- **S10 (`ConditionReport`)**: `SaveConditionReport`, `UpdateReport`, `DeleteReport` chỉ cho phép người tham gia hợp đồng (`BorrowerId`, `OwnerUserId`) hoặc `Librarian`/`Curator`/`Admin`.
- **S11 (`HotBookQueue`)**: `CancelReservation` chỉ cho phép `res.UserId == currentUserId` hoặc `Librarian`/`Admin`.
- **S12 (`LoanRenewalFine`)**: `Index` lọc hợp đồng và phiếu phạt theo `currentUserId` đối với thành viên thường; `RenewLoan` kiểm tra `BorrowerId == currentUserId` hoặc `Librarian`/`Admin` (trả `403` nếu vi phạm).
- **S13 (`GeminiAiAssistant`)**: `SendMessage`, `DeleteSession`, `GetPage` với `sessionId` đều kiểm tra `session.UserId == currentUserId`.
- **S15 (`TrustScoreReview`)**: Chặn tự đánh giá chính mình, chặn đánh giá trùng trên cùng `Loan + TargetType`, kiểm tra `LoanId` tồn tại, và `UpdateReview`/`DeleteReview` chỉ cho phép `ReviewerId == currentUserId` hoặc `Admin`.
- **S17 (`SandboxPayment`)**: `VerifyCallback`, `CancelTransaction` chỉ cho phép chủ giao dịch `tx.UserId == currentUserId` (hoặc `Librarian`/`Admin`). Chữ ký `HMAC-SHA512` kiểm tra bằng `CryptographicOperations.FixedTimeEquals` và chặn xử lý lặp lại khi trạng thái khác `"Pending"`.
- **S18 (`NotificationHub`)**: `Index` chỉ tải thông báo của `currentUserId` (chế độ `viewAll` chỉ mở khi `Admin`); `MarkRead`, `MarkAllRead`, `DeleteNotification` chỉ tác động bản ghi thuộc `currentUserId`.
- **S19 (`DisputeResolution`)**: `CreateDispute`, `UpdateDispute`, `DeleteDispute` chỉ cho phép người mượn, chủ sách, `Curator`, `Librarian` hoặc `Admin`.
- **S20 (`ExchangeHubClub`)**: `RegisterCopyDropoffAtHub` chỉ cho phép `copy.OwnerUserId == currentUserId` hoặc `Curator`/`Admin`.

---

## 6. Bảo vệ CSRF, OTP & Chống Bot (reCAPTCHA v3)

- **CSRF:** Toàn bộ POST endpoint (MVC & JSON API) gắn `[ValidateAntiForgeryToken]`, đầu trang `_Layout.cshtml` cấp `<meta name="csrf-token" ...>` và `wwwroot/js/core-api.js` tự động đính kèm header `RequestVerificationToken`.
- **OTP 6 số (`S01`/`S18`):** Sinh mã ngẫu nhiên an toàn, băm `SHA-256` trước khi lưu `OtpVerifications`, giới hạn thời gian hiệu lực và số lần sử dụng; khi chưa điền `Resend__ApiKey` chỉ trả `devOtpCode` ở môi trường phát triển để kiểm thử.
- **reCAPTCHA v3 (`IRecaptchaService`):** Bảo vệ `Login`, `Register`, `SendOtp`, `QuickRequestLoan`; khi `ReCaptcha__SiteKey` / `ReCaptcha__SecretKey` để trống ở môi trường cục bộ, dịch vụ tự động bỏ qua an toàn; khi cấu hình key thật, xác minh qua Google `siteverify` yêu cầu `score >= 0.5`.

---

## 7. Kiểm soát Bảo mật Bổ sung Sprint 3

1. **Đăng nhập QR 2 Chiều (`/api/qr-login/*` — `QrAuthService`):**
   - Sinh token 32 byte bằng `RandomNumberGenerator`, chỉ lưu mã băm `SHA-256` vào bảng `QrLoginSessions`.
   - Thời hạn ngắn (`120s` cho `WebLoginByMobile`, `60s` cho `MobileLoginByWeb`), giới hạn tốc độ tối đa `20 lần/phút/IP` (`HTTP 429`).
   - Xác nhận nhận phiên (`claim` / `mobile-claim`) dùng cập nhật SQL nguyên tử chuyển trạng thái `Approved -> Consumed` một lần duy nhất (mọi yêu cầu phát lại đều nhận `HTTP 400`).
   - Kiểm tra `LockoutEnd > DateTimeOffset.UtcNow` ở cả bước phê duyệt (`ApproveWebLoginSessionAsync` -> `400`) lẫn bước nhận Cookie (`ClaimWebLoginSessionAsync` / `ClaimMobileLinkSessionAsync` -> `403`), kèm tạo thông báo cảnh báo bảo mật tại `S18`.
2. **QR Bàn giao & Hoàn trả Sách Vật lý (`/api/handover-qr/*` — `BookHandoverQrService`):**
   - Lưu băm `SHA-256` của token 32 byte và mã dự phòng 6 ký tự (hiệu lực `10 phút`, chỉ dùng 1 lần).
   - Bảo vệ Anti-IDOR chặt chẽ: bên thứ ba không thuộc hợp đồng cố tình xác nhận nhận sách (`Handover`) bị chặn **`HTTP 403 Forbidden`**; quét lại mã `Consumed` bị từ chối **`HTTP 400`**.
3. **Khép kín Tài chính Tranh chấp (`S19 <-> S17`):**
   - Khi Admin phân xử `Resolved` kèm `CompensationAmount > 0`, `Loan.Status` tiếp tục giữ trạng thái `"Disputed"` (người mượn vẫn bị chặn tạo phiếu mới `HTTP 400`) và hệ thống phát hành giao dịch `"Pending"` ký `HMAC-SHA512` bên `S17`; chỉ khi thanh toán đối soát `"Completed"` mới xóa nợ và mở khóa quyền mượn.
4. **Trợ lý AI Copilot 2 Tầng (`S13GeminiAiAssistant/ExecuteCopilotAction`):**
   - Các lệnh thực thi (`renew_loan`, `update_reading_progress`, `send_my_reminder_email`) xác minh quyền sở hữu `currentUserId` qua Service gốc (`S03`, `S12`, `S18`); riêng `send_due_reminders_batch` khóa chặt vai trò `Librarian,Admin` (`403 Forbidden` nếu `User` hoặc `Curator` gọi trực tiếp).
   - Toàn bộ thẻ xác nhận và nút lệnh phía Client được dựng bằng DOM API an toàn (`innerText`), chống XSS.
5. **Mobile Companion API (`/api/mobile/*`) & Android WebView Shell:**
   - Mọi request tới `/api/*` khi chưa đăng nhập hoặc thiếu quyền nhận **`HTTP 401`/`403` JSON** trong `Program.cs` thay vì chuyển hướng HTML `302`.
   - Google OAuth trên Android mở qua Chrome Custom Tabs (`CustomTabsIntent` + Deep Link `bookshare://open`) tránh lỗi `403 disallowed_useragent`, đồng thời giới hạn quyền Camera (`RESOURCE_VIDEO_CAPTURE`) đúng origin `BuildConfig.BASE_URL`.
