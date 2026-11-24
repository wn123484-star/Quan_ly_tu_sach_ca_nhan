# PHÂN CÔNG NHIỆM VỤ SPRINT 3 — NHÓM PHÁT TRIỂN BOOKSHARE PORTAL

> **Nguyên tắc phân bổ theo năng lực thực tế (`Top 1 → Top 5`):** Giao đúng phần việc theo thế mạnh từng thành viên — người vững nhất gánh kiến trúc lõi, bảo mật, thanh toán & tích hợp khó; các thành viên tiếp theo phụ trách kiểm thử chuyên sâu theo mảng (Thống kê/Biểu đồ/QA tổng, Nghiệp vụ CRUD, Email/OTP/Thông báo, Biểu tượng/Giao diện/Video Demo) để cả nhóm phối hợp nhịp nhàng, không chồng chéo và đạt chất lượng bảo vệ cao nhất.  
> **Tài liệu kiểm thử đi kèm:** [`docs/TESTER_MANUAL_QA_CHECKLIST.md`](TESTER_MANUAL_QA_CHECKLIST.md)  
> **Nhánh làm việc chính:** `mainforSprint3` | **Môi trường kiểm thử thật:** `https://bookshare.castle-tech.cloud`

---

## BẢNG TỔNG QUAN PHÂN BỔ THEO NĂNG LỰC (TOP 1 → TOP 5)

| Xếp hạng | Thành viên | Vai trò trong Sprint 3 | Trọng tâm công việc | Tỷ trọng độ khó |
| :---: | :--- | :--- | :--- | :---: |
| **Top 1** | **Hưng** | **Team Leader / Principal Full-Stack Architect** | Xương sống hệ thống khó nhất: CSDL & EF Scaffold 0 lệch Docker, Google OAuth 2.0, Ví Tiền Ảo & Chuyển/Hủy gói 4 cấp + Chống spam nguyên tử, QR Thanh Toán đồng bộ Web ↔ Android APK, AI Copilot bền vững, Admin Xóa TK, Vá lỗi Production & Merge PR. | Rất cao (Lõi hệ thống) |
| **Top 2** | **Huyền** | **QA Lead, Thống Kê Biểu Đồ & Tài Liệu Bảo Vệ** | Kiểm thử E2E luồng khó (Tài chính Ví/Gói/QR, RBAC/IDOR, OAuth, Trang lỗi, Mobile App); **Kiểm tra & hoàn thiện số liệu Thống kê – Biểu đồ Dashboard (`S22`, `S03`)**; quản lý Bug Report; hoàn thiện Báo cáo Cuối kỳ & Slide thuyết trình. | Cao |
| **Top 3** | **Huy** | **Nghiệp Vụ CRUD & Kiểm Thử Chu Trình Mượn Trả** | Kiểm thử toàn diện **CRUD (Thêm/Xem/Sửa/Xóa)** & chuỗi nghiệp vụ cốt lõi `S02`–`S12`, `S15`, `S19`–`S21` (Tủ sách, ISBN, Mượn Thư viện/P2P, QR bàn giao, Hàng chờ, Gia hạn, Tranh chấp, Quầy thủ thư); đề xuất & hỗ trợ xử lý các lỗi logic nghiệp vụ. | Khá – Cao |
| **Top 4** | **Lâm** | **Email/OTP, Thông Báo, Cài Đặt Tài Khoản & Dữ Liệu** | Kiểm thử chuyên sâu toàn bộ luồng **Gửi Email (Resend/OTP)**, chốt bắt buộc Email hợp lệ, phân tách `UserName` / `FullName` / `Email` cho mọi cấp trong Cài Đặt (`S01`), **Popup Thông Báo nhanh (`S18`)**, gửi tóm tắt AI qua Email (`S13`) & kiểm chứng dữ liệu mẫu SQL. | Trung bình – Khá |
| **Top 5** | **Kiệt** | **Biểu Tượng (Custom Icons), Giao Diện Đa Style & Video Demo** | Sưu tầm/thiết kế & **upload hoàn thiện bộ 18 Icon tùy chỉnh** tại `/S22AdminAnalytics` (đảm bảo hiện đẹp, 0 lỗi 404, lưu bền vững); kiểm thử thẩm mỹ **5 phong cách Bootstrap (Sketchy...), 10 bảng màu, 5 font chữ, Header & reCAPTCHA**; **Quay Video Demo hoàn chỉnh**. | Vừa sức – Trực quan |

