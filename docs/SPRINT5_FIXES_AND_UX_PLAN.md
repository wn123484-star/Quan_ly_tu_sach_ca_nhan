# KẾ HOẠCH TRIỂN KHAI SPRINT 5 — SỬA LỖI PRODUCTION, ĐA GIAO DIỆN BOOTSTRAP, AI CHAT BỀN VỮNG & CHỐNG SPAM HỘI VIÊN

> **Phạm vi:** Xử lý triệt để toàn bộ lỗi phát hiện trên môi trường thật (`bookshare.castle-tech.cloud`) và nâng cấp trải nghiệm toàn diện:
> 1. Vá triệt để lỗi **404 ảnh biểu tượng tải lên** (`brand_logo_*.png`, `ai_fab_*.png`) & lưu trữ bền vững trong cả đĩa tĩnh lẫn SQL Server.
> 2. Vá triệt để lỗi **Đăng nhập Google OAuth 2.0** (`MÃ 400: Không nhận được thông tin định danh hợp lệ từ Google`) bằng External Cookie Scheme riêng biệt + DataProtection bền vững + làm sạch dấu ngoặc `.env`.
> 3. **Lịch sử Chat AI bền vững theo tài khoản & phân quyền** (tải lại trang không mất), bổ sung **nút Reset Chat**, điều khiển giao diện/font/cỡ chữ ngay trong chat và **gửi kết quả chat qua Email**.
> 4. **Hệ thống Giao diện Bootstrap đa phong cách** (Chuẩn Hiện đại, **Nét vẽ nghệch ngoặc Sketchy**, Phẳng Flatly, Kính mờ Morph, Đêm Cyborg) + **10 bảng màu** (**Vàng Mộng Mơ**, **Hồng Ấm Áp**, Tím Oải Hương, Giấy Cổ Thư Viện, Lục Bảo, Đại Dương, Sapphire, Cam Hoàng Hôn, Tím Hoàng Gia, Đêm Ngân Hà) + **5 Font chữ tải sẵn nội bộ** + **4 Cỡ chữ** + **Sáng/Tối** lưu bền vững trong Cài Đặt.
> 5. **Cài Đặt Tài Khoản cho mọi cấp**: phân tách rạch ròi **Tên hiển thị (`FullName`)**, **Tên đăng nhập (`UserName`)** và **Email nhận thư (`Email`)**; bắt buộc điền Email hợp lệ trước khi gửi bất kỳ thư nào.
> 6. **Popup Thông Báo nhanh** tại chuông header (xem nhanh danh sách chưa đọc, bấm mở chi tiết hoặc điều hướng thẳng tới trang nghiệp vụ tương ứng).
> 7. **Nâng cấp Luật Gói Hội Viên & Sổ Cái Chống Spam**: cấm mua trùng gói đang sở hữu, cơ chế **Chuyển Gói có lệ phí chuyển đổi minh bạch (`15.000đ` ảo) & khấu trừ ngày còn dư**, **nút Hủy Gói** an toàn, **IdempotencyKey khóa trùng CSDL**, chống spam hóa đơn `Pending` & chống spam nạp ví, minh bạch `BalanceBefore`/`BalanceAfter`.
> 8. **Tinh chỉnh thẩm mỹ**: cân bằng dải màu Header (không lệch bên tối bên sáng) và thu gọn/đẩy huy hiệu **chống bot reCAPTCHA v3** gọn về góc trái dưới, tuyệt đối không đè lên nút Trợ lý AI góc phải dưới.

---

## TIÊU CHÍ NGHIỆM THU (DEFINITION OF DONE)

