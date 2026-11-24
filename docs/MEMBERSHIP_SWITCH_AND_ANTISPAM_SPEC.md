# ĐẶC TẢ LUẬT GÓI HỘI VIÊN (CHỐNG MUA TRÙNG, CHUYỂN GÓI CÓ LỆ PHÍ, HỦY GÓI) & SỔ CÁI CHỐNG SPAM (SPRINT 5)

## 1. BỔ SUNG LƯỢC ĐỒ CSDL ĐẢM BẢO TOÀN VẸN & MINH BẠCH 1 LẦN

### 1.1. Bảng `dbo.Transactions`
Bổ sung các cột mới vào `database/01_BookShare_Schema.sql` và `DatabaseBootstrapService.cs` (idempotent):
- `IdempotencyKey NVARCHAR(100) NULL` — Khóa chống gửi lặp duy nhất mỗi lượt thao tác, gắn Filtered Unique Index:
  ```sql
  CREATE UNIQUE NONCLUSTERED INDEX [UX_Transactions_IdempotencyKey]
  ON [dbo].[Transactions]([IdempotencyKey] ASC)
  WHERE [IdempotencyKey] IS NOT NULL;
  ```
- `ActionType NVARCHAR(30) NULL` — Phân loại hành động nghiệp vụ (`Register`, `Switch`, `Cancel`, `TopUp`, `Fine`, `Deposit`).
- `SwitchFee DECIMAL(18,2) NOT NULL CONSTRAINT [DF_Transactions_SwitchFee] DEFAULT (0)` — Lệ phí chuyển đổi gói tách bạch minh bạch.
- `ProrationCredit DECIMAL(18,2) NOT NULL CONSTRAINT [DF_Transactions_ProrationCredit] DEFAULT (0)` — Khoản khấu trừ hoàn lại theo số ngày còn hiệu lực của gói cũ.
- `BalanceBefore DECIMAL(18,2) NULL` & `BalanceAfter DECIMAL(18,2) NULL` — Số dư Ví Tiền Ảo ngay trước và ngay sau giao dịch.
- Mở rộng `CK_Transactions_Type`: `IN (N'Membership', N'MembershipSwitch', N'MembershipCancel', N'Fine', N'SimulatedDeposit', N'WalletTopUp')`.
- Bổ sung `CK_Transactions_Amount`: `CHECK ([Amount] >= 0)` (cho phép ghi biên lai hủy gói `0đ` minh bạch).

### 1.2. Bảng `dbo.SystemIconSettings` & `dbo.AspNetUsers`
- `dbo.SystemIconSettings`: bổ sung `CustomImageData VARBINARY(MAX) NULL` và `CustomImageContentType NVARCHAR(100) NULL` để lưu nhị phân ảnh biểu tượng trực tiếp trong SQL Server, tự động phục hồi ra `/uploads/icons/` nếu container khởi tạo lại.
- `dbo.AspNetUsers`: bổ sung `UiPreferencesJson NVARCHAR(1000) NULL` lưu cài đặt giao diện/font/cỡ chữ của từng thành viên.

---

## 2. LUẬT GÓI HỘI VIÊN CHẶT CHẼ (`S16MembershipTierService`)

### 2.1. Cấm mua trùng gói đang sở hữu
- Nếu thành viên đang có một gói trả phí còn hiệu lực (`ActiveTier` thuộc `Standard`, `Premium`, `Scholar` và `ExpiryDate > UtcNow`):
  - **Không cho phép mua lại đúng gói đang sử dụng** (`targetTier == activeTier`): Backend trả về `HTTP 400 Bad Request` kèm thông báo rõ ngày hết hạn hiện tại.
  - Trên UI `/S16MembershipTier`: thẻ gói hiện tại hiển thị badge **"Gói Hiện Tại (Đã Sở Hữu)"**, vô hiệu hóa nút mua, hiển thị trực tiếp **Nút Hủy Gói** để người dùng chủ động chuyển về `Free` nếu muốn.

