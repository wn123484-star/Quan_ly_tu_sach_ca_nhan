# KỊCH BẢN KIỂM THỬ TOÀN DIỆN DÀNH CHO TESTER — BOOKSHARE PORTAL

> **Mục tiêu tài liệu:** Hướng dẫn Tester kiểm tra đầy đủ toàn bộ chức năng, giao diện, bảo mật phân quyền, luồng nghiệp vụ mượn trả sách, Ví Tiền Ảo & QR Thanh Toán, Gói Hội Viên 4 Cấp, Trợ lý AI, Thông báo, Google OAuth 2.0 và Quản trị hệ thống trên **Web trình duyệt** & **Ứng dụng Android Web App**.  
> **Nguyên tắc sản phẩm bắt buộc kiểm tra xuyên suốt:** **100% chia sẻ & mượn trả sách giấy 0đ — TUYỆT ĐỐI KHÔNG bán sách**, mặc định mở lần đầu là **Light Mode (nền sáng)**, giao diện tiếng Việt chuyên nghiệp (không hiện mã kỹ thuật nội bộ như `S01`–`S22`, stack trace hay lỗi 404 ảnh).

---

## 1. THÔNG TIN MÔI TRƯỜNG & TÀI KHOẢN KIỂM THỬ

### 1.1. Địa chỉ truy cập & công cụ
- **Môi trường Production/Staging:** `https://bookshare.castle-tech.cloud` *(hoặc địa chỉ máy chủ local `http://localhost:5087` / `http://localhost:5198`)*
- **Trình duyệt khuyến nghị:** Chrome / Edge (mở sẵn **DevTools `F12` → Tab Console & Network** để bắt mọi lỗi đỏ JS hoặc HTTP `404`/`500`).
- **Kiểm tra đa thiết bị:** Desktop (`>= 1280px`), Mobile responsive (`390x844`), và **Android App (`BookShare-Mobile-Android.apk`)** nếu có.

### 1.2. Danh sách 6 tài khoản mẫu (4 vai trò phân quyền)
> *Lưu ý: Nếu môi trường đang là CSDL trắng chưa có sách mẫu, Tester đăng nhập **Admin** (`admin@bookshare.vn` / `Admin@123`) → vào **Quản Trị Hệ Thống** (`/S22AdminAnalytics`) → bấm **"Khôi Phục Bộ Dữ Liệu Cộng Đồng Chuẩn"** để nạp đầy đủ 6 tài khoản, 35 đầu sách và 55 bản sao.*

| STT | Vai trò (`Role`) | Email / Tên đăng nhập | Mật khẩu | Vai trò kiểm thử chính |
| :---: | :--- | :--- | :--- | :--- |
| 1 | **Admin** | `admin@bookshare.vn` *(hoặc gõ `admin`)* | `Admin@123` | Toàn quyền: Đổi icon toàn trang, xóa/khóa tài khoản, đổi vai trò, duyệt CLB, phân xử tranh chấp, reset CSDL. |
| 2 | **Librarian** *(Thủ thư)* | `thuthu@cmc.edu.vn` | `Librarian@123` | Duyệt/giao/thu hồi sách Kho thư viện, Quầy thủ thư quét mã vạch, nhập lô sách, tính phạt, hoàn cọc. |
| 3 | **Curator** *(Chủ nhiệm CLB)* | `clbsach@cmc.edu.vn` | `Curator@123` | Kiểm định sách tại Trạm CLB, điều phối giao nhận trung gian P2P. |
| 4 | **User 1** *(Độc giả Premium)* | `nguyenhung@cmc.edu.vn` | `User@123` | Tủ sách cá nhân, cho mượn P2P, tiến độ đọc, ghi chú, cài đặt giao diện & AI chat. |
| 5 | **User 2** *(Độc giả Premium)* | `minhanh@cmc.edu.vn` | `User@123` | Kiểm thử mượn–trả–đánh giá sạch, Ví Tiền Ảo, Chuyển/Hủy gói hội viên & QR Thanh Toán. |
| 6 | **User 3** *(Độc giả Free — Đang quá hạn)* | `hoangnam@cmc.edu.vn` | `User@123` | Có sẵn hợp đồng quá hạn (`-5 ngày`, phạt `25.000đ`) & tranh chấp mở để test luật chặn mượn. |

---

## 2. KIỂM THỬ GIAO DIỆN, ĐA PHONG CÁCH BOOTSTRAP, FONT CHỮ & HIỂN THỊ