1. Hoàn tất 3 tài liệu đặc tả Pha 0 (`SPRINT5_FIXES_AND_UX_PLAN.md`, `THEME_TYPOGRAPHY_AND_SETTINGS_SPEC.md`, `MEMBERSHIP_SWITCH_AND_ANTISPAM_SPEC.md`) trước khi chỉnh sửa mã nguồn.
2. Tải ảnh biểu tượng trong Quản trị (`S22`) -> ảnh trả về **HTTP 200**, hiển thị ngay trên Header/FAB và được lưu nhị phân trong SQL Server (`SystemIconSettings.CustomImageData`) tự khôi phục nếu thư mục đĩa bị xóa.
3. Google OAuth 2.0 dùng External Cookie Scheme riêng (`BookShare.External`), không còn bị `OnValidatePrincipal` hủy phiên trước khi vào `GoogleCallback`.
4. Mọi thư viện CSS Bootswatch & 5 bộ Font tiếng Việt (`Roboto`, `Be Vietnam Pro`, `Lora`, `Patrick Hand`, `JetBrains Mono`) được lưu nội bộ trong `wwwroot/`, **0 phụ thuộc CDN ngoài**.
5. Tải lại trang vẫn giữ nguyên lịch sử hội thoại AI của đúng tài khoản đang đăng nhập trên cả Widget nổi và trang `/S13GeminiAiAssistant`; có nút **Đặt lại cuộc trò chuyện (Reset Chat)** và **Gửi tóm tắt qua Email** (chặn gửi nếu chưa có Email hợp lệ).
6. Mọi vai trò (`User`, `Curator`, `Librarian`, `Admin`) chỉnh được **Tên hiển thị**, **Email nhận thư** và **Cài đặt Giao diện/Style/Font/Cỡ chữ** trong Cài Đặt; hệ thống chặn gửi thư nếu thiếu Email hợp lệ.
7. Chuông thông báo trên Header mở **Popup nhanh**, hỗ trợ đánh dấu đã đọc, xem chi tiết và đi thẳng tới trang đích.
8. Thành viên đang sở hữu gói trả phí không thể mua trùng gói đó (`HTTP 400`), có thể **Chuyển Gói** (tính lệ phí chuyển đổi `15.000đ` + hoàn trừ ngày dư minh bạch) hoặc **Hủy Gói** về `Free`; sổ cái giao dịch chống double-submit bằng `IdempotencyKey` (`UNIQUE INDEX`), chống spam nạp ví và chống đẻ rác hóa đơn `Pending`.
9. Header đổ màu đồng đều dịu mắt; huy hiệu reCAPTCHA v3 thu gọn tinh tế bên góc trái dưới, không đè nút AI FAB.
10. `dotnet build BookShare.sln --nologo` đạt **0 Error(s)**; `qa_full_bot.py --autostart` (L1–L9) đạt **100% PASS (0 FAIL)**; `smoke_test.py --autostart` đạt **100% PASS (0 lỗi)**.

---

## BẢNG CHECKLIST TIẾN ĐỘ TỪNG PHA

| Mã | Hạng mục | Trạng thái | Tệp mục tiêu chính | Cách kiểm chứng |
| :--- | :--- | :---: | :--- | :--- |
| **P0.1** | Lập kế hoạch tổng thể `docs/SPRINT5_FIXES_AND_UX_PLAN.md` | [x] | `docs/SPRINT5_FIXES_AND_UX_PLAN.md` | Hoàn tất trước khi code |
| **P0.2** | Lập đặc tả Giao diện, Font & Cài đặt `docs/THEME_TYPOGRAPHY_AND_SETTINGS_SPEC.md` | [x] | `docs/THEME_TYPOGRAPHY_AND_SETTINGS_SPEC.md` | Hoàn tất trước khi code |
| **P0.3** | Lập đặc tả Chuyển gói, Hủy gói & Chống spam `docs/MEMBERSHIP_SWITCH_AND_ANTISPAM_SPEC.md` | [x] | `docs/MEMBERSHIP_SWITCH_AND_ANTISPAM_SPEC.md` | Hoàn tất trước khi code |
| **P1.1** | Tải 4 bộ font tiếng Việt cục bộ (`Be Vietnam Pro`, `Lora`, `Patrick Hand`, `JetBrains Mono`) & gỡ `@import` CDN khỏi `bootswatch/*.min.css` | [x] | `wwwroot/fonts/*`, `wwwroot/vendor/bootswatch/*.min.css` | 0 URL `fonts.googleapis.com` trong CSS (`L9` PASS) |
| **P1.2** | Nâng cấp `01_BookShare_Schema.sql` + `DatabaseBootstrapService.cs` & chạy EF Scaffold `--schema dbo` | [x] | `01_BookShare_Schema.sql`, `DatabaseBootstrapService.cs`, `Models/Entities/*`, `BookShareDbContext.cs` | 0 cột lệch schema, build `0 Error(s)` |
| **P2.1** | Vá 404 Upload Icon (`UseStaticFiles` + lưu binary SQL + tự khôi phục + endpoint fallback) | [x] | `Program.cs`, `SystemIconService.cs`, `HomeController.cs` | Upload PNG -> GET `/uploads/icons/...` & `/assets/system-icons/...` đều `200 OK` |
| **P2.2** | Vá Google OAuth 2.0 (External Cookie Scheme riêng + DataProtection + strip quotes `.env`) | [x] | `Program.cs`, `AuthConfigHelper.cs`, `S01AuthOtpGoogleController.cs`, `RecaptchaService.cs`, `ResendEmailService.cs` | Tách `BookShare.External` không bị `OnValidatePrincipal` cắt phiên OAuth |
| **P3.1** | Nâng cấp `S16` & `S17`: cấm mua trùng, chuyển gói có lệ phí `15.000đ`, hủy gói, `IdempotencyKey`, chống spam & sổ cái minh bạch | [x] | `MembershipTierCatalog.cs`, `S16*`, `S17*`, `IS16ToS22Services.cs`, `S16ToS22ViewModels.cs` | L4, L8, L9 PASS 100% (bao gồm 429 rate-limit & 400 idempotency/duplicate) |
| **P4.1** | Nâng cấp `S01` Cài đặt mọi cấp (phân tách `FullName` vs `Email`, sửa Email, lưu `UiPreferencesJson`, chốt kiểm tra Email khi gửi thư) | [x] | `S01AuthOtpService.cs`, `S01AuthOtpGoogleController.cs`, `S01_AuthOtpGoogle/Index.cshtml`, `s01-auth-otp-google.js` | Mọi vai trò đổi Email/Tên hiển thị & lưu cấu hình UI (`L9` PASS) |
| **P4.2** | Nâng cấp `S18` Popup Thông báo nhanh (`RecentPopup` + modal chi tiết + điều hướng đích) | [x] | `S18NotificationHubController.cs`, `S18NotificationHubService.cs`, `_Layout.cshtml`, `core-api.js` | Bấm chuông mở popup -> bấm item đánh dấu đọc & xem chi tiết (`L9` PASS) |
| **P4.3** | Nâng cấp `S13` & Widget AI: lưu lịch sử theo phân quyền qua tải lại trang, nút **Reset Chat**, chọn style/font/cỡ chữ & gửi tóm tắt qua Email | [x] | `HomeController.cs`, `S13GeminiAiAssistantController.cs`, `S13GeminiAiService.cs`, `S13*`, `core-api.js` | Reload trang vẫn còn tin nhắn cũ, Reset xóa sạch, chặn gửi mail thiếu Email (`L9` PASS) |
| **P5.1** | Cân bằng đổ màu Header, thu gọn & dịch reCAPTCHA v3 sang góc trái dưới, tích hợp 5 Bootswatch Styles + 10 Palettes + 5 Fonts + 4 Font Sizes | [x] | `core-theme.css`, `_Layout.cshtml`, `core-api.js`, `S16*`, `S17*` | Header cân đối `180deg`, reCAPTCHA không đè AI FAB, Sketchy/Gold/Rose mượt |
| **P6.1** | Bổ sung Lớp 9 (Sprint 5) vào `scripts/qa_full_bot.py` và chạy `dotnet build` + `qa_full_bot.py` + `smoke_test.py` đến 100% PASS | [x] | `scripts/qa_full_bot.py` | 0 Error / **395/395 PASS (0 FAIL)** / Smoke 100% |
| **P6.2** | Đồng bộ toàn bộ tài liệu `.md` (`SPRINT5_*`, `QA_TEST_REPORT`, `PROGRESS`, `TODO`, `README`) | [x] | `docs/*.md`, `TODO.md`, `README.md` | Khớp thực tế 100% |

