# ĐẶC TẢ KỸ THUẬT CHỨC NĂNG ADMIN XÓA TÀI KHOẢN (`S22`)

> **Phạm vi:** Cho phép Quản trị viên (`Admin`) tại Trung tâm Quản trị Hệ thống (`/S22AdminAnalytics`) xóa vĩnh viễn các tài khoản không còn hoạt động hoặc tài khoản rác, bảo đảm an toàn tuyệt đối toàn vẹn dữ liệu sách vật lý & khóa ngoại SQL Server.

---

## 1. API & Bảo Mật
- **Endpoint:** `POST /S22AdminAnalytics/DeleteUser`
- **Kiểm soát truy cập:** `[Authorize(Roles = "Admin")]` + `[ValidateAntiForgeryToken]`
- **Request DTO:** `{ "userId": "string" }`

---

## 2. Luật Bảo Vệ An Toàn Dữ Liệu (Fail-Safe Rules)

| Trường hợp | Phản hồi | Lý do |
| :--- | :---: | :--- |
| Không phải `Admin` (`User`, `Curator`, `Librarian`) | `403 Forbidden` | Chỉ Quản trị viên tối cao mới có quyền xóa tài khoản |
| Tự xóa tài khoản đang đăng nhập (`targetUserId == adminUserId`) | `400 Bad Request` | Ngăn khóa hệ thống do tự xóa phiên quản trị |
| Xóa tài khoản Quản trị gốc (`u-admin-01`) hoặc `Admin` duy nhất còn lại | `400 Bad Request` | Bảo đảm hệ thống luôn còn ít nhất 1 Quản trị viên gốc |
| Tài khoản đang mượn sách chưa trả (`Loans.Status IN ('Active', 'Overdue', 'Disputed')`) | `400 Bad Request` | Tránh mất dấu bản sao sách vật lý đang lưu thông |
| Tài khoản là chủ sách P2P đang có bản sao đang cho người khác mượn (`BookCopies.Status IN ('Borrowed', 'Reserved')`) | `400 Bad Request` | Bảo vệ hợp đồng mượn đang diễn ra của độc giả khác |
| Tài khoản đang có hồ sơ tranh chấp mở (`Disputes.Status IN ('Open', 'UnderReview')`) | `400 Bad Request` | Cần phân xử xong tranh chấp trước khi xóa tài khoản |

---

## 3. Chuỗi Dọn Khóa Ngoại Nguyên Tử (`IDbContextTransaction`)

Khi tài khoản vượt qua toàn bộ kiểm tra an toàn, `S22AdminAnalyticsService.DeleteUserAsync` mở 1 giao dịch SQL (`BeginTransactionAsync`) và xử lý theo đúng thứ tự phụ thuộc khóa ngoại (`ON DELETE NO ACTION` trước):

1. Nếu tài khoản đang làm quản lý thư viện/trạm CLB (`Libraries.ManagerUserId`), bàn giao về `u-admin-01`.
2. Xóa các bản ghi liên quan trong `QrLoginSessions` (`InitiatorUserId` / `ApprovedUserId`), `BookHandoverQrs` (`CreatedByUserId` / `ConsumedByUserId`), `RoleUpgradeRequests` (`UserId` / `ReviewedByUserId`).
3. Xóa `Disputes` đã đóng liên quan tới tài khoản (`RaisedByUserId`, `ResolvedByAdminId`, hoặc thuộc các `Loan` đã đóng của tài khoản), `ConditionReports` (`ReportedByUserId` hoặc thuộc `Loan` đã đóng), `Reviews` (`ReviewerId` hoặc `TargetType='User' AND TargetId=targetUserId`), `Transactions` của tài khoản.
4. Với các `LoanRequests` có `ApproverId = targetUserId` nhưng thuộc người mượn khác: gán `ApproverId = NULL`. Xóa các `Loans` & `LoanRequests` đã hoàn tất/hủy của tài khoản và của các `BookCopies` thuộc sở hữu tài khoản.
5. Xóa `ShelfItems`, `Shelves`, `BookCopies` thuộc `OwnerUserId = targetUserId`, `Reservations`, `BookNotes`, `ReadingProgresses`, `AiChatMessages`, `AiChatSessions`, `Notifications`, `OtpVerifications`, `Memberships`, `UserWallets`.
6. Xóa liên kết Identity (`AspNetUserRoles`, `AspNetUserLogins`, `AspNetUserClaims`, `AspNetUserTokens`) và cuối cùng xóa bản ghi tại `AspNetUsers`.
7. Commit giao dịch. Nếu trình duyệt của tài khoản vừa xóa vẫn giữ Cookie cũ, `OnValidatePrincipal` trong `Program.cs` tự động hủy phiên ở yêu cầu tiếp theo (0 lỗi 500).