| Mã TC | Hạng mục kiểm thử | Các bước thực hiện | Kết quả mong đợi | Trạng thái |
| :---: | :--- | :--- | :--- | :---: |
| **UI-01** | Mặc định Light Mode & không lỗi Console | Mở trang chủ `/` ở cửa sổ Ẩn danh (Incognito), mở `F12 → Console & Network`. | Trang mở nền sáng dịu mắt, splash screen nền sáng. **0 lỗi đỏ Console**, **0 tài nguyên 404**, **0 yêu cầu tới `fonts.googleapis.com` hay CDN ngoài**. | [ ] |
| **UI-02** | Đổ màu Header cân đối | Quan sát thanh Header từ trái sang phải trên Desktop (`>= 1200px`) qua các bảng màu khác nhau. | Menu nằm gọn **1 hàng** không bị rớt dòng lộn xộn; nền Header đổ màu dọc đều mắt, không bị lệch bên tối đen bên sáng chói. | [ ] |
| **UI-03** | Huy hiệu chống bot (reCAPTCHA v3) không đè AI Chatbot | Quan sát 2 góc dưới màn hình trên Desktop, Mobile và trang Đăng nhập `/S01AuthOtpGoogle`. | Nút **Trợ lý AI** nằm gọn ở **góc phải dưới**. Huy hiệu **reCAPTCHA** được thu nhỏ và ẩn gọn ở mép **góc trái dưới** (chỉ trượt ra khi di chuột vào), **tuyệt đối không đè lên nút Trợ lý AI** hay thanh Bottom Nav. | [ ] |
| **UI-04** | Đổi 5 Phong cách Bootstrap (đặc biệt **Nét vẽ nghệch ngoặc**) | Vào menu Chủ đề trên Header hoặc **Cài đặt tài khoản → tab Giao Diện & Font** (`/S01AuthOtpGoogle?tab=appearance`), lần lượt chọn: `Chuẩn`, `Nét Vẽ Nghệch Ngoặc (Sketchy)`, `Kính Mờ (Morph)`, `Phẳng (Flatly)`, `Đêm Công Nghệ (Cyborg)`. | Giao diện thay đổi tức thì theo đúng phong cách Bootstrap tương ứng. Khi chọn **Sketchy**, viền thẻ/nút mang nét vẽ tay nghệch ngoặc và tự chuyển sang font viết tay `Patrick Hand` hiển thị đủ dấu tiếng Việt. | [ ] |
| **UI-05** | Đổi 10 Bảng màu (Vàng Mộng Mơ, Hồng Ấm Áp...) & Dark Mode | Lần lượt chọn `Vàng Mộng Mơ`, `Hồng Ấm Áp`, `Tím Oải Hương`, `Giấy Cổ Thư Viện`, `Lục Bảo`... kết hợp bật/tắt nút **Tối/Sáng** trên Header. | Màu Header, điểm nhấn và nền trang đổi hài hòa, chữ dễ đọc không bị chìm màu. | [ ] |
| **UI-06** | Đổi 5 Phông chữ nội bộ & 4 Cỡ chữ + Lưu bền vững | Trong tab **Giao Diện & Font**, thử cả 5 font (`Roboto`, `Be Vietnam Pro`, `Lora`, `Patrick Hand`, `JetBrains Mono`) và 4 cỡ chữ (`Nhỏ`, `Tiêu chuẩn`, `Lớn`, `Rất lớn`) → bấm **Lưu Cài Đặt Giao Diện** → nhấn `F5` tải lại trang. | Cả 5 font hiển thị chuẩn dấu tiếng Việt, cỡ chữ phóng/thu mượt không vỡ layout; tải lại trang vẫn giữ nguyên cấu hình đã lưu (không nháy giao diện). | [ ] |
| **UI-07** | Không còn chữ kỹ thuật & 100% phi thương mại | Lướt nhanh các trang chính, tìm từ khóa `S01`–`S22`, `Ver07`, `CRUD`, `SQL`, `Mua ngay`, `Giá bán`. | Không thấy thuật ngữ kỹ thuật nội bộ và không có bất kỳ chức năng bán sách/giỏ hàng thương mại nào. | [ ] |

---

## 3. XÁC THỰC, GOOGLE OAUTH, QR LOGIN, CÀI ĐẶT MỌI CẤP & PHÂN QUYỀN