---

## NHẬT KÝ THAY ĐỔI (CHANGE LOG)

| Thời điểm | Pha | Kết quả thực hiện & Kiểm chứng |
| :--- | :--- | :--- |
| 2026-11-24 | Pha 0 | Khởi tạo bộ 3 tài liệu đặc tả Sprint 5 trước khi sửa mã nguồn. |
| 2026-11-24 | Pha 1–2 | Tải 6 tệp font tiếng Việt cục bộ (`Be Vietnam Pro`, `Lora`, `Patrick Hand`, `JetBrains Mono`), gỡ sạch `@import` CDN khỏi 4 tệp Bootswatch; nâng cấp CSDL (`SystemIconSettings.CustomImageData`, `AspNetUsers.UiPreferencesJson`, `Transactions.IdempotencyKey` + `SwitchFee` + `ProrationCredit` + `BalanceBefore/After`) & chạy EF Scaffold (`--schema dbo`); bổ sung `app.UseStaticFiles()` + fallback `/assets/system-icons/{iconKey}` vá triệt để 404 icon; tách `BookShare.External` Cookie Scheme + DataProtection + strip quotes `.env` vá triệt để Google OAuth 2.0. |
| 2026-11-24 | Pha 3–5 | Triển khai luật chống mua trùng gói, cơ chế Chuyển Gói có lệ phí chuyển `15.000đ` + khấu trừ ngày dư, nút Hủy Gói, khóa `IdempotencyKey` + chống spam nạp ví `429` + gộp hóa đơn `Pending`; xây dựng Cài đặt tài khoản mọi cấp phân tách `FullName`/`UserName`/`Email` + chốt kiểm tra Email khi gửi thư; xây dựng Popup Thông báo nhanh (`RecentPopup`), lịch sử Chat AI bền vững theo phân quyền + nút Reset Chat + gửi chat qua Email + thanh chọn nhanh Style/Màu/Font/Cỡ chữ; cân bằng Header `180deg`, 5 phong cách Bootstrap (gồm Sketchy nghệch ngoặc) + 10 bảng màu (gồm Vàng Mộng Mơ, Hồng Ấm Áp) và thu gọn reCAPTCHA về góc trái dưới. |
| 2026-11-24 | Pha 6 | Bổ sung 27 kiểm thử Lớp 9 vào `scripts/qa_full_bot.py` — phát hiện & vá ngay lỗi double-rollback che ngoại lệ `403 Forbidden` trong giao dịch `S17`. Xác minh cuối cùng: `dotnet build` **0 Error(s)**, `qa_full_bot.py` **395/395 PASS (0 FAIL)**, `smoke_test.py` **100% PASS (0 lỗi)**; đồng bộ toàn bộ tài liệu `.md`. |
