# KẾ HOẠCH TRIỂN KHAI SPRINT 4 — VÍ TIỀN ẢO, GÓI HỘI VIÊN 4 CẤP, QUÉT QR THANH TOÁN & ADMIN XÓA TÀI KHOẢN

> **Phạm vi:** Gỡ bỏ hoàn toàn cấu hình cổng ngân hàng thật (`VNPay__*`) khỏi `.env.example` & hệ thống; xây dựng **Ví Tiền Ảo miễn phí 100%** (`dbo.UserWallets`), nâng cấp **Hệ thống Gói Hội Viên 4 Cấp** (`Free`, `Standard`, `Premium`, `Scholar`) phân tách rõ giá tiền ảo, phạm vi loại sách được mượn, giới hạn mượn đồng thời, thời hạn mượn & độ ưu tiên hàng chờ; xây dựng luồng **Quét QR Thanh Toán giống ngân hàng** (bấm nút -> mở camera quét -> xem hóa đơn chi tiết -> bấm xác nhận thanh toán) đồng bộ real-time giữa Web và Android Web App; bổ sung tính năng **Admin Xóa Tài Khoản** an toàn toàn vẹn khóa ngoại tại `/S22AdminAnalytics`.  
> **Nguyên tắc bắt buộc:** Strict Service Layer (`Controller -> IService -> Service -> BookShareDbContext`), Database-First Scaffold (`--schema dbo`, 0 lệch schema với `01_BookShare_Schema.sql`), 100% phi thương mại không bán sách, Light Mode mặc định (`#f8fafc`), 0 CDN ngoài, 0 từ ngữ kỹ thuật trên UI.

---

## TIÊU CHÍ NGHIỆM THU (DEFINITION OF DONE)

1. Hoàn tất 3 tệp tài liệu Pha 0 (`SPRINT4_WALLET_MEMBERSHIP_PLAN.md`, `VIRTUAL_WALLET_AND_QR_PAY_SPEC.md`, `ADMIN_USER_DELETE_SPEC.md`) trước khi sửa mã nguồn và cập nhật tiến độ `[x]` sau mỗi pha.
2. Loại bỏ toàn bộ biến môi trường ngân hàng thật (`VNPay__TmnCode`, `VNPay__HashSecret`, `VNPay__BaseUrl`, `VNPay__ReturnUrl`) khỏi `.env.example` và không gọi bất kỳ cổng thanh toán ngân hàng thật nào.
3. CSDL `01_BookShare_Schema.sql` + `DatabaseBootstrapService` đồng bộ bảng `dbo.UserWallets`, mở rộng 4 cấp `Memberships.Tier` (`Free`/`Standard`/`Premium`/`Scholar`) và cột hóa đơn `Transactions` (`TargetTier`, `DurationMonths`, `Description`), scaffold sạch `--schema dbo` không lệch cột Docker.
4. Thành viên có thể nạp tiền ảo miễn phí thoải mái trên `/S17SandboxPayment`, đăng ký/nâng cấp gói 4 cấp trên `/S16MembershipTier` bằng số dư Ví Tiền Ảo hoặc tạo mã QR hóa đơn; luật mượn theo từng cấp được thực thi thật ở Backend (`MaxConcurrentLoans`, `MaxBorrowDays`, phạm vi sách, ưu tiên hàng chờ `S11`, giới hạn gia hạn `S12`).
5. Nút **Quét QR Thanh Toán** hoạt động đồng bộ trên Web & Web App (kể cả nút QR trung tâm Bottom Nav): mở camera quét -> gọi `PreviewQrInvoice` hiển thị hóa đơn chi tiết & so sánh số dư Ví -> bấm **Xác Nhận Thanh Toán** -> gọi `ConfirmQrInvoice` trừ tiền ảo atomic, chống replay/IDOR và cập nhật real-time về màn hình hiển thị QR qua polling `InvoiceStatus`.
6. Admin tại `/S22AdminAnalytics` có nút **Xóa TK** được bảo vệ `[Authorize(Roles="Admin")]` + `[ValidateAntiForgeryToken]`: chặn tự xóa, chặn xóa `u-admin-01`, chặn xóa tài khoản đang có sách chưa trả hoặc tranh chấp mở, và xóa sạch nguyên tử tài khoản hợp lệ.
7. `dotnet build BookShare.sln --nologo` đạt **0 Error(s)**, `python scripts/qa_full_bot.py --autostart` (L1–L8) đạt **100% PASS (0 FAIL)**, `python scripts/smoke_test.py --autostart` đạt **100% PASS (0 lỗi)**.