| Mã TC | Hạng mục kiểm thử | Các bước thực hiện | Kết quả mong đợi | Trạng thái |
| :---: | :--- | :--- | :--- | :---: |
| **AUTH-01** | Đăng nhập chuẩn & không lộ tài khoản mẫu | Mở `/S01AuthOtpGoogle` khi chưa đăng nhập. | Không hiện danh sách tài khoản mẫu/nút điền nhanh. Nhập sai mật khẩu báo lỗi rõ; nhập đúng tài khoản mẫu vào được hệ thống. | [ ] |
| **AUTH-02** | Đăng ký công khai & chống leo thang quyền | Mở tab **Đăng Ký**, tạo tài khoản mới mật khẩu `< 6` ký tự (kỳ vọng chặn), sau đó tạo tài khoản hợp lệ. | Tài khoản mới vào được trang chủ với vai trò **User** (`100` điểm uy tín), không thể truy cập `/S22AdminAnalytics` (bị chặn `403`). | [ ] |
| **AUTH-03** | Đăng nhập bằng **Google OAuth 2.0** | Trên môi trường thật đã điền `ClientId`/`ClientSecret`, bấm **"Tiếp tục nhanh bằng tài khoản Google"** → chọn tài khoản Gmail → cấp quyền. | Chuyển hướng mượt về BookShare và đăng nhập thành công vào **Tủ Sách Cá Nhân** (không còn lỗi `MÃ 400: Không nhận được thông tin định danh hợp lệ từ Google`). | [ ] |
| **AUTH-04** | Đăng nhập nhanh bằng **Mã QR 2 chiều** | Máy A (chưa đăng nhập) mở `/S01AuthOtpGoogle` → tab **Mã QR** (kiểm tra mã QR hiện rõ, không nháy trắng sau 1 giây). Máy B (đã đăng nhập) quét mã đó và bấm **Phê duyệt**. | Máy A tự động đăng nhập thành công trong 2–3 giây; quét lại mã cũ bị từ chối. | [ ] |
| **AUTH-05** | Cài đặt tài khoản mọi cấp: Phân tách **Tên hiển thị** vs **Email** | Đăng nhập từng cấp (`User`, `Curator`, `Librarian`, `Admin`) → vào **Hồ Sơ, Email & Bảo Mật** (`/S01AuthOtpGoogle`). Thử nhập một địa chỉ email vào ô **Tên hiển thị** rồi bấm Lưu. | Hệ thống **chặn (`400`)** yêu cầu Tên hiển thị phải tách biệt với Email. Ô **Tên đăng nhập** hiển thị riêng (chỉ đọc). Nhập Tên hiển thị bình thường + đổi **Email nhận thư** sang email thật của Tester → Lưu thành công, Header đổi ngay Tên hiển thị mà không mất Tên đăng nhập cũ. | [ ] |
| **AUTH-06** | Chốt bắt buộc có Email hợp lệ khi gửi thư | Xóa trống ô Email trong Cài Đặt → Lưu → thử bấm **Gửi qua Email** trong Chat AI hoặc **Gửi OTP** tại Thông Báo. | Hệ thống **chặn gửi**, báo rõ tài khoản chưa có Email hợp lệ và hiện nút dẫn thẳng về **Cài Đặt Tài Khoản** để điền Email. Sau khi điền Email hợp lệ → gửi thư thành công! | [ ] |
| **AUTH-07** | Đổi mật khẩu & kiểm tra bảo mật | Đổi mật khẩu tài khoản test → đăng xuất → đăng nhập lại bằng mật khẩu cũ. | Mật khẩu cũ bị từ chối; mật khẩu mới đăng nhập thành công. | [ ] |
| **AUTH-08** | Kiểm thử phân quyền RBAC & Chống IDOR | Đăng nhập `User 1`, thử truy cập trực tiếp `/S21LibrarianDesk`, `/S22AdminAnalytics` hoặc cố sửa/xóa kệ sách, ghi chú, thông báo của `User 2`. | Giao diện hiển thị trang từ chối quyền thân thiện hoặc ẩn dữ liệu quản trị; mọi thao tác API trái phép bị chặn `403`. | [ ] |

---

## 4. TỦ SÁCH CÁ NHÂN, BIÊN MỤC ISBN, TÌM KIẾM & KHÔNG GIAN ĐỌC (`S02`–`S07`)