---

## CHI TIẾT TỪNG ISSUE GIAO VIỆC (COPY TRỰC TIẾP LÊN GITHUB ISSUES)

---

### 🥇 TOP 1 — HƯNG (Team Leader / Kiến trúc lõi & Tích hợp khó nhất)
**Tiêu đề GitHub Issue:**  
`[Sprint3] Hưng - Kiến trúc Lõi, Bảo mật Google OAuth/RBAC, Ví Tiền Ảo & Chuyển/Hủy Gói Chống Spam, QR Thanh Toán Web-Android, AI Bền Vững & Deploy VPS`

#### 1. Hạng mục phụ trách chính
1. **CSDL, EF Core Scaffold & Vận hành Production VPS Docker:**
   - Hoàn thiện lược đồ chuẩn `database/01_BookShare_Schema.sql` + `DatabaseBootstrapService.cs` (tự động nâng cấp cột mới không mất dữ liệu VPS, 0 lệch schema với Docker).
   - Vá dứt điểm các lỗi hạ tầng production: `app.UseStaticFiles()` + lưu nhị phân `CustomImageData` cho ảnh biểu tượng (0 lỗi 404), lưu bền vững `DataProtection` keys, gỡ sạch biến ngân hàng thật `VNPay__*`.
2. **Bảo mật lõi & Xác thực nâng cao:**
   - Vá triệt để **Google OAuth 2.0** bằng External Cookie Scheme riêng (`BookShare.External`), làm sạch dấu ngoặc `.env`, giữ nguyên khóa fail-closed khi thiếu key.
   - Rà soát toàn diện **RBAC 4 vai trò**, **Anti-IDOR**, **CSRF**, `OnValidatePrincipal` tự hủy cookie mồ côi và tính năng **Admin Xóa Tài Khoản** dọn sạch khóa ngoại nguyên tử (`/S22AdminAnalytics`).
   - Hoàn thiện cơ chế bắt ngoại lệ toàn cục & trang báo lỗi thân thiện `400/401/403/404/405/500` (tách rạch ròi HTML vs JSON API).
3. **Hệ thống Tài chính Tiền Ảo, Gói Hội Viên 4 Cấp & Chống Spam nguyên tử (`S16`, `S17`):**
   - Triển khai 4 cấp hội viên (`Free`, `Standard`, `Premium`, `Scholar`) thực thi luật mượn thật ở Backend (`MaxConcurrentLoans`, `MaxBorrowDays`, `MaxRenewals`, hàng chờ ưu tiên `S11`, sách hot).
   - Xây dựng cơ chế **Cấm mua trùng gói đang sở hữu**, **Chuyển gói có lệ phí `15.000đ` + khấu trừ ngày còn dư (`PreviewTierSwitch`)**, **Hủy gói an toàn (`CancelMembership`)**.
   - Khóa chặt sổ cái CSDL chống spam: transaction `Serializable` + `UPDLOCK/HOLDLOCK`, `IdempotencyKey` (`UNIQUE INDEX`), gộp hóa đơn `Pending` trùng, giới hạn tốc độ nạp ví (`5 lần/phút` -> `429`) và ghi nhận `BalanceBefore -> BalanceAfter`.
4. **QR Thanh Toán đồng bộ Web ↔ Android App & Trợ lý AI Copilot:**
   - Đồng bộ nút quét QR trung tâm Android WebView Shell (`mobile/bookshare-android/`) + GitHub Actions build APK với luồng Quét QR Thanh Toán & QR Login/Bàn giao trên Web.
   - Nâng cấp Trợ lý AI (`S13` & Widget) lưu lịch sử bền vững theo tài khoản/phân quyền, nút **Reset Chat**, điều khiển giao diện/font/cỡ chữ và gửi tóm tắt qua Email.
5. **Điều phối nhóm:** Review bug do Huyền/Huy/Lâm/Kiệt báo về, hotfix nhanh, chạy bot QA tự động `qa_full_bot.py` (`395/395 PASS`) + `smoke_test.py` (`100% PASS`) và triển khai bản ổn định lên VPS.

