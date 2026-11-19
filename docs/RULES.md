# BỘ QUY TẮC KỸ THUẬT & QUY CHUẨN PHÁT TRIỂN (RULES.md)

Tài liệu này là **Hiến pháp Kỹ thuật** bắt buộc áp dụng cho toàn bộ thành viên và AI Agent khi tham gia phát triển dự án **BookShare** trên nền tảng **ASP.NET Core 10 MVC (.NET 10)**.

---

## 1. Quy tắc Nghiệp vụ Bất khả Xâm phạm (Domain Guardrails)

1. **Cấm tuyệt đối mô hình Thương mại điện tử mua bán sách:**
   - Giao diện và cơ sở dữ liệu **KHÔNG ĐƯỢC PHÉP** có: Giá bán sách (`Price`), nút `"Mua ngay"`, nút `"Thêm vào giỏ hàng"`, trang Giỏ hàng (`ShoppingCart`), hay hóa đơn mua đứt sách.
   - Các nút hành động trên thẻ sách chỉ được phép là: `"Đặt mượn Kho chung"`, `"Mượn P2P từ Chủ sách"`, `"Xếp hàng chờ ( Đặt trước )"`, `"Lưu vào Tủ sách"`, hoặc `"Cập nhật trang đọc"`.
2. **Chỉ chia sẻ Sách Giấy vật lý:**
   - Cấm mọi chức năng upload tệp PDF/EPUB/MOBI. Mọi bản sao sách (`BookCopies`) đều là sách giấy có mã vạch (`Barcode`), tình trạng vật lý (`Condition`: `New`, `Good`, `Fair`, `Worn`) và biên bản ảnh chụp thực tế (`ConditionReports`).

---

## 2. Quy chuẩn Kiến trúc .NET 10 & Database-First

### 2.1. Quy trình Database-First Scaffold
1. Mọi thay đổi về cấu trúc bảng phải được cập nhật tại `database/01_BookShare_Schema.sql` trước tiên.
2. Sử dụng lệnh chuẩn EF Core CLI để sinh Model tự động vào `Models/Entities/`:
   ```bash
   dotnet ef dbcontext scaffold "Name=ConnectionStrings:DefaultConnection" Microsoft.EntityFrameworkCore.SqlServer -o Models/Entities -c BookShareDbContext --force
   ```
3. **Nghiêm cấm sửa tay các file trong `Models/Entities/`** vì lần chạy Scaffold tiếp theo sẽ ghi đè. Nếu cần thêm thuộc tính tính toán hoặc phương thức hỗ trợ, bắt buộc tạo `partial class` ở thư mục riêng hoặc ánh xạ sang `Models/ViewModels/`.

### 2.2. Nguyên tắc Phân tầng (Strict Service Layer Pattern)
- **Controller KHÔNG BAO GIỜ được phép inject hoặc gọi trực tiếp `BookShareDbContext`.**
- Luồng gọi bắt buộc: `Controller` $\rightarrow$ `IService (Interface)` $\rightarrow$ `Service (Implementation)` $\rightarrow$ `BookShareDbContext`.
- Mỗi Controller Action chỉ làm nhiệm vụ: Nhận Request/ViewModel, kiểm tra `ModelState`, lấy `currentUserId` từ `User.FindFirstValue(ClaimTypes.NameIdentifier)`, gọi `Service`, và trả về `View()` hoặc `JsonResult`.

---

## 3. Quy chuẩn Cô lập 22 Dịch vụ (Modular UI Rule)

1. **Nguyên tắc Bộ 3 File Độc lập (1-1-1):**
   - Mỗi dịch vụ từ `S01` đến `S22` chỉ sử dụng đúng 1 file `.cshtml`, 1 file `.css` và 1 file `.js` riêng biệt theo bảng ánh xạ trong `docs/ARCHITECTURE.md`.
2. **Cách nhúng CSS & JS trong mỗi file `Index.cshtml`:**
   ```html
   @section Styles {
       <link rel="stylesheet" href="~/modules/s03-reading-tracker/s03-reading-tracker.css" asp-append-version="true" />
   }
   @section Scripts {
       <script type="module" src="~/modules/s03-reading-tracker/s03-reading-tracker.js" asp-append-version="true"></script>
   }
   ```
3. **Quy tắc Cách ly khi Chỉnh sửa:**
   - Khi nâng cấp hoặc sửa lỗi cho dịch vụ `Sxx`, lập trình viên / AI chỉ được phép chỉnh sửa trong phạm vi bộ 3 file của `Sxx` và `Service` tương ứng, tuyệt đối không làm ảnh hưởng đến giao diện của 21 dịch vụ còn lại.

---

## 4. Quy chuẩn Giao diện & Kế thừa `book-tracker-ver07`

1. **Bố cục Cổng Dịch vụ (Service Portal Layout):**
   - Header điều hướng thông minh hiển thị theo Role (`User`, `Curator`, `Librarian`, `Admin`), tích hợp nút Sao lưu / Phục hồi JSON kế thừa từ `book-tracker-ver07`.
   - Trang chủ / Khám phá có **Thanh tìm kiếm đa tiêu chí cỡ lớn (Hero Search Bar)**, **Cụm Icon Dịch vụ nhanh (Quick Service Hub)**, và **Các kệ sách phân loại trực quan**.
2. **Bộ Font chữ & Đa Giao diện (Themes):**
   - Sử dụng bộ font `Roboto` nội bộ (`Roboto-Regular.ttf`, `Roboto-Bold.ttf`, `Roboto-Italic.ttf`...) được khai báo `@font-face` trong `wwwroot/css/core-theme.css`.
   - Hỗ trợ chuyển đổi mượt mà giữa các chế độ giao diện: `Default (Bootstrap 5)`, `Flatly`, `Cyborg (Ban đêm - Dark Mode)`, `Morph`, và `Pure CSS Mode (.pure-mode)`.