### 2.2. Cơ chế Chuyển Gói (`SwitchTier`) có lệ phí chuyển đổi minh bạch
- Khi thành viên đang sở hữu gói trả phí `A` (`Standard`/`Premium`/`Scholar`) và muốn chuyển sang gói trả phí khác `B` (`B != A`):
  - Không mua chồng hai gói song song; hệ thống thực hiện **Chuyển đổi gói** với công thức hiển thị minh bạch trên cả màn hình xem trước (`POST /S16MembershipTier/PreviewTierSwitch`) lẫn hóa đơn lưu CSDL:
    - **Giá gói mục tiêu (`NewPlanPrice`)**: theo kỳ hạn `1 / 6 / 12` tháng của gói `B`.
    - **Lệ phí chuyển gói (`SwitchFee`)**: **`15.000đ` ảo** (cố định, minh bạch).
    - **Khấu trừ giá trị còn dư của gói cũ (`ProrationCredit`)**: tính theo số ngày còn hiệu lực của gói `A` (`RemainingDays / TotalDays * Giá ước tính gói A`, làm tròn nghìn, tối đa không vượt quá `NewPlanPrice`).
    - **Tổng tiền ảo cần thanh toán (`TotalPayable`)**:
      $$\text{TotalPayable} = \max\left(\text{SwitchFee},\; \text{NewPlanPrice} - \text{ProrationCredit} + \text{SwitchFee}\right)$$
  - Hỗ trợ cả **Chuyển gói tức thì bằng Ví Tiền Ảo** (`InstantWallet`) lẫn **Tạo Hóa đơn QR Chuyển Gói** (`QrInvoice`, lưu `TransactionType = 'MembershipSwitch'`, `SwitchFee`, `ProrationCredit`).
  - Khi giao dịch hoàn tất: vô hiệu hóa bản ghi gói `A` (`IsActive = false`) và kích hoạt duy nhất bản ghi gói `B` mới (`IsActive = true`), đồng thời tự hủy các hóa đơn `Pending` chuyển/đăng ký gói cũ còn tồn đọng.

### 2.3. Chức năng Hủy Gói Hội Viên (`POST /S16MembershipTier/CancelMembership`)
- Chỉ áp dụng khi thành viên đang sử dụng gói trả phí (`Standard` / `Premium` / `Scholar`); nếu đang ở `Free` trả về `HTTP 400`.
- **Chốt an toàn hạn mức sách đang mượn**: Nếu số sách đang mượn (`Active` / `Overdue` / `Disputed`) vượt quá hạn mức gói `Free` (`2 + LendQuotaBonus`), từ chối hủy với `HTTP 400` và thông báo rõ số sách cần trả bớt trước khi hạ về `Free`.
- Khi hợp lệ:
  1. Đánh dấu gói trả phí hiện tại `IsActive = false`, `ExpiryDate = UtcNow`.
  2. Kích hoạt lại gói `Free` (`MaxConcurrentLoans = 2`, `ExpiryDate = null`).
  3. Tự động hủy mọi hóa đơn `Pending` thuộc `Membership` / `MembershipSwitch` của người dùng đó.
  4. Ghi 1 bản ghi biên lai minh bạch `TransactionType = 'MembershipCancel'`, `Amount = 0`, `PaymentStatus = 'Completed'`, `BalanceBefore = BalanceAfter = wallet.Balance`.
  5. Gửi thông báo xác nhận hủy gói vào Trung tâm Thông báo (`S18`).

---

## 3. CHỐNG SPAM & MINH BẠCH 1 LẦN TRÊN VÍ / THANH TOÁN (`S17SandboxPaymentService`)

1. **Giao dịch CSDL nguyên tử (`BeginTransactionAsync`) + Khóa hàng `UPDLOCK, HOLDLOCK`**:
   - Mọi thao tác cộng/trừ `dbo.UserWallets` (`TopUpVirtualWalletAsync`, `ConfirmQrInvoicePaymentAsync`, `SwitchOrExtendMembershipAsync` InstantWallet, `RefundSimulatedDepositAsync`) đều chạy trong transaction nguyên tử, đảm bảo không bao giờ trừ đúp hoặc lệch số dư khi gửi đồng thời.
2. **Khóa `IdempotencyKey` chống bấm đúp / gửi lại (F5)**:
   - Client sinh mã `idempotencyKey` mới cho mỗi lần người dùng bấm xác nhận; nếu cùng `idempotencyKey` được gửi lặp lại -> Backend chặn trùng lặp (`HTTP 409/400`), tuyệt đối không ghi giao dịch hay cộng/trừ ví lần 2.
3. **Chống spam hóa đơn `Pending`**:
   - Khi tạo hóa đơn `Pending` trùng `(UserId, TransactionType, TargetTier, DurationMonths, LoanId, Amount)` trong vòng 10 phút -> **tái sử dụng ngay mã `#TX-` đang chờ** thay vì tạo hàng loạt hóa đơn rác.
   - Mỗi thành viên chỉ được giữ tối đa **3 hóa đơn `Pending`** chưa thanh toán cùng lúc (trừ Quản trị viên).
4. **Chống spam Nạp Tiền Ảo miễn phí**:
   - Sliding-window rate limit trên bộ nhớ máy chủ: tối đa **5 lượt nạp/phút** mỗi tài khoản (trả `HTTP 429` nếu spam liên tục).
   - Trần số dư ví an toàn: tối đa **`50.000.000đ` ảo** mỗi tài khoản (dư sức trải nghiệm mọi gói dài hạn, không gây tràn số hay lạm dụng DB).
   - Mọi giao dịch hiển thị rõ **Số dư trước → Biến động (+/-) → Số dư sau** trong bảng lịch sử sổ cái.