#### 2. Tiêu chí hoàn thành (Definition of Done)
- [ ] `dotnet build BookShare.sln` đạt **0 Error(s)**.
- [ ] `qa_full_bot.py --autostart` (L1–L9) đạt **100% PASS (0 FAIL)** & `smoke_test.py` đạt **100% PASS**.
- [ ] Deploy thành công lên `https://bookshare.castle-tech.cloud` không lỗi 500, Google OAuth & upload icon hoạt động ổn định.
- [ ] Đóng sạch 100% bug mức `Blocker` / `Major` từ 4 thành viên báo cáo.

---

### 🥈 TOP 2 — HUYỀN (QA Lead, Thống Kê Biểu Đồ, Báo Cáo Cuối Kỳ & Slide)
**Tiêu đề GitHub Issue:**  
`[Sprint3] Huyền - Kiểm thử QA Tổng thể (Ví/Gói/QR/Bảo mật), Kiểm chứng Thống kê Biểu đồ Dashboard, Báo cáo Cuối kỳ & Slide Bảo vệ`

#### 1. Hạng mục phụ trách chính
1. **Kiểm tra & hoàn thiện mảng Thống kê – Biểu đồ (Analytics & Charts):**
   - Kiểm chứng độ chính xác số liệu trên **Dashboard Quản trị (`/S22AdminAnalytics`)**: đối chiếu từng con số KPI (Tổng đầu sách, Bản sao, Thành viên, Đang mượn, Quá hạn, Tranh chấp) và dữ liệu biểu đồ **Chart.js** với số lượng bản ghi thực tế trong bảng sau mỗi thao tác mượn/trả/nạp tiền/xóa tài khoản.
   - Kiểm tra biểu đồ **Tiến độ đọc sách (`/S03ReadingTracker`)**: số trang đọc, phần trăm hoàn thành, biểu đồ ở cả chế độ **Light Mode**, **Dark Mode**, **Vàng Mộng Mơ**, **Hồng Ấm Áp** và màn hình Mobile (đảm bảo chữ trục biểu đồ không bị chìm màu hoặc tràn khung).
   - Nếu thấy thiếu chỉ số thống kê cần thiết cho thuyết trình (VD: tỷ lệ người dùng theo 4 cấp hội viên, tỷ lệ mượn Thư viện vs P2P), tổng hợp yêu cầu cụ thể kèm ảnh chụp đề xuất cho Hưng bổ sung.
2. **Kiểm thử QA trọng tâm các luồng phức tạp & bảo mật (theo `TESTER_MANUAL_QA_CHECKLIST.md`):**
   - Test trọn bộ **Mục 6 (`PAY-01` → `PAY-09`)**: Nạp tiền ảo, chống spam 6 lần/phút (`429`), đăng ký gói từ `Free`, xác nhận **không mua trùng được gói đang sở hữu**, test công thức **Chuyển gói (`Giá mới - Hoàn dư + Phí 15.000đ`)**, test **Hủy gói** (cả trường hợp đang mượn > 2 cuốn bị chặn), test **Quét QR Thanh Toán** đồng bộ 2 tab/2 máy.
   - Test **Mục 3 (`AUTH-03`, `AUTH-04`, `AUTH-08`)**: Đăng nhập Google OAuth thật, Đăng nhập QR 2 chiều, kiểm tra RBAC/IDOR (User thường không vào được API Admin/Librarian).
   - Test **Mục 10 (`ERR-01`, `ERR-02`)**: Trang lỗi `404` (`/S02Pers`, link sai), trang `403`, đảm bảo không lộ stack trace hay lỗi trắng trang.
3. **Tổng hợp Bug Report, Báo cáo Cuối kỳ & Slide Thuyết trình:**
   - Gom bug từ Huy, Lâm, Kiệt vào danh sách chung có phân mức ưu tiên (`Blocker` / `Major` / `Minor`), theo dõi trạng thái Fix → Retest → Closed.
   - Cập nhật Báo cáo Cuối kỳ (kiến trúc 32 bảng, phân quyền 4 vai trò, luồng Ví Tiền Ảo 0đ bán sách, QR Thanh Toán, AI Copilot, kết quả QA 395/395 PASS) và chuẩn bị **Slide bảo vệ** sắc nét kèm ảnh chụp giao diện thực tế.

