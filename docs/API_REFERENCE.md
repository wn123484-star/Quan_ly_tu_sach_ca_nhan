# TÀI LIỆU THAM CHIẾU API & ROUTE — BOOKSHARE PORTAL

Tài liệu liệt kê toàn bộ các endpoint HTTP (`GET` / `POST`) trên 23 Controller (`HomeController` + `S01` → `S22`) của dự án BookShare (.NET 10 MVC).

> **Quy ước chung cho mọi `POST` API:**
> - Yêu cầu CSRF Token (`RequestVerificationToken` header qua `apiFetch()` hoặc input ẩn `__RequestVerificationToken`).
> - Gửi kèm `Accept: application/json` nhận mã `401 Unauthorized` (chưa đăng nhập) hoặc `403 Forbidden` (thiếu vai trò / vi phạm quyền sở hữu IDOR) dưới dạng JSON `{ "message": "..." }`.

---

## 0. Cổng Khám phá Chung (`HomeController`)

| Method | Đường dẫn | Quyền | Tham số / Payload | Mô tả & Phản hồi |
| :--- | :--- | :--- | :--- | :--- |
| `GET` | `/` | Công khai | `?keyword=&source=all&category=` | Trang chủ Portal tìm kiếm sách giấy 0đ (Kho chung + P2P). |
| `POST` | `/api/ai-chat/quick-ask` | Công khai (tự tách quyền) | `{ message }` | Hỏi nhanh Trợ lý AI Copilot 2 tầng: trả lời + `clientCommands` (`set_theme`, `navigate_to`, `search_catalog`) và `actionCards` cho thành viên đã đăng nhập. |
| `POST` | `/api/qr-login/create` | Công khai (Rate limit 20/phút) | `{}` | Khởi tạo phiên QR đăng nhập Web (`WebLoginByMobile`, hiệu lực `120s`, chỉ lưu SHA-256). |
| `GET` | `/api/qr-login/status/{sessionId}` | Công khai | `{sessionId}` | Kiểm tra trạng thái phiên QR (`Pending`/`Approved`/`Consumed`/`Rejected`/`Expired`). |
| `POST` | `/api/qr-login/preview` | `[Authorize]` | `PreviewQrLoginRequestDto` | Xem trước thiết bị & IP của mã `BOOKSHARE-QRLOGIN` trước khi xác nhận. |
| `POST` | `/api/qr-login/approve` | `[Authorize]` | `ApproveQrLoginRequestDto` | Phê duyệt hoặc từ chối phiên đăng nhập máy tính (kiểm tra `LockoutEnd`). |
| `POST` | `/api/qr-login/claim` | Công khai | `ClaimQrLoginRequestDto` | Nhận Cookie đăng nhập 1 lần (atomic `Approved -> Consumed`, replay trả `400`, tài khoản khóa trả `403`). |
| `POST` | `/api/qr-login/create-mobile-link` | `[Authorize]` | `{}` | Tạo mã QR liên kết thiết bị di động `BOOKSHARE-MOBILELINK` hiệu lực `60s`. |
| `POST` | `/api/qr-login/mobile-claim` | Công khai | `MobileClaimQrLoginRequestDto` | Đổi mã liên kết 1 lần sang Cookie đăng nhập (replay trả `400`). |
| `POST` | `/api/handover-qr/create` | `[Authorize]` (Chủ sách/Staff/Curator) | `CreateHandoverQrRequestDto` | Tạo mã QR bàn giao (`Handover`) hoặc thu hồi (`Return`) hiệu lực `10 phút` + mã dự phòng 6 ký tự. |
| `POST` | `/api/handover-qr/confirm` | `[Authorize]` + Anti-IDOR | `ConfirmHandoverQrRequestDto` | Xác nhận nhận/trả sách thực tế qua QR/ShortCode (`403` nếu không thuộc hợp đồng, `400` nếu dùng lại). |
| `GET` | `/api/mobile/notifications/unread` | `[Authorize]` (`401` JSON) | `?sinceId=0` | API cho Android `NotificationPoller` lấy tối đa 10 thông báo chưa đọc mới nhất + `targetUrl`. |
| `GET` | `/api/mobile/session` | Công khai | — | Trả trạng thái phiên (`authenticated`, `userId`, `role`, `unreadCount`) cho Mobile Shell. |