| Mã TC | Hạng mục kiểm thử | Các bước thực hiện | Kết quả mong đợi | Trạng thái |
| :---: | :--- | :--- | :--- | :---: |
| **BOOK-01** | Trang chủ & Bộ lọc đa tiêu chí (`/`, `/S06SearchDiscovery`) | Tìm kiếm theo tên sách, tác giả, ISBN; lọc theo nguồn `Kho chung Thư viện` / `Tủ sách P2P Cộng đồng`, thể loại, trạng thái sẵn có; thử đổi chế độ hiển thị lưới/danh sách. | Trả đúng kết quả, ảnh bìa hiển thị đầy đủ (nếu ảnh lỗi tự chuyển sang ảnh bìa dự phòng SVG đẹp, không vỡ ảnh). | [ ] |
| **BOOK-02** | Quét mã ISBN & Biên mục sách (`/S05IsbnAutoCatalog`) | Đăng nhập `User 1`, tra cứu ISBN mẫu (VD: `9786048045562`) → lưu vào Tủ sách cá nhân. | Tự điền thông tin sách, cấp mã vạch `BS-...`, sách xuất hiện trong **Tủ Sách Cá Nhân** (`/S02PersonalShelf`), không bị trộn vào Kho Thư viện (`S21`). | [ ] |
| **BOOK-03** | Tủ sách cá nhân & Bật/Tắt cho mượn P2P (`/S02PersonalShelf`) | Tạo kệ mới, sửa tên kệ, tắt công tắc **"Cho phép mượn P2P"** trên 1 cuốn sách → sang tài khoản `User 2` thử mượn cuốn đó → bật lại. | Khi tắt cho mượn, `User 2` không thể tạo phiếu mượn; khi bật lại thì mượn bình thường. | [ ] |
| **BOOK-04** | Tiến độ đọc sách & Sổ tay trích dẫn (`/S03ReadingTracker`, `/S04BookNotesQuotes`) | Thêm sách đang đọc, cập nhật số trang hiện tại (thử nhập số trang > tổng trang để kiểm tra validate), tạo ghi chú trích dẫn theo số trang, sửa và xóa ghi chú. | Thanh phần trăm tiến độ cập nhật chuẩn, biểu đồ hiển thị đẹp ở cả Light & Dark Mode; ghi chú lưu/sửa/xóa đúng chủ sở hữu. | [ ] |
| **BOOK-05** | Kho Thư viện (`/S07LibraryCatalog`) | Đăng nhập `Librarian`, kiểm tra danh sách bản sao Kho chung và cập nhật vị trí kệ sách. | Lưu vị trí kệ thành công; tài khoản `User` thường chỉ xem được, không thấy nút sửa kho. | [ ] |

---

## 5. LUỒNG MƯỢN TRẢ THƯ VIỆN, P2P, QR BÀN GIAO, HÀNG CHỜ, GIA HẠN, TRANH CHẤP & QUẦY THỦ THƯ

| Mã TC | Hạng mục kiểm thử | Các bước thực hiện | Kết quả mong đợi | Trạng thái |
| :---: | :--- | :--- | :--- | :---: |
| **LOAN-01** | Mượn & trả sách Kho Thư viện (`/S08LibraryLoanFlow`) | `User 2` gửi yêu cầu mượn 1 cuốn kho trường → `Librarian` duyệt → xác nhận bàn giao (hoặc dùng **QR Bàn giao**) → xác nhận hoàn trả. | Trạng thái bản sao chuyển đúng chu trình `Available → Reserved → Borrowed → Available`; hạn trả tuân thủ tối đa theo gói hội viên. | [ ] |
| **LOAN-02** | Mượn cộng đồng P2P & chống duyệt trùng (`/S09P2PLoanFlow`) | `User 2` và 1 tài khoản khác cùng gửi yêu cầu mượn 1 cuốn của `User 1`. `User 1` duyệt cho `User 2`, sau đó thử bấm duyệt thêm cho người kia. | Hệ thống **chặn duyệt trùng** (`400`) vì sách đã giữ chỗ cho `User 2`. Qua `Curator` tại `/S20ExchangeHubClub` hoặc chủ sách chuyển trạng thái `Đang giao → Đã nhận → Đã trả`. | [ ] |
| **LOAN-03** | Hộ chiếu tình trạng sách (`/S10ConditionReport`) | Trong lúc `User 2` đang giữ sách, tải ảnh chụp tình trạng sách lúc nhận (`.png`/`.jpg`) → kiểm tra ảnh hiển thị. | Ảnh upload thành công, hiển thị rõ (không bị 404); người ngoài hợp đồng không thể tải ảnh vào phiếu của người khác (`403`). | [ ] |
| **LOAN-04** | Quét QR bàn giao & hoàn trả sách 1 lần dùng | Chủ sách/Thủ thư bấm **Hiện mã QR bàn giao** → Người mượn dùng camera quét (hoặc nhập mã dự phòng 6 ký tự). Thử quét lại lần 2. | Lần 1 xác nhận thành công, chuyển trạng thái ngay; lần 2 bị từ chối vì mã đã tiêu thụ. | [ ] |
| **LOAN-05** | Đặt trước Sách Hot & ưu tiên hạng hội viên (`/S11HotBookQueue`) | Đặt trước 1 cuốn đang hết bản sao sẵn có; kiểm tra thứ tự hàng chờ (Scholar > Premium > Standard > Free). Khi sách được trả, kiểm tra hệ thống đôn hàng chờ & gửi thông báo. | Xếp đúng thứ tự ưu tiên theo gói; người đầu hàng chờ nhận thông báo giữ chỗ 48h. | [ ] |
| **LOAN-06** | Gia hạn, Phí trễ & Luật chặn nợ quá hạn (`/S12LoanRenewalFine`, `/S21LibrarianDesk`) | Đăng nhập `User 3` (đang có phiếu quá hạn & tranh chấp) thử mượn sách mới → kiểm tra bị chặn. `User 2` bấm **Gia hạn** trên phiếu đang mượn hợp lệ. | `User 3` bị chặn mượn kèm lý do rõ ràng; `User 2` gia hạn thành công đúng số ngày & số lần tối đa của gói hội viên. | [ ] |
| **LOAN-07** | Khiếu nại & Tranh chấp (`/S19DisputeResolution`) | Trong 1 hợp đồng đang mở, báo tranh chấp sách hư hại → kiểm tra người mượn bị khóa mượn mới → `Admin` phân xử `Resolved` kèm mức bồi thường `25.000đ` → người mượn thanh toán hóa đơn bồi thường tại `/S17SandboxPayment`. | Trước khi trả bồi thường vẫn khóa mượn; sau khi thanh toán hóa đơn bồi thường xong thì tự động mở khóa quyền mượn. | [ ] |
| **LOAN-08** | Điểm uy tín & Đánh giá 2 chiều (`/S15TrustScoreReview`) | Sau khi trả sách, `User 2` đánh giá sách/chủ sách (`1`–`5` sao). Thử tự đánh giá bản thân hoặc đánh giá trùng 2 lần trên cùng hợp đồng. | Đánh giá hợp lệ cộng điểm uy tín; tự đánh giá hoặc đánh giá trùng bị chặn (`400`). | [ ] |
| **LOAN-09** | Đề nghị nâng quyền Chủ nhiệm CLB (`S01 → S22`) | Tài khoản `User` mở `/S01AuthOtpGoogle` gửi đề nghị trở thành **Chủ nhiệm CLB** → `Admin` vào `/S22AdminAnalytics` phê duyệt. | Không cho gửi trùng khi đang `Pending`; sau khi Admin duyệt, tài khoản đó đăng nhập lại có vai trò `Curator` và thấy menu **Điều Hành → Trạm CLB**. | [ ] |