#### 2. Tiêu chí hoàn thành (Definition of Done)
- [ ] Hoàn thành kiểm thử 100% các nhóm `PAY-01..09`, `AUTH-03/04/08`, `ERR-01..02` và kiểm chứng khớp 100% số liệu **Thống kê & Biểu đồ** tại `S22` và `S03`.
- [ ] Nộp bảng tổng hợp kết quả Test Run (Pass/Fail) + danh sách Bug đã Retest đạt 100%.
- [ ] Hoàn thiện bản mềm **Báo cáo Cuối kỳ** và **Slide Thuyết trình** gửi nhóm duyệt trước buổi bảo vệ.

---

### 🥉 TOP 3 — HUY (Nghiệp Vụ CRUD & Chu Trình Mượn Trả Cốt Lõi)
**Tiêu đề GitHub Issue:**  
`[Sprint3] Huy - Kiểm thử & Chuẩn hóa Nghiệp vụ CRUD Toàn hệ thống (S02-S12, S15, S19-S21) và Chu trình Mượn/Trả Thư viện & P2P`

#### 1. Hạng mục phụ trách chính
1. **Kiểm thử CRUD đầy đủ (`Create – Read – Update – Delete`) trên từng phân hệ dữ liệu:**
   - **Tủ sách cá nhân (`/S02PersonalShelf`)**: Tạo kệ mới, sửa tên/mô tả/công khai kệ, xóa kệ; cập nhật tình trạng bản sao; bật/tắt công tắc **Cho phép mượn P2P** và kiểm tra tác động thực tế sang trang Mượn P2P.
   - **Tiến độ đọc (`/S03ReadingTracker`) & Ghi chú (`/S04BookNotesQuotes`)**: Thêm sách đọc, cập nhật trang hiện tại (test biên: trang âm, trang > tổng trang, hoàn thành 100%), xóa tiến độ; thêm/sửa/xóa trích dẫn theo trang.
   - **Biên mục ISBN (`/S05IsbnAutoCatalog`) & Kho Thư viện (`/S07LibraryCatalog`)**: Tra cứu ISBN, thêm đầu sách mới + bản sao (xác nhận User chỉ tạo được sách cá nhân, không lọt vào Kho Thư viện), sửa thông tin sách (Librarian/Admin), cập nhật vị trí kệ, xóa sách khi chưa/đang có bản sao mượn.
   - **Tìm kiếm & Bộ lọc (`/`, `/S06SearchDiscovery`)**: Kiểm tra tìm theo tên, tác giả, ISBN, lọc nguồn sách, thể loại, phân trang, chuyển chế độ lưới/danh sách.
2. **Kiểm thử trọn vẹn chuỗi nghiệp vụ Mượn – Trả – Gia hạn – Tranh chấp – Đánh giá:**
   - **Mượn Kho Thư viện (`/S08LibraryLoanFlow`) & Quầy Thủ thư (`/S21LibrarianDesk`)**: Tạo phiếu mượn → Thủ thư duyệt → Quét QR/Barcode bàn giao → Trả sách → Nhập lô bản sao mới → Báo sách hỏng/mất.
   - **Mượn Cộng đồng P2P (`/S09P2PLoanFlow`) & Trạm CLB (`/S20ExchangeHubClub`)**: Tạo 2 yêu cầu mượn cùng 1 cuốn → xác nhận **chặn duyệt trùng** → Chủ nhiệm CLB kiểm định & điều phối giao nhận trung gian → Hoàn trả.
   - **Hộ chiếu tình trạng (`/S10`) & Hàng chờ Sách Hot (`/S11`)**: Upload ảnh kiểm định sách (kiểm tra hiển thị 0 lỗi 404), đặt trước sách hết bản sao, xác nhận thứ tự ưu tiên theo gói hội viên & tự đôn hàng chờ khi sách được trả.
   - **Gia hạn & Phí trễ (`/S12`), Tranh chấp (`/S19`) & Đánh giá (`/S15`)**: Test đúng giới hạn mượn & số ngày/số lần gia hạn của từng gói (`Free`: 2 cuốn/14 ngày/1 lần; `Standard`: 3 cuốn/21 ngày/2 lần; `Premium`: 5 cuốn/30 ngày/2 lần; `Scholar`: 10 cuốn/45 ngày/3 lần); test `User 3` quá hạn/tranh chấp bị chặn mượn; test mở tranh chấp → Admin phân xử → trả bồi thường mở khóa; test đánh giá 2 chiều (chặn tự đánh giá & chặn đánh giá trùng).