---

## 1. Nhóm Xác thực, Cá nhân & Biên mục (`S01` → `S07`)

| Module | Method | Endpoint | Quyền | Input / DTO | Nghiệp vụ & Mã trạng thái |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **S01** | `GET` | `/S01AuthOtpGoogle` | Công khai | `?returnUrl=` | Giao diện Đăng nhập, Đăng ký, Xác thực Google OAuth & Hồ sơ (khi đã đăng nhập). |
| **S01** | `POST` | `/S01AuthOtpGoogle/Login` | Công khai (+reCAPTCHA) | `LoginInputModel` | Xác thực Email/Mật khẩu, kiểm tra `LockoutEnd`, nâng cấp hash Identity chuẩn. |
| **S01** | `POST` | `/S01AuthOtpGoogle/Register` | Công khai (+reCAPTCHA) | `RegisterInputModel` | Tạo tài khoản mới luôn gán vai trò `"User"`, tự đăng nhập. |
| **S01** | `POST` | `/S01AuthOtpGoogle/Logout` | `[Authorize]` | Form rỗng | Đăng xuất khỏi phiên hiện tại. |
| **S01** | `POST` | `/S01AuthOtpGoogle/SendOtp` | Công khai (+reCAPTCHA) | `SendOtpRequestDto` | Tạo OTP 6 số (băm `SHA-256`), gửi qua Resend/SMTP (hoặc trả `devOtpCode` ở Dev). |
| **S01** | `POST` | `/S01AuthOtpGoogle/VerifyOtp` | Công khai | `VerifyOtpRequestDto` | Đối chiếu mã OTP và đánh dấu `EmailConfirmed = true`. |
| **S01** | `POST` | `/S01AuthOtpGoogle/GoogleLogin` | Công khai | `?returnUrl=` | Fail-closed: trả **`400`** nếu `.env` chưa có Google OAuth key thật; ngược lại Challenge Google. |
| **S01** | `GET` | `/S01AuthOtpGoogle/GoogleCallback` | Công khai | `?returnUrl=` | Liên kết/tạo người dùng từ Google; tự `SignOutAsync` nếu bị khóa hoặc lỗi upsert. |
| **S01** | `POST` | `/S01AuthOtpGoogle/UpdateProfile` | `[Authorize]` | `UpdateProfileRequestDto` | Cập nhật `FullName`, `PhoneNumber`, `Address`, `AvatarUrl` của chính tài khoản đăng nhập. |
| **S01** | `POST` | `/S01AuthOtpGoogle/ChangePassword` | `[Authorize]` | `ChangePasswordRequestDto` | Đổi mật khẩu qua `UserManager` Identity (vô hiệu hóa mật khẩu cũ). |
| **S01** | `POST` | `/S01AuthOtpGoogle/SubmitCuratorRequest` | `[Authorize]` | `SubmitCuratorRequestDto` | Gửi đề nghị nâng quyền lên Chủ nhiệm CLB (`RoleUpgradeRequests`), chặn gửi trùng khi đang `Pending`. |
| **S02** | `GET` | `/S02PersonalShelf` | Công khai (riêng tư theo user) | `?shelfId=` | Danh sách kệ sách & bản sao cá nhân của user đang đăng nhập. |
| **S02** | `POST` | `/S02PersonalShelf/ToggleLendable` | `[Authorize]` + Owner | `ToggleLendableRequestDto` | Bật/tắt `IsLendable` cho bản sao P2P (`403` nếu không phải chủ sở hữu). |
| **S02** | `POST` | `/S02PersonalShelf/CreateShelf` | `[Authorize]` | `CreateShelfRequestDto` | Tạo kệ sách cá nhân mới. |
| **S02** | `POST` | `/S02PersonalShelf/AddBookToShelf` | `[Authorize]` + Owner | `AddBookToShelfRequestDto` | Tạo bản sao cá nhân mới / thêm bản sao vào kệ. |
| **S02** | `POST` | `/S02PersonalShelf/UpdateCopy` | `[Authorize]` + Owner | `UpdateBookCopyRequestDto` | Cập nhật tình trạng, ghi chú, quyền cho mượn bản sao (`403` nếu IDOR). |
| **S02** | `POST` | `/S02PersonalShelf/DeleteCopy` | `[Authorize]` + Owner | `DeleteBookCopyRequestDto` | Xóa bản sao cá nhân chưa đang được mượn (`403` nếu IDOR). |
| **S02** | `POST` | `/S02PersonalShelf/UpdateShelf` | `[Authorize]` + Owner | `UpdateShelfRequestDto` | Đổi tên / mô tả kệ sách cá nhân. |
| **S02** | `POST` | `/S02PersonalShelf/DeleteShelf` | `[Authorize]` + Owner | `DeleteShelfRequestDto` | Xóa kệ sách cá nhân. |
| **S03** | `GET` | `/S03ReadingTracker` | Công khai (lọc theo user) | — | Mục tiêu & tiến độ đọc cá nhân. |
| **S03** | `GET` | `/S03ReadingTracker/ChartData` | Công khai (rỗng nếu Guest) | — | Dữ liệu JSON cho biểu đồ Chart.js tiến độ đọc. |
| **S03** | `POST` | `/S03ReadingTracker/UpdateProgress` | `[Authorize]` + Owner | `UpdateReadingProgressRequestDto` | Cập nhật số trang, trạng thái, hạn hoàn thành (`403` nếu không thuộc user). |
| **S03** | `POST` | `/S03ReadingTracker/AddBookToTracker` | `[Authorize]` | `AddTrackingBookRequestDto` | Thêm đầu sách vào danh sách theo dõi đọc. |
| **S03** | `POST` | `/S03ReadingTracker/DeleteProgress` | `[Authorize]` + Owner | `DeleteReadingProgressRequestDto` | Xóa mục tiêu tiến độ đọc. |
| **S04** | `GET` | `/S04BookNotesQuotes` | Công khai (ghi chú công khai) | `?bookId=&community=` | Sổ tay ghi chú & trích dẫn theo trang. |
| **S04** | `POST` | `/S04BookNotesQuotes/SaveNote` | `[Authorize]` | `SaveBookNoteRequestDto` | Lưu ghi chú & trích dẫn trang sách. |
| **S04** | `POST` | `/S04BookNotesQuotes/TogglePublic` | `[Authorize]` + Owner | `ToggleNotePublicRequestDto` | Bật/tắt chia sẻ ghi chú cộng đồng (`403` nếu IDOR). |
| **S04** | `POST` | `/S04BookNotesQuotes/DeleteNote` | `[Authorize]` + Owner | `DeleteBookNoteRequestDto` | Xóa ghi chú (`403` nếu IDOR). |
| **S05** | `GET` | `/S05IsbnAutoCatalog` | Công khai | — | Giao diện tra cứu & biên mục ISBN. |
| **S05** | `POST` | `/S05IsbnAutoCatalog/LookupIsbn` | Công khai | `IsbnLookupQueryDto` | Tra cứu từ DB nội bộ, kho ISBN chuẩn & OpenLibrary API. |
| **S05** | `POST` | `/S05IsbnAutoCatalog/SaveBookAndCopy` | `[Authorize]` | `SaveIsbnBookAndCopyRequestDto` | Lưu đầu sách & sinh bản sao (`403` nếu `User`/`Curator` chọn `TargetOwnerType = "System"`). |
| **S05** | `POST` | `/S05IsbnAutoCatalog/UpdateCatalogBook` | `Librarian,Admin` | `UpdateCatalogBookRequestDto` | Cập nhật thông tin đầu sách danh mục. |
| **S05** | `POST` | `/S05IsbnAutoCatalog/DeleteCatalogBook` | `Librarian,Admin` | `DeleteCatalogBookRequestDto` | Xóa đầu sách danh mục (nếu chưa có bản sao ràng buộc). |
| **S06** | `GET` | `/S06SearchDiscovery` | Công khai | `?keyword=&author=&isbn=&...` | Tìm kiếm đa tiêu chí & phân trang SQL. |
| **S06** | `GET` | `/S06SearchDiscovery/Suggest` | Công khai | `?q=` | Gợi ý autocomplete JSON theo tên sách/tác giả/ISBN. |
| **S06** | `POST` | `/S06SearchDiscovery/QuickRequestLoan` | `[Authorize]` (+reCAPTCHA) | `QuickLoanRequestDto` | Đặt mượn nhanh qua `ILoanFlowCoordinatorService` (kiểm tra hạn mức, `TrustScore >= 50`, quá hạn, tranh chấp). |
| **S07** | `GET` | `/S07LibraryCatalog` | Công khai | `?libraryId=&status=&page=` | Tồn kho thư viện đối tác & vị trí kệ. |
| **S07** | `POST` | `/S07LibraryCatalog/UpdateShelfLocation` | `Librarian,Admin` | `S07UpdateShelfRequestDto` | Cập nhật vị trí kệ vật lý (`403` với `User`/`Curator`). |