---

## BẢNG CHECKLIST TỪNG PHA

| Mã | Hạng mục | Trạng thái | Tệp mục tiêu chính | Cách kiểm chứng |
| :--- | :--- | :---: | :--- | :--- |
| **P0.1** | Lập kế hoạch & bảng theo dõi `docs/SPRINT4_WALLET_MEMBERSHIP_PLAN.md` | [x] | `docs/SPRINT4_WALLET_MEMBERSHIP_PLAN.md` | Tạo xong trước khi code |
| **P0.2** | Lập đặc tả Ví Tiền Ảo, 4 Cấp Hội Viên & QR Thanh Toán `docs/VIRTUAL_WALLET_AND_QR_PAY_SPEC.md` | [x] | `docs/VIRTUAL_WALLET_AND_QR_PAY_SPEC.md` | Đầy đủ bảng giá, hạn mức & luồng QR |
| **P0.3** | Lập đặc tả Admin Xóa Tài Khoản `docs/ADMIN_USER_DELETE_SPEC.md` | [x] | `docs/ADMIN_USER_DELETE_SPEC.md` | Đầy đủ luật chặn & thứ tự xóa FK |
| **P1.1** | Mở rộng `01_BookShare_Schema.sql` (`UserWallets`, 4 cấp `Memberships`, cột hóa đơn `Transactions`) & `DatabaseBootstrapService` | [x] | `database/01_BookShare_Schema.sql`, `DatabaseBootstrapService.cs` | Dựng sạch LocalDB 0 lỗi |
| **P1.2** | Chạy `dotnet ef dbcontext scaffold --schema dbo --no-onconfiguring --force` & dọn `.env.example` bỏ biến VNPay thật | [x] | `Models/Entities/*`, `BookShareDbContext.cs`, `.env.example` | 0 cột lệch schema, 0 `VNPay__` trong `.env.example` |
| **P2.1** | Xây dựng `MembershipTierCatalog` (4 cấp `Free`/`Standard`/`Premium`/`Scholar`) & thực thi luật thật tại `LoanFlowCoordinatorService`, `S08`, `S09`, `S11`, `S12` | [x] | `MembershipTierCatalog.cs`, `LoanFlowCoordinatorService.cs`, `S08`, `S09`, `S11`, `S12` | L8 kiểm chứng hạn mức & luật từng cấp |
| **P2.2** | Nâng cấp `S16MembershipTierService` & `S17SandboxPaymentService` + Controllers: Nạp tiền ảo free, nâng cấp bằng số dư Ví, tạo/xem/xác nhận/poll QR Hóa đơn | [x] | `S16*`, `S17*`, `S16ToS22ViewModels.cs` | L4, L6, L8 PASS 100% |
| **P3.1** | Thiết kế lại giao diện `/S16MembershipTier` (bảng so sánh 4 cấp chi tiết, thanh toán Ví trực tiếp hoặc tạo QR) & `/S17SandboxPayment` (Nạp Tiền Ảo free + QR Thanh Toán) | [x] | `Views/Modules/S16*`, `S17*`, `wwwroot/modules/s16-*`, `s17-*` | Giao diện Light Mode, 0 từ kỹ thuật |
| **P3.2** | Tích hợp nút **Quét QR Thanh Toán** đồng bộ Web & Android Web App (`core-api.js`, `_Layout.cshtml`, `#btnMobileCenterQr`, modal Hóa đơn giống ngân hàng + polling) | [x] | `core-api.js`, `_Layout.cshtml`, `core-theme.css` | Quét `BOOKSHARE:PAY:` mở Hóa đơn -> Xác nhận |
| **P4.1** | Triển khai `DeleteUserAsync` + `POST /S22AdminAnalytics/DeleteUser` + nút **Xóa TK** trên `/S22AdminAnalytics` | [x] | `S22AdminAnalyticsService.cs`, `S22AdminAnalyticsController.cs`, `S22_AdminAnalytics/*` | L8 kiểm chứng RBAC, luật bảo vệ & xóa thành công |
| **P5.1** | Mở rộng `scripts/qa_full_bot.py` Lớp 8 (Bỏ VNPay `.env`, Nạp tiền ảo, 4 cấp hội viên, QR Invoice Preview/Confirm/Replay/IDOR, Admin DeleteUser) | [x] | `scripts/qa_full_bot.py` | 100% PASS (`22/22` L8) |
| **P5.2** | Chạy `dotnet build`, `qa_full_bot.py --autostart`, `smoke_test.py --autostart` đến 100% PASS | [x] | Toàn bộ solution | 0 Error / 0 FAIL (`368/368 PASS`) |
| **P6.1** | Đồng bộ tất cả tài liệu `.md` (`SPRINT4_*`, `QA_TEST_REPORT`, `PROGRESS`, `TODO`, `README`) | [x] | `docs/*.md`, `TODO.md`, `README.md` | Khớp thực tế 100% |