---

## 6. VÍ TIỀN ẢO MIỄN PHÍ, GÓI HỘI VIÊN 4 CẤP, QR THANH TOÁN & CHỐNG SPAM (`S16`, `S17`)

> **Trọng tâm kiểm thử kỹ:** Kiểm tra tính minh bạch 1 lần, không ngân hàng thật, không trừ/cộng đúp, không cho mua trùng gói đang có, tính đúng lệ phí chuyển gói `15.000đ` và nút hủy gói.

| Mã TC | Hạng mục kiểm thử | Các bước thực hiện | Kết quả mong đợi | Trạng thái |
| :---: | :--- | :--- | :--- | :---: |
| **PAY-01** | Nạp Tiền Ảo miễn phí & biến động sổ cái (`/S17SandboxPayment`) | Đăng nhập `User 2` (hoặc tài khoản mới) → vào **Ví Tiền Ảo Miễn Phí** → chọn nhanh `+200.000đ` → bấm **Nạp Tiền Ảo Miễn Phí Ngay**. Thử nạp `< 1.000đ`. | Nạp `< 1.000đ` bị chặn (`400`). Nạp `+200.000đ` thành công ngay (`0đ` tiền thật), số dư Ví tăng đúng `+200.000đ`, bảng lịch sử hiện rõ biên lai `#TX-` kèm dòng **`Ví: Trước → Sau`**. | [ ] |
| **PAY-02** | Chống spam Nạp Tiền Ảo (Rate Limit + Double-Click) | Bấm nạp tiền ảo liên tục **6 lần trong vòng 1 phút** trên cùng 1 tài khoản. | 5 lần đầu thành công (`200`); đến lần thứ 6 hệ thống chặn với cảnh báo chống spam (`HTTP 429`), không cộng tiền thêm. Khi đang gọi API, nút nạp bị khóa kèm spinner chống bấm đúp. | [ ] |
| **PAY-03** | Xem bảng giá 4 Cấp & Đăng ký gói mới từ `Free` (`/S16MembershipTier`) | Dùng tài khoản đang ở gói `Free` → chuyển đổi kỳ hạn `1 Tháng / 6 Tháng / 12 Tháng` → kiểm tra giá: `Standard` (`29k/149k/249k`), `Premium` (`49k/249k/399k`), `Scholar` (`89k/449k/699k`). Đăng ký `Standard 1 Tháng` bằng **Ví Tiền Ảo**. | Hiện hộp thoại báo giá minh bạch `29.000đ` (không phí chuyển vì đăng ký mới từ Free). Xác nhận → trừ đúng `29.000đ`, hạng thẻ đổi ngay sang `Standard` (**3 cuốn / 21 ngày / 2 lần gia hạn**). | [ ] |
| **PAY-04** | **Chống mua trùng gói đang sở hữu** | Ngay khi đang sở hữu gói `Standard` còn hạn, nhìn vào thẻ `Standard` trên `/S16MembershipTier`. | Thẻ `Standard` hiển thị badge **"Đang sử dụng"**, nút mua bị khóa **"Đang Sở Hữu (Không Mua Trùng)"** và hiện **Nút Hủy Gói Hội Viên Này**. Không thể mua chồng gói `Standard` lần 2. | [ ] |
| **PAY-05** | **Chuyển Gói có lệ phí chuyển đổi (`15.000đ`) & khấu trừ ngày dư** | Từ gói `Standard` đang có, bấm **"Chuyển Sang Gói Này"** ở thẻ `Premium` hoặc `Scholar` (6 tháng). | Hiện hộp thoại báo giá minh bạch gồm 4 dòng rõ ràng: **Giá gói mới**, **Hoàn trừ ngày dư gói cũ (`-...đ`)**, **Lệ phí chuyển gói (`+15.000đ ảo`)**, và **Tổng thanh toán cuối cùng**. Xác nhận trừ đúng số tiền đó, gói cũ bị tắt và kích hoạt duy nhất gói mới. | [ ] |
| **PAY-06** | **Quét QR Thanh Toán giống ngân hàng (Đồng bộ Web ↔ Mobile/Web App)** | Trên máy tính (hoặc tab 1), bấm **"Tạo Mã QR Chuyển Gói / Thanh Toán"** → màn hình hiện Mã QR `#TX-...` và trạng thái *"Đang chờ quét..."*. Trên điện thoại / Android Web App (hoặc nút **Quét QR Thanh Toán** trên header), bấm mở camera quét mã QR đó (hoặc bấm **Chọn ảnh** / nhập mã `BOOKSHARE-PAY:{id}`). | Sau khi quét, hiện **Hóa Đơn Thanh Toán Tiền Ảo** chi tiết (mã HĐ, nội dung, phí chuyển nếu có, số tiền, số dư Ví hiện tại, số dư dự kiến sau thanh toán). Bấm **"Xác Nhận Thanh Toán"** → trừ ví 1 lần duy nhất, kích hoạt gói ngay, và màn hình QR bên tab 1 **tự động cập nhật "Đã thanh toán thành công!"** trong 2–3 giây. Quét lại mã QR cũ bị từ chối. | [ ] |
| **PAY-07** | Thanh toán khi thiếu tiền trong Ví | Quét hoặc xác nhận 1 hóa đơn lớn hơn số dư Ví hiện tại. | Hộp thoại báo rõ **còn thiếu bao nhiêu tiền ảo** và đổi nút chính thành **"Đi Nạp Tiền Ảo Miễn Phí"**; không trừ âm số dư Ví. | [ ] |
| **PAY-08** | **Chống spam tạo hóa đơn `Pending` hàng loạt** | Vào `/S17SandboxPayment`, bấm **"Tạo Hóa Đơn & Hiện Mã QR"** 2 lần liên tiếp với cùng mục đích & số tiền. Sau đó tạo thử > 3 hóa đơn chờ khác nhau. | Lần bấm thứ 2 **tái sử dụng đúng mã `#TX-` đang chờ** (không đẻ rác hóa đơn trùng). Nếu đang có 3 hóa đơn `Pending`, hệ thống chặn tạo thêm và yêu cầu thanh toán hoặc hủy bớt hóa đơn cũ. | [ ] |
| **PAY-09** | **Nút Hủy Gói Hội Viên (`CancelMembership`)** | Đang ở gói `Standard`/`Premium`/`Scholar`, bấm **"Hủy Gói"** → xác nhận. *(Thử thêm trường hợp đang mượn > 2 cuốn sách rồi bấm Hủy gói).* | Nếu đang mượn vượt hạn mức `Free` (`2 + thưởng P2P`) → hệ thống **chặn hủy** và báo rõ số sách cần trả bớt. Nếu hợp lệ → hạ về `Free`, tự hủy các hóa đơn chờ cũ và ghi 1 biên lai `Hủy gói hội viên (0đ)` minh bạch trong lịch sử. | [ ] |