---

## 2. Nhóm Mượn – Trả, Kiểm định, AI & Uy tín (`S08` → `S15`)

| Module | Method | Endpoint | Quyền | Input / DTO | Nghiệp vụ & Liên thông |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **S08** | `GET` | `/S08LibraryLoanFlow` | Công khai (lọc theo vai trò) | — | Danh sách phiếu kho chung của user (hoặc toàn hệ thống nếu `Librarian`/`Admin`). |
| **S08** | `POST` | `/S08LibraryLoanFlow/CreateRequest` | `[Authorize]` | `CreateLibraryLoanRequestDto` | Tạo yêu cầu mượn kho chung (kiểm tra eligibility + quota + `TrustScore`). |
| **S08** | `POST` | `/S08LibraryLoanFlow/ApproveOrReject` | `Librarian,Admin` | `ApproveRejectLibraryLoanDto` | Duyệt (giữ bản sao `Reserved`) hoặc từ chối phiếu kho chung. |
| **S08** | `POST` | `/S08LibraryLoanFlow/ConfirmHandover` | `Librarian,Admin` | `HandoverLibraryLoanDto` | Giao sách kho chung, tạo `Loan` `Active` & bản sao `Borrowed`. |
| **S08** | `POST` | `/S08LibraryLoanFlow/ConfirmReturn` | `Librarian,Admin` | `ReturnLibraryLoanDto` | Thu hồi sách trả (`Returned`), lập biên bản `Return` & đôn hàng chờ `S11`. |
| **S08** | `POST` | `/S08LibraryLoanFlow/CancelRequest` | `[Authorize]` + Owner/Staff | `CancelLibraryLoanRequestDto` | Hủy phiếu chờ duyệt (`403` nếu IDOR). |
| **S09** | `GET` | `/S09P2PLoanFlow` | Công khai (lọc theo user/Admin) | — | Theo dõi phiếu mượn P2P giữa chủ sách và bạn đọc. |
| **S09** | `POST` | `/S09P2PLoanFlow/CreateRequest` | `[Authorize]` | `CreateP2PLoanRequestDto` | Tạo phiếu mượn P2P + chọn điểm hẹn Hub (chặn tự mượn sách mình). |
| **S09** | `POST` | `/S09P2PLoanFlow/ApproveOrReject` | `[Authorize]` + Chủ sách/`Admin` | `ApproveP2PLoanDto` | Duyệt/từ chối P2P (`400` nếu bản sao đã giữ chỗ bởi đơn khác, `403` nếu không phải chủ sách). |
| **S09** | `POST` | `/S09P2PLoanFlow/AdvanceStage` | `[Authorize]` + Role theo bước | `P2PStageTransitionDto` | Chuyển `InTransit` -> `HandoverBorrowed` (tạo `Loan`, `+1 LendQuotaBonus`, `+3 TrustScore`) -> `Returned` (đôn hàng chờ `S11`). |
| **S10** | `GET` | `/S10ConditionReport` | Công khai (lọc theo user/Staff) | `?loanId=` | Danh sách hợp đồng đủ điều kiện & ảnh đối chiếu Handover/Return. |
| **S10** | `POST` | `/S10ConditionReport/UploadReport` | `[Authorize]` + Participant | Multipart form + `IFormFile` | Kiểm tra định dạng/kích thước ảnh, lưu `/uploads/conditions/` & bảng `ConditionReports` (`403` nếu ngoài cuộc). |
| **S10** | `POST` | `/S10ConditionReport/UpdateReport` | `[Authorize]` + Owner/Staff | `UpdateConditionReportRequestDto` | Cập nhật phân loại & ghi chú biên bản ảnh. |
| **S10** | `POST` | `/S10ConditionReport/DeleteReport` | `[Authorize]` + Owner/Staff | `DeleteConditionReportRequestDto` | Xóa biên bản ảnh của chính mình (`403` nếu IDOR). |
| **S11** | `GET` | `/S11HotBookQueue` | Công khai | — | Hàng chờ sách hot ưu tiên gói `Premium` trước `Free`. |
| **S11** | `POST` | `/S11HotBookQueue/Enqueue` | `[Authorize]` | `EnqueueReservationRequestDto` | Đăng ký xếp hàng đặt trước đầu sách đang bận. |
| **S11** | `POST` | `/S11HotBookQueue/NotifyTop` | `Librarian,Admin` | `NotifyTopQueueRequestDto` | Gửi thông báo giữ chỗ 48h cho độc giả đứng đầu hàng chờ. |
| **S11** | `POST` | `/S11HotBookQueue/Cancel` | `[Authorize]` + Owner/Staff | `CancelReservationRequestDto` | Hủy đặt chỗ (`403` nếu IDOR). |
| **S12** | `GET` | `/S12LoanRenewalFine` | Công khai (lọc theo user/Staff) | — | Danh sách hợp đồng & phiếu phạt thuộc user (hoặc toàn bộ nếu `Librarian`/`Admin`). |
| **S12** | `POST` | `/S12LoanRenewalFine/RenewLoan` | `[Authorize]` + Borrower/Staff | `RenewLoanRequestDto` | Gia hạn +7 ngày (tối đa 2 lần, chặn khi có hàng chờ `S11`, `403` nếu IDOR). |
| **S12** | `POST` | `/S12LoanRenewalFine/AssessFine` | `Librarian,Admin` | `AssessOverdueFineRequestDto` | Tính phạt 5.000đ/ngày quá hạn, tạo `Transaction` `Fine` liên kết thanh toán qua `S17`. |
| **S13** | `GET` | `/S13GeminiAiAssistant` | Công khai (riêng tư theo user) | `?sessionId=` | Giao diện Trợ lý AI Gemini (Smart Local RAG Fallback khi trống API Key). |
| **S13** | `POST` | `/S13GeminiAiAssistant/SendMessage` | `[Authorize]` + Owner | `SendAiChatRequestDto` | Gửi câu hỏi Multi-turn (`8` tin nhắn gần nhất, trả thêm `clientCommands` & `actionCards`, `403` nếu truy cập session người khác). |
| **S13** | `POST` | `/S13GeminiAiAssistant/ExecuteCopilotAction` | `[Authorize]` + Anti-IDOR / RBAC | `ExecuteCopilotActionRequestDto` | Thực thi Action Card (`renew_loan`, `update_reading_progress`, `send_my_reminder_email`, `send_due_reminders_batch` — trả `403` nếu không phải `Librarian`/`Admin`). |
| **S13** | `POST` | `/S13GeminiAiAssistant/CreateSession` | `[Authorize]` | `CreateAiSessionRequestDto` | Tạo phiên hội thoại mới. |
| **S13** | `POST` | `/S13GeminiAiAssistant/DeleteSession` | `[Authorize]` + Owner | `DeleteAiSessionRequestDto` | Xóa phiên hội thoại AI. |
| **S14** | `GET` | `/S14AiRecommendation` | Công khai | `?mood=&major=` | Gợi ý cá nhân hóa từ lịch sử đọc & kho sách thực tế. |
| **S14** | `POST` | `/S14AiRecommendation/GenerateCustomAdvice` | `[Authorize]` | `S14GenerateCustomRecRequestDto` | Sinh lộ trình đọc 14 ngày theo tâm trạng & mục tiêu kỹ năng. |
| **S14** | `POST` | `/S14AiRecommendation/QuickAddToTracker` | `[Authorize]` | `AddTrackingBookRequestDto` | Thêm nhanh sách gợi ý vào mục tiêu đọc `S03`. |
| **S15** | `GET` | `/S15TrustScoreReview` | Công khai | — | Đánh giá 2 chiều (`Book`/`User`), bảng xếp hạng uy tín `TrustScore`. |
| **S15** | `POST` | `/S15TrustScoreReview/SubmitReview` | `[Authorize]` | `SubmitReviewRequestDto` | Gửi đánh giá, cộng/trừ `TrustScore` (chặn tự đánh giá, chặn trùng `Loan`, xác minh `LoanId` tồn tại). |
| **S15** | `POST` | `/S15TrustScoreReview/UpdateReview` | `[Authorize]` + Owner/`Admin` | `UpdateReviewRequestDto` | Cập nhật nhận xét đã gửi (`403` nếu IDOR). |
| **S15** | `POST` | `/S15TrustScoreReview/DeleteReview` | `[Authorize]` + Owner/`Admin` | `DeleteReviewRequestDto` | Xóa đánh giá & hoàn tác điểm `TrustScore` tương ứng (`403` nếu IDOR). |