3. **Khung Hiển thị Mã Trạng thái & Bắt Ngoại lệ (`#systemStatusBox`):**
   - Mọi trang phải tích hợp thanh trạng thái `.status-banner` kế thừa từ `book-tracker-ver07`, hiển thị trực quan các mã phản hồi HTTP / Hệ thống:
     - `MÃ 200` (`.status-200` - Xanh lá): Thao tác thành công / Hệ thống ổn định.
     - `MÃ 300` (`.status-300` - Xanh dương): Điều hướng dự phòng / Gợi ý thao tác tiếp theo.
     - `MÃ 400` (`.status-400` - Cam/Vàng): Dữ liệu nhập vào chưa hợp lệ / Vi phạm quy tắc nghiệp vụ.
     - `MÃ 404` (`.status-404` - Cam đậm): Không tìm thấy bản sao sách hoặc mã đơn mượn.
     - `MÃ 500` (`.status-500` - Đỏ): Ngoại lệ máy chủ / Mất kết nối mạng hoặc dịch vụ bên thứ 3.

---

## 5. Kỹ thuật Triển khai Tích hợp Bên thứ 3 Chuẩn .NET 10

### 5.1. Cấu hình Biến môi trường `.env` & `User Secrets`
- Đặt `DotNetEnv.Env.Load();` ở dòng đầu tiên trong `Program.cs` trước `WebApplication.CreateBuilder(args)`.
- Trong file `.env`, sử dụng dấu **2 gạch dưới (`__`)** để ánh xạ tự động vào `IConfiguration` của .NET 10 (ví dụ: `Gemini__ApiKey` $\rightarrow$ `builder.Configuration["Gemini:ApiKey"]`).
- Tuyệt đối không đưa khóa thật vào `appsettings.json` hoặc commit file `.env` lên Git.

### 5.2. Đăng nhập Google OAuth 2.0 (`S01_AuthOtpGoogle`)
- Sử dụng gói NuGet chính thức `Microsoft.AspNetCore.Authentication.Google` tích hợp với `SignInManager<ApplicationUser>`.
- Cấu hình chuẩn trong `Program.cs`:
  ```csharp
  builder.Services.AddAuthentication()
      .AddGoogle(options =>
      {
          options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
          options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
          options.CallbackPath = "/signin-google";
      });
  ```
- Luồng xử lý Callback sử dụng `GetExternalLoginInfoAsync()`, `ExternalLoginSignInAsync()` và `AddLoginAsync()` để liên kết tài khoản vào bảng `AspNetUserLogins`.

### 5.3. Gửi OTP 6 Số & Nhắc hạn qua Resend (`S01` & `S18`)
- `ResendEmailService` sử dụng `IHttpClientFactory` gọi tới `POST https://api.resend.com/emails` với Header `Authorization: Bearer <Resend:ApiKey>`.
- Sinh mã OTP 6 chữ số an toàn mật mã học:
  ```csharp
  int otpNumber = RandomNumberGenerator.GetInt32(100000, 999999);
  ```
- Không lưu mã OTP dạng rõ (plaintext) trong DB: Băm mã bằng `SHA256.HashData()` lưu vào cột `OtpCodeHash` của bảng `OtpVerifications` với thời hạn `ExpiresAt = DateTime.UtcNow.AddMinutes(5)` và cờ `IsUsed = false`.

### 5.4. Trợ lý AI Gemini Kết hợp Ngữ cảnh CSDL (`S13` & `S14`)
- **Cấm tuyệt đối gọi trực tiếp Gemini API từ file `.js` phía trình duyệt** (để bảo vệ `Gemini:ApiKey`).
- Frontend gửi câu hỏi tới `POST /api/ai-chat/send`.
- Backend `GeminiAiService` truy vấn nhanh danh sách sách sẵn có trong `BookCopies` + trạng thái đọc/mượn hiện tại của User làm `SystemInstruction` (RAG nhẹ), gọi model `gemini-2.5-flash`, lưu lịch sử vào `AiChatSessions` & `AiChatMessages` rồi trả kết quả về trình duyệt.
- Phía JS bắt buộc dùng `element.innerText = responseText` khi vẽ bong bóng chat để chống tấn công XSS.

---

## 6. Quy tắc Bảo mật Ứng dụng (Security Checklist)

1. **Chống CSRF (Cross-Site Request Forgery):**
   - Mọi request `POST`, `PUT`, `DELETE` đều phải kèm `[ValidateAntiForgeryToken]`. File `wwwroot/js/core-api.js` tự động đính kèm header `RequestVerificationToken` từ thẻ `<meta name="csrf-token" />` hoặc input ẩn `__RequestVerificationToken`.
2. **Chống IDOR (Insecure Direct Object Reference):**
   - Khi sửa/xóa kệ sách (`Shelves`), bản sao sách cá nhân (`BookCopies`), ghi chú (`BookNotes`) hoặc duyệt đơn mượn (`LoanRequests`), tầng `Service` bắt buộc kiểm tra tường minh:
     ```csharp
     if (entity.OwnerUserId != currentUserId && !isUserAdmin)
         throw new UnauthorizedAccessException("Bạn không có quyền thao tác trên tài nguyên của người khác.");
     ```
3. **Bảo mật Cookie & Phiên đăng nhập:**
   - Cấu hình Cookie của ASP.NET Core Identity với `HttpOnly = true`, `SecurePolicy = CookieSecurePolicy.SameAsRequest`, `SameSite = SameSiteMode.Lax`, và `SlidingExpiration = true`.