---

## 7. TRỢ LÝ AI BỀN VỮNG THEO PHÂN QUYỀN, RESET CHAT & GỬI EMAIL (`S13`, `S14` & WIDGET)

| Mã TC | Hạng mục kiểm thử | Các bước thực hiện | Kết quả mong đợi | Trạng thái |
| :---: | :--- | :--- | :--- | :---: |
| **AI-01** | **Lưu lịch sử Chat AI bền vững qua tải lại trang** | Đăng nhập `User 1`, mở Widget Trợ lý AI ở góc phải dưới, nhắn 1 câu (VD: *"Gợi ý sách công nghệ sẵn có"*). Sau đó nhấn **`F5` tải lại trang** hoặc chuyển sang trang khác rồi mở lại Widget AI. | Toàn bộ tin nhắn cũ vẫn được giữ nguyên (tải tự động từ CSDL). Đăng xuất và đăng nhập `User 2` → **không thấy** lịch sử chat của `User 1` (cô lập tuyệt đối theo tài khoản/phân quyền). | [ ] |
| **AI-02** | **Nút Reset Chat (Đặt lại cuộc trò chuyện)** | Bấm biểu tượng **Reset Chat** (`↻`) trên thanh tiêu đề Widget AI (và thử trên trang toàn màn hình `/S13GeminiAiAssistant`) → Xác nhận. | Tin nhắn cũ được xóa sạch, thay bằng lời chào mới phù hợp đúng vai trò đang đăng nhập (`Độc giả` / `Thủ thư` / `Chủ nhiệm CLB` / `Quản trị viên`). Tải lại trang vẫn sạch như mới. | [ ] |
| **AI-03** | Chỉnh Style / Màu / Font / Cỡ chữ ngay trong Chat AI | Bấm biểu tượng **Bảng màu** trên thanh tiêu đề Widget AI (hoặc chọn trên thanh công cụ `/S13GeminiAiAssistant`), và thử gõ lệnh chat: *"Đổi giao diện sang phong cách nét vẽ nghệch ngoặc màu hồng ấm áp"*. | Giao diện đổi tức thì sang phong cách **Sketchy** + bảng màu **Hồng Ấm Áp** + font viết tay và tự đồng bộ lưu vào cài đặt. | [ ] |
| **AI-04** | Gửi tóm tắt hội thoại AI qua Email | Bấm biểu tượng **Phong bì (Gửi qua Email)** trên Widget AI hoặc trang `/S13GeminiAiAssistant`. | Nếu tài khoản đã có Email hợp lệ → gửi thành công và ghi log trong Thông báo. Nếu chưa có Email → hiện cảnh báo yêu cầu điền Email trong Cài Đặt trước. | [ ] |
| **AI-05** | Gợi ý sách AI cá nhân hóa (`/S14AiRecommendation`) | Mở `/S14AiRecommendation`, chọn tâm trạng/chuyên ngành đọc và bấm tạo gợi ý. | Hiển thị danh sách sách gợi ý kèm tỷ lệ phù hợp và nút chuyển sang mượn sách. | [ ] |