---

## 3. Nhóm Gói Thành viên, Ví Bảo lãnh, CLB Hub, Thủ thư & Admin (`S16` → `S22`)

| Module | Method | Endpoint | Quyền | Input / DTO | Nghiệp vụ & Liên thông |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **S16** | `GET` | `/S16MembershipTier` | Công khai | — | Hiển thị hạng thẻ hiện tại, tổng hạn mức mượn (`Base + Bonus`). |
| **S16** | `POST` | `/S16MembershipTier/SwitchMembership` | `[Authorize]` | `S16SwitchMembershipRequestDto` | Chuyển `Free` miễn phí hoặc khởi tạo đơn `Pending` chuyển tiếp sang `/S17SandboxPayment?txId=...` kèm `expectedHmac`. |
| **S17** | `GET` | `/S17SandboxPayment` | Công khai (lọc theo user/Staff) | `?txId=` | Ví bảo lãnh & lịch sử giao dịch; tự điền chuỗi chuẩn & chữ ký cho `txId` hợp lệ. |
| **S17** | `POST` | `/S17SandboxPayment/CreateOrder` | `[Authorize]` | `S17CreateSandboxOrderDto` | Tạo lệnh `Membership` / `Fine` / `SimulatedDeposit` kèm `HMAC-SHA512`. |
| **S17** | `POST` | `/S17SandboxPayment/VerifyCallback` | `[Authorize]` + Owner/Staff | `S17SandboxCallbackDto` | Đối soát chữ ký `HMAC-SHA512` `FixedTimeEquals` (chặn tamper `400`, chặn replay `400`, chặn IDOR `403`); tự kích hoạt `Premium` hoặc xóa nợ `FineAmount`. |
| **S17** | `POST` | `/S17SandboxPayment/RefundDeposit` | `Librarian,Admin` | `S17RefundDepositDto` | Hoàn trả 100% cọc bảo lãnh (`SimulatedDeposit`). |
| **S17** | `POST` | `/S17SandboxPayment/CancelTransaction` | `[Authorize]` + Owner/`Admin` | `S17CancelTransactionDto` | Hủy lệnh giao dịch chưa hoàn tất (`403` nếu IDOR). |
| **S18** | `GET` | `/S18NotificationHub` | Công khai (lọc theo user) | `?typeFilter=&viewAll=` | Thông báo cá nhân; `viewAll=true` chỉ mở cho `Admin`. |
| **S18** | `GET` | `/S18NotificationHub/UnreadCount` | Công khai (`0` nếu Guest) | — | Số thông báo chưa đọc của user hiện tại. |
| **S18** | `POST` | `/S18NotificationHub/MarkRead` | `[Authorize]` + Owner | `S18MarkNotificationRequestDto` | Đánh dấu đã đọc 1 thông báo thuộc sở hữu (`403` nếu IDOR). |
| **S18** | `POST` | `/S18NotificationHub/MarkAllRead` | `[Authorize]` + Owner | — | Đánh dấu toàn bộ thông báo của chính mình đã đọc. |
| **S18** | `POST` | `/S18NotificationHub/TriggerResendEmail` | `Librarian,Admin` | `S18TriggerResendCampaignDto` | Gửi nhắc hạn trả / báo sách hàng chờ qua Resend/SMTP. |
| **S18** | `POST` | `/S18NotificationHub/DeleteNotification` | `[Authorize]` + Owner | `S18DeleteNotificationRequestDto` | Xóa thông báo cá nhân (`403` nếu IDOR). |
| **S19** | `GET` | `/S19DisputeResolution` | Công khai (ẩn danh sách/đối chiếu theo quyền) | — | Hồ sơ tranh chấp kèm ảnh đối chiếu từ `S10`. |
| **S19** | `POST` | `/S19DisputeResolution/CreateDispute` | `[Authorize]` + Participant | `S19CreateDisputeRequestDto` | Mở khiếu nại trên `Loan` -> khóa mượn mới cho người mượn cho tới khi `Admin` xử lý. |
| **S19** | `POST` | `/S19DisputeResolution/ResolveDispute` | `Admin` | `S19AdminResolveDisputeDto` | Phân xử (`Resolved`/`Rejected`), phạt `TrustScore`, khấu trừ cọc hoặc phát hành hóa đơn bồi thường `CompensationAmount` (`Pending` qua `S17` giữ `Disputed` tới khi thanh toán), cập nhật tình trạng bản sao. |
| **S19** | `POST` | `/S19DisputeResolution/UpdateDispute` | `[Authorize]` + Reporter/`Admin` | `S19UpdateDisputeRequestDto` | Bổ sung lý do khiếu nại khi hồ sơ còn mở. |
| **S19** | `POST` | `/S19DisputeResolution/DeleteDispute` | `[Authorize]` + Reporter/`Admin` | `S19DeleteDisputeRequestDto` | Hủy khiếu nại khi chưa phân xử xong. |
| **S20** | `GET` | `/S20ExchangeHubClub` | Công khai (ẩn nút điều hành theo Role) | — | Danh sách Điểm trạm CLB, đơn ký gửi/giao nhận Hub. |
| **S20** | `POST` | `/S20ExchangeHubClub/RegisterDropoff` | `[Authorize]` + Chủ sách/`Curator`/`Admin` | `S20RegisterCopyDropoffDto` | Đăng ký ký gửi bản sao cá nhân tại Hub (`403` nếu IDOR). |
| **S20** | `POST` | `/S20ExchangeHubClub/VerifyPhysicalBook` | `Curator,Admin` | `S20CuratorVerifyCopyDto` | Kiểm định sách thật tại Hub, cộng `+2 TrustScore` tối đa 1 lần cho mỗi bản sao. |
| **S20** | `POST` | `/S20ExchangeHubClub/ProcessHubHandover` | `Curator,Admin` | `S20CuratorHandoverActionDto` | Xử lý ký gửi (`CheckInDropoff`), giao cho người mượn (`HandoverToBorrower`, +1 quota bonus), thu hồi (`ConfirmReturnAtHub`). |
| **S20** | `POST` | `/S20ExchangeHubClub/UpdateHubStation` | `Curator,Admin` | `S20UpdateHubStationDto` | Cập nhật tên, địa chỉ, giờ mở cửa & điện thoại Điểm trạm Hub. |
| **S21** | `GET` | `/S21LibrarianDesk` | Công khai (ẩn PII/thao tác nếu không phải `Librarian`/`Admin`) | — | Quầy Thủ thư: quét Barcode, quá hạn, nhập lô, trạng thái bản sao. |
| **S21** | `POST` | `/S21LibrarianDesk/QuickScanBarcode` | `Librarian,Admin` | `S21QuickScanBarcodeDto` | Xuất kho/thu hồi nhanh tại quầy (`AutoDetect`, `Checkout`, `Checkin`), kiểm tra eligibility & đôn hàng chờ `S11`. |
| **S21** | `POST` | `/S21LibrarianDesk/BatchImportCopies` | `Librarian,Admin` | `S21BatchImportCopiesDto` | Nhập lô bản sao kho chung (`BS-SYS-...`). |
| **S21** | `POST` | `/S21LibrarianDesk/UpdateCopyState` | `Librarian,Admin` | `S21UpdateCopyStateDto` | Chuyển trạng thái `Available` / `Damaged` / `Lost`. |
| **S21** | `POST` | `/S21LibrarianDesk/DeleteSystemCopy` | `Librarian,Admin` | `S21DeleteSystemCopyDto` | Xóa bản sao kho chung không còn lịch sử ràng buộc. |
| **S22** | `GET` | `/S22AdminAnalytics` | Công khai (ẩn PII & điều khiển nếu không phải `Admin`) | — | Dashboard thống kê hệ thống, đề nghị `Curator`, thành viên & Hangfire Jobs. |
| **S22** | `GET` | `/S22AdminAnalytics/ChartData` | `Admin` | — | Dữ liệu JSON cho 3 biểu đồ Chart.js Admin (`403` với vai trò khác). |
| **S22** | `POST` | `/S22AdminAnalytics/ReviewRoleUpgrade` | `Admin` | `ReviewRoleUpgradeRequestDto` | Phê duyệt hoặc từ chối đề nghị nâng quyền lên `Curator`, tự gán role & gửi thông báo `S18`. |
| **S22** | `POST` | `/S22AdminAnalytics/ChangeUserRole` | `Admin` | `S22ChangeUserRoleDto` | Phân quyền vai trò thành viên (`User`, `Curator`, `Librarian`, `Admin`). |
| **S22** | `POST` | `/S22AdminAnalytics/ToggleUserLock` | `Admin` | `S22ToggleUserLockDto` | Khóa / mở khóa tài khoản qua `LockoutEnd`. |
| **S22** | `POST` | `/S22AdminAnalytics/TriggerDailyJob` | `Admin` | `S22TriggerJobRequestDto` | Kích hoạt thủ công `CheckOverdue`, `ExpireReservations`, `CheckMemberships`, hoặc `AllDailyJobs`. |
| **S22** | `POST` | `/S22AdminAnalytics/ManageSampleData` | `Admin` | `S22ManageSampleDataDto` | `SeedSampleData` (xóa sạch an toàn cả các bảng FK phụ & nạp lại `02_BookShare_SeedData.sql`) hoặc `ResetTransactions`. |