3. **Báo lỗi kèm kịch bản tái hiện chuẩn:** Bất kỳ lỗi CRUD, lỗi validate form, lỗi nút bấm không phản hồi hoặc lỗi lệch trạng thái bản sao nào phải ghi rõ bước tái hiện + ảnh chụp + tab Network gửi Hưng/Huyền.

#### 2. Tiêu chí hoàn thành (Definition of Done)
- [ ] Hoàn thành kiểm thử 100% các nhóm `BOOK-01..05` và `LOAN-01..09` trong `TESTER_MANUAL_QA_CHECKLIST.md`.
- [ ] Xác nhận mọi thao tác CRUD (`S02`, `S03`, `S04`, `S05`, `S07`, `S15`, `S20`, `S21`) chạy mượt, dữ liệu sau khi `F5` khớp 100% với CSDL, không có lỗi `500` hay kẹt trạng thái sách.
- [ ] Nộp bảng checklist kết quả CRUD & danh sách bug (kèm ảnh chứng minh đã Retest PASS sau khi fix).

---

### 🏅 TOP 4 — LÂM (Chuyên Trách Email/OTP, Thông Báo, Cài Đặt Tài Khoản & Dữ Liệu)
**Tiêu đề GitHub Issue:**  
`[Sprint3] Lâm - Kiểm thử Hệ thống Email/OTP Resend, Cài đặt Email Mọi Cấp, Popup Thông báo Nhanh (S18), Gửi Mail từ AI & Kiểm chứng Dữ liệu Mẫu`

#### 1. Hạng mục phụ trách chính
1. **Kiểm thử toàn bộ luồng Gửi Email thật (Resend) & Mã OTP 6 chữ số:**
   - Test **Xác minh Email bằng OTP 6 số** tại `/S18NotificationHub` (gửi mã về Gmail thật của Lâm, kiểm tra tiêu đề, nội dung HTML tiếng Việt, thời hạn 5 phút, nhập sai mã bị chặn, nhập đúng mã đổi trạng thái sang **"Đã xác minh OTP"**, thử gửi spam > 5 lần/2 phút bị chặn).
   - Test các **Chiến dịch gửi Email tự động** tại `/S18NotificationHub` (vai trò `Librarian`/`Admin`): *Nhắc hạn trả sách (`DueReminder`)*, *Báo có sách đặt trước hàng chờ (`QueueAvailable`)*, và *Gửi thông báo tùy chỉnh kèm Email*.
   - Test **Gửi bản lưu hội thoại Trợ lý AI qua Email** từ cả **Widget AI góc phải** lẫn trang `/S13GeminiAiAssistant` — mở hộp thư Gmail thật kiểm tra bố cục email hiển thị sạch đẹp, không vỡ font tiếng Việt.
2. **Kiểm thử Cài Đặt Tài Khoản mọi cấp & Chốt bắt buộc Email (`S01`):**
   - Đăng nhập lần lượt cả 4 vai trò (`User`, `Curator`, `Librarian`, `Admin`) → vào `/S01AuthOtpGoogle` (tab **Hồ Sơ & Email**).
   - Xác nhận tách bạch 3 trường: **Tên đăng nhập (`UserName` — chỉ đọc)**, **Tên hiển thị (`FullName`)**, và **Email nhận thư (`Email`)**.
   - Test kịch bản biên:
     - Nhập địa chỉ email vào ô **Tên hiển thị** → phải bị chặn `400`.
     - Nhập Email sai định dạng hoặc trùng Email của tài khoản khác → phải bị chặn `400`.
     - Xóa trống Email rồi lưu → thử bấm gửi OTP / gửi Email từ Chat AI / gửi thông báo tùy chỉnh → hệ thống **phải chặn (`400`)** và hiện thông báo dẫn về **Cài Đặt điền Email**.
     - Điền lại Email thật (VD: Gmail của Lâm) cho tài khoản Admin `admin@bookshare.vn` → xác nhận vẫn đăng nhập được bằng `admin` / `admin@bookshare.vn`, Tên hiển thị vẫn là *"Quản Trị Viên Hệ Thống"*, và thư gửi ra đi thẳng về Gmail mới!