---

## NHẬT KÝ THAY ĐỔI (CHANGE LOG)

| Thời điểm | Pha | Kết quả thực hiện & Kiểm chứng |
| :--- | :--- | :--- |
| 2026-11-24 | Pha 0 | Khởi tạo bộ 3 tài liệu đặc tả Sprint 4 (`SPRINT4_WALLET_MEMBERSHIP_PLAN.md`, `VIRTUAL_WALLET_AND_QR_PAY_SPEC.md`, `ADMIN_USER_DELETE_SPEC.md`) trước khi chỉnh sửa mã nguồn. |
| 2026-11-24 | Pha 1 | Bổ sung bảng `UserWallets`, mở rộng 4 cấp `Memberships` (`Free`/`Standard`/`Premium`/`Scholar`) và 3 cột hóa đơn (`TargetTier`, `DurationMonths`, `Description`) trên `Transactions` vào `01_BookShare_Schema.sql` + `DatabaseBootstrapService`; xóa sạch 4 biến `VNPay__*` khỏi `.env.example`; dựng lại LocalDB sạch & chạy EF Core Scaffold (`--schema dbo`). |
| 2026-11-24 | Pha 2–4 | Triển khai `MembershipTierCatalog` (4 cấp hội viên có thực thi luật mượn/hạn trả/hàng chờ/gia hạn thật tại `LoanFlowCoordinatorService`, `S08`, `S09`, `S11`, `S12`), Ví Tiền Ảo nạp miễn phí (`TopUpVirtualWallet`), QR Thanh Toán giống ngân hàng (`PreviewQrInvoice`, `ConfirmQrInvoice`, `InvoiceStatus` polling + đồng bộ nút QR trung tâm Web App `#btnMobileCenterQr`), giao diện mới cho `S16`/`S17`, và chức năng Admin Xóa Tài Khoản (`DeleteUserAsync` dọn sạch khóa ngoại nguyên tử + chốt bảo vệ sách đang mượn & tranh chấp mở). |
| 2026-11-24 | Pha 5–6 | Mở rộng `scripts/qa_full_bot.py` Lớp 8 (`22` checks) — phát hiện & vá ngay cột khóa ngoại `BookNotes.ReadingProgressId` trong `DeleteUserAsync`. Xác minh cuối cùng: `dotnet build` **0 Error(s)**, `qa_full_bot.py` **368/368 PASS (0 FAIL)**, `smoke_test.py` **100% PASS (0 lỗi)**; đồng bộ toàn bộ tài liệu `.md`. |