---

## 8. POPUP THÔNG BÁO NHANH & TRUNG TÂM THÔNG BÁO (`S18`)

| Mã TC | Hạng mục kiểm thử | Các bước thực hiện | Kết quả mong đợi | Trạng thái |
| :---: | :--- | :--- | :--- | :---: |
| **NOTIF-01** | **Popup Thông Báo nhanh khi bấm Chuông Header** | Khi đã đăng nhập, bấm vào **biểu tượng Chuông** (có chấm đỏ số lượng chưa đọc) trên thanh Header. | Mở ngay **Popup nhanh** hiển thị danh sách thông báo mới nhất, nút **"Đọc tất cả"** và nút **"Xem tất cả →"**. | [ ] |
| **NOTIF-02** | Xem chi tiết & chuyển trang đích từ Popup | Bấm vào 1 thông báo chưa đọc (hoặc nút **"Xem nhanh"** / **"Mở trang chi tiết"**) trong Popup. | Thông báo tự động đánh dấu **đã đọc** (chấm đỏ giảm đi 1), hiện hộp thoại xem đầy đủ nội dung kèm nút **"Đi tới trang chi tiết →"** dẫn đúng trang liên quan (Phiếu mượn, Gói hội viên, Ví Tiền Ảo, Tranh chấp hoặc làm nổi bật đúng thẻ trên `/S18NotificationHub`). | [ ] |
| **NOTIF-03** | Xác minh Email bằng mã OTP 6 số (`/S18NotificationHub`) | Nhập Email hợp lệ → bấm **Gửi OTP** → nhập 6 chữ số OTP nhận được trong hộp thư (hoặc banner chế độ dev khi chưa gắn Resend key) → bấm **Xác Minh OTP**. | Xác minh thành công, trạng thái Email đổi sang **"Đã xác minh OTP"**. | [ ] |

---