3. **Kiểm thử Popup Thông Báo nhanh & Quản lý Thông báo (`S18`):**
   - Kiểm tra **Chuông thông báo trên Header**: chấm đỏ cập nhật đúng số thông báo chưa đọc, bấm chuông mở **Popup nhanh**, bấm **"Xem nhanh"** hiện nội dung chi tiết & tự đánh dấu đã đọc, bấm **"Mở trang chi tiết →"** điều hướng đúng trang nghiệp vụ (hoặc cuộn & làm nổi bật đúng thẻ trên `/S18NotificationHub?highlight=...`), test nút **"Đọc tất cả"** và nút xóa thông báo.
4. **Kiểm chứng Dữ liệu mẫu & Khôi phục CSDL:**
   - Kiểm tra quy trình Admin bấm **Nạp/Khôi phục dữ liệu mẫu** và **Làm sạch CSDL** tại `/S22AdminAnalytics`: đảm bảo không thiếu đầu sách, không lỗi tiếng Việt UTF-8, không chết trang khi CSDL trắng.

#### 2. Tiêu chí hoàn thành (Definition of Done)
- [ ] Hoàn thành 100% các nhóm `AUTH-05`, `AUTH-06`, `AI-04`, `NOTIF-01..03` trong `TESTER_MANUAL_QA_CHECKLIST.md`.
- [ ] Chụp màn hình bằng chứng **Email thật nhận được trong hộp thư Gmail** (OTP 6 số, Nhắc hạn trả sách, Bản lưu Chat AI) để đưa vào Báo cáo & Slide của Huyền.
- [ ] Xác nhận 100% kịch bản thiếu Email bị chặn đúng và dẫn về Cài Đặt mượt mà, Popup Thông báo hoạt động không lỗi trên cả Desktop & Mobile.

---

### 🎨 TOP 5 — KIỆT (Bộ Icon Tùy Chỉnh, Thẩm Mỹ Đa Giao Diện & Quay Video Demo)
**Tiêu đề GitHub Issue:**  
`[Sprint3] Kiệt - Thiết kế/Upload Bộ 18 Biểu tượng Tùy chỉnh (S22), Kiểm thử Giao diện Đa Style/Font/Màu & Quay Video Demo Hoàn chỉnh`

#### 1. Hạng mục phụ trách chính
1. **Chuẩn bị & Upload bộ Biểu tượng tùy chỉnh (Custom Icons) cho toàn trang (`/S22AdminAnalytics`):**
   - Tìm/thiết kế bộ ảnh icon đẹp, đồng bộ phong cách giáo dục/thư viện (định dạng `.png` nền trong suốt hoặc `.svg` sạch, dung lượng `< 500KB`, kích thước vuông `128x128` hoặc `256x256`) cho các vị trí quan trọng trong Tab **Tùy Chỉnh Biểu Tượng**:
     - `brand_logo` (Logo chính BookShare trên Header & Splash Screen)
     - `brand_favicon` (Icon trên tab trình duyệt)
     - `ai_fab` (Icon nút Trợ lý AI góc phải dưới)
     - `qr_scan` (Icon nút quét QR)
     - Các icon thanh điều hướng (`nav_home`, `nav_discover`, `nav_shelf`, `nav_loan`, `nav_community`, `nav_cart_shelf`) & lối tắt kho sách (`quick_library`, `quick_p2p`, `quick_filter`, `quick_isbn`, `quick_hot`).
   - Đăng nhập `Admin` upload thực tế từng icon lên web → kiểm tra **ảnh hiển thị sắc nét ngay trên Header/nút AI, mở `F12 → Network` xác nhận `200 OK` (0 lỗi 404)**, nhấn **`F5`** tải lại trang xác nhận icon **lưu bền vững không mất**, test công tắc gạt Trái (Mặc định) / Phải (Ảnh tùy chỉnh) và test chặn file sai định dạng (`.txt`/`.exe`).
2. **Kiểm thử thẩm mỹ Giao diện Đa Phong Cách, Bảng Màu, Font & Bố cục (`UI-01` → `UI-07`, `AI-01` → `AI-03`):**
   - Test đủ **5 phong cách Bootstrap** (`Chuẩn`, **`Nét Vẽ Nghệch Ngoặc — Sketchy`**, `Kính Mờ Morph`, `Phẳng Flatly`, `Cyborg`), **10 bảng màu** (đặc biệt **`Vàng Mộng Mơ`**, **`Hồng Ấm Áp`**, `Tím Oải Hương`, `Giấy Cổ`), **5 phông chữ tiếng Việt**, **4 cỡ chữ** và **Light/Dark Mode**.
   - Soi kỹ lỗi thị giác (UI glitches): kiểm tra Header đổ màu đều 2 bên không bị tối lệch, nút **reCAPTCHA góc trái dưới không đè lên Trợ lý AI góc phải dưới**, nút **Reset Chat AI** và lịch sử chat giữ nguyên khi `F5`, ảnh bìa sách không méo/vỡ, kiểm tra giao diện trên màn hình điện thoại (`390px`) và Android App.
   - Chụp bộ ảnh màn hình đẹp (High-res Screenshots) các màn hình nổi bật (Trang chủ, Sketchy, Vàng Mộng Mơ, Hồng Ấm Áp, Ví Tiền Ảo, Quét QR Thanh Toán, Trợ lý AI, Dashboard Admin) gửi Huyền ghép vào Slide & Báo cáo.
3. **Quay & dựng Video Demo sản phẩm hoàn chỉnh (3–5 phút, Full HD 1080p):**
   - Kịch bản Demo mạch lạc theo 6 phân đoạn:
     1. Giới thiệu Trang chủ, Bộ lọc, đổi Giao diện (*Nét vẽ nghệch ngoặc*, *Vàng Mộng Mơ*, *Hồng Ấm Áp*, Font/Cỡ chữ) & Đăng nhập Google/QR.
     2. Tủ sách cá nhân, Biên mục ISBN, Tiến độ đọc & Sổ tay trích dẫn.
     3. Luồng Mượn–Trả Kho Thư viện & P2P + Quét QR bàn giao + Hộ chiếu tình trạng sách.
     4. **Điểm nhấn:** Nạp Tiền Ảo miễn phí → Đăng ký/Chuyển gói 4 cấp (hiện lệ phí chuyển `15.000đ`) → **Quét QR Thanh Toán giống ngân hàng** (đồng bộ màn hình tức thì) → Hủy gói.
     5. **Điểm nhấn:** Trợ lý AI bền vững (đổi giao diện bằng câu lệnh, Reset Chat, gửi tóm tắt qua Email) + **Popup Thông Báo nhanh**.
     6. Quản trị Hệ thống (`S22`): Thống kê biểu đồ, Đổi biểu tượng toàn trang trực tiếp & Quản trị tài khoản.

#### 2. Tiêu chí hoàn thành (Definition of Done)
- [ ] Đóng góp bộ ảnh icon sạch, upload thành công lên web thật với **0 lỗi 404** và hiển thị hài hòa cả Light Mode & Dark Mode.
- [ ] Hoàn thành kiểm thử 100% nhóm `UI-01..07`, `AI-01..03`, `ADM-01..02` trên cả Desktop & Mobile.
- [ ] Bàn giao bộ ảnh chụp màn hình sắc nét cho Huyền và **Video Demo Full HD** hoàn chỉnh đúng kịch bản.

---

## QUY TRÌNH PHỐI HỢP & HẠN CHÓT CHUNG SPRINT 3

1. **Ngày 1–2:** Hưng deploy bản Sprint 5 mới nhất lên `https://bookshare.castle-tech.cloud` → cả nhóm bắt đầu thực thi song song theo từng Issue riêng.
2. **Ngày 3:** Huyền (Thống kê/Tài chính/Bảo mật), Huy (CRUD/Mượn trả), Lâm (Email/OTP/Thông báo), Kiệt (Icons/UI) hoàn tất lượt test 1 và log toàn bộ lỗi (nếu có) lên GitHub Issues theo mẫu chuẩn trong [`docs/TESTER_MANUAL_QA_CHECKLIST.md`](TESTER_MANUAL_QA_CHECKLIST.md).
3. **Ngày 4:** Hưng xử lý dứt điểm toàn bộ bug phát sinh & re-deploy → các thành viên Retest đóng Issue.
4. **Ngày 5:** Kiệt chốt Video Demo & bộ ảnh; Huyền hoàn thiện Báo cáo Cuối kỳ & Slide bảo vệ; Hưng chốt tag phát hành & diễn tập thuyết trình cả nhóm.