## 9. QUẢN TRỊ HỆ THỐNG: ĐỔI ICON (0 LỖI 404) & XÓA TÀI KHOẢN (`S22`)

| Mã TC | Hạng mục kiểm thử | Các bước thực hiện | Kết quả mong đợi | Trạng thái |
| :---: | :--- | :--- | :--- | :---: |
| **ADM-01** | **Upload & Đổi Biểu Tượng Toàn Trang (Logo, AI FAB...) — 0 lỗi 404** | Đăng nhập `Admin` → `/S22AdminAnalytics` → tab **Tùy Chỉnh Biểu Tượng** → chọn tệp ảnh `.png` / `.webp` / `.svg` sạch tải lên cho `Logo chính BookShare` và `Trợ Lý AI Thủ Thư` → nhấn **`F5`** và mở `F12 → Console/Network`. | Ảnh biểu tượng **hiển thị ngay lập tức** trên Logo góc trái Header & nút Trợ lý AI góc phải dưới, Network trả về **`200 OK`** (**tuyệt đối không còn 404**). Thử gạt công tắc sang trái (Mặc định) / phải (Ảnh tùy chỉnh) và bấm **Khôi phục mặc định** hoạt động tức thì. | [ ] |
| **ADM-02** | Chặn tệp biểu tượng độc hại | Thử upload tệp `.exe` hoặc tệp `.svg` chứa thẻ `<script>` vào mục Biểu tượng. | Hệ thống từ chối (`400`), không lưu tệp lên máy chủ. | [ ] |
| **ADM-03** | **Admin Xóa Tài Khoản an toàn** | 1) Thử xóa chính tài khoản `admin@bookshare.vn`.<br>2) Thử xóa tài khoản đang giữ sách chưa trả.<br>3) Tạo 1 tài khoản mới sạch (hoặc sau khi trả hết sách) → bấm nút đỏ **Xóa TK** → Xác nhận. | 1) Không hiện nút xóa / chặn `400` với Admin gốc.<br>2) Chặn `400` nếu thành viên đang giữ sách hoặc có tranh chấp mở.<br>3) Xóa sạch tài khoản hợp lệ (`200`), không lỗi khóa ngoại; nếu tài khoản đó đang mở trình duyệt khác, tải lại trang lập tức bị đăng xuất sạch (`0 lỗi 500`). | [ ] |
| **ADM-04** | Khóa/Mở khóa tài khoản & Đổi vai trò | Khóa 1 tài khoản thường → thử đăng nhập bằng tài khoản đó → mở khóa lại. | Khi bị khóa, đăng nhập bị từ chối với thông báo tài khoản đang bị tạm khóa; mở khóa xong đăng nhập bình thường. | [ ] |

---

## 10. XỬ LÝ NGOẠI LỆ, TRANG BÁO LỖI THÂN THIỆN & MẪU BÁO CÁO LỖI

| Mã TC | Hạng mục kiểm thử | Các bước thực hiện | Kết quả mong đợi | Trạng thái |
| :---: | :--- | :--- | :--- | :---: |
| **ERR-01** | Trang lỗi 404 thông minh (sai đường dẫn) | Gõ trực tiếp vào thanh địa chỉ `/S02Pers` và `/duong-dan-sai-bat-ky`. | Hiển thị trang lỗi **404 BookShare nền sáng thân thiện** (không hiện trang trắng/đen mặc định trình duyệt, không lộ stack trace tiếng Anh). Với `/S02Pers`, hiện thêm nút gợi ý đi thẳng tới **Tủ Sách Cá Nhân (`/S02PersonalShelf`)**. | [ ] |
| **ERR-02** | Không trả HTML lỗi cho API/JSON | Truy cập `/api/khong-ton-tai` với header `Accept: application/json`. | Trả về JSON lỗi `404` gọn gàng có mã tham chiếu hỗ trợ (`requestId`). | [ ] |

---

### 📋 MẪU BÁO CÁO LỖI CHO TESTER (Copy & gửi lại Dev khi phát hiện lỗi)

```markdown
- **Mã Test Case:** (VD: PAY-06 / UI-04 / ADM-01)
- **Mức độ nghiêm trọng:** [ ] Khẩn cấp (Blocker)  [ ] Cao (Major)  [ ] Trung bình  [ ] Nhỏ (UI)
- **Môi trường & Tài khoản:** (VD: Chrome Windows / Android App — tài khoản minhanh@cmc.edu.vn)
- **URL xảy ra lỗi:** 
- **Các bước tái hiện:**
  1. ...
  2. ...
- **Kết quả thực tế:** (Kèm ảnh chụp màn hình + ảnh tab F12 Console/Network nếu có)
- **Kết quả mong đợi:**
```
