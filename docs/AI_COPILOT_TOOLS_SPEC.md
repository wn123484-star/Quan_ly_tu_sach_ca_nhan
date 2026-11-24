# ĐẶC TẢ TRỢ LÝ AI COPILOT (GEMINI FUNCTION CALLING & ACTION CARDS)

> **Tài liệu Sprint 3 — Điều Phối Công Cụ An Toàn 2 Tầng, Multi-turn RAG & Bảo Vệ RBAC**

---

## 1. Mục tiêu Nâng cấp

Biến Trợ lý AI ở trang Trợ lý Đọc sách (`S13`), Gợi ý Lộ trình (`S14`) và Widget Chat nổi thành **Copilot thực thi được hành động** theo mô hình an toàn 2 tầng:
1. **Hội thoại Multi-turn có ngữ cảnh:** Lấy tối đa **8 tin nhắn gần nhất** trong cùng `AiChatSession` thuộc sở hữu của đúng người dùng (`session.UserId == currentUserId`) gửi kèm trong mảng `contents` tới Gemini 2.5 Flash với `HttpClient.Timeout = 20 giây`.
2. **Dữ liệu thời gian thực từ SQL:** Hệ thống tiếp tục ghép thông tin kho sách `Available`, danh sách hợp đồng đang mượn (`LoanId`, tên sách, barcode, hạn trả, số lần gia hạn) và tiến độ đọc (`ProgressId`, số trang hiện tại/tổng trang) vào `system_instruction`.
3. **Fallback thông minh cục bộ:** Khi chưa có `Gemini__ApiKey` hoặc mất kết nối tới Google API, bộ **Local Intent Analyzer** phân tích ý định tiếng Việt để vẫn trả về câu trả lời chính xác cùng đầy đủ `clientCommands` / `actionCards`.

---

## 2. Hai Tầng Công Cụ (Function Calling / Copilot Actions)

### Tầng 1 — `clientCommands` (Thao tác Giao diện An toàn, Thực thi phía Client)

| Tên hàm / `command` | Tham số (`args`) | Mô tả |
| :--- | :--- | :--- |
| `set_theme` | `{ "theme": "light" \| "dark" }` | Chuyển ngay giao diện Sáng / Tối (`data-bs-theme`) và lưu `localStorage`. |
| `navigate_to` | `{ "url": "/...", "label": "..." }` | Chỉ cho phép điều hướng tới các route hợp lệ trong whitelist 22 phân hệ + `/`. |
| `search_catalog` | `{ "keyword": "..." }` | Tạo nút/điều hướng tới `/S06SearchDiscovery?keyword=...`. |

### Tầng 2 — `actionCards` (Thao tác Nghiệp vụ — Bắt buộc Thẻ Xác nhận 1 Chạm)

> **Quy tắc bất biến:** AI **KHÔNG BAO GIỜ** tự ghi thay đổi xuống CSDL khi chỉ mới trả lời tin nhắn. Backend chỉ trả về đối tượng `actionCards` để người dùng xem thông số rõ ràng và tự bấm nút **"Xác nhận thực hiện"** gọi `POST /S13GeminiAiAssistant/ExecuteCopilotAction`.

| `actionKey` | Tham số (`parameters`) | Quyền & Ràng buộc | Dịch vụ thực thi |
| :--- | :--- | :--- | :--- |
| `renew_loan` | `{ loanId, bookTitle }` | `[Authorize]` + Anti-IDOR (`BorrowerId == currentUserId`) | `IS12LoanRenewalFineService.RenewLoanAsync` |
| `update_reading_progress` | `{ progressId, currentPage, bookTitle }` | `[Authorize]` + Anti-IDOR (`Progress.UserId == currentUserId`) | `IS03ReadingTrackerService.UpdateProgressAsync` |
| `send_my_reminder_email` | `{ email }` | `[Authorize]` (chỉ gửi tóm tắt hạn trả của chính mình) | `IResendEmailService.SendNotificationEmailAsync` |
| `send_due_reminders_batch` | `{ campaignType: "DueReminder" }` | **`Librarian,Admin` (RBAC chặt)** — trả `403` với `User`/`Curator` | `IS18NotificationHubService.TriggerResendCampaignAsync` |

---

## 3. Hợp Đồng API (`S13GeminiAiAssistantController`)

1. **`POST /S13GeminiAiAssistant/SendMessage`** (`[Authorize]`, `[ValidateAntiForgeryToken]`) và **`POST /api/ai-chat/quick-ask`** (Widget nổi):
   - Trả thêm trường:
     - `clientCommands`: `[{ command, label, args }]`
     - `actionCards`: `[{ actionKey, title, description, confirmLabel, requiresRole, parameters }]`
   - Với `QuickAsk` của **Guest** (chưa đăng nhập): nếu hỏi thông tin cá nhân sẽ nhắc đăng nhập; nếu hỏi gợi ý kho sách công khai trả về danh sách sách có `Mã vạch:` và `0đ` (bảo toàn 100% kiểm thử Guest Lớp 2). Không bao giờ đề xuất `actionCards` nhạy cảm cho Guest.
2. **`POST /S13GeminiAiAssistant/ExecuteCopilotAction`** (`[Authorize]`, `[ValidateAntiForgeryToken]`):
   - Đầu vào `{ actionKey, parameters }`.
   - **RBAC:** `actionKey == "send_due_reminders_batch"` bắt buộc `User` có vai trò `Librarian` hoặc `Admin`; nếu không, ném `UnauthorizedAccessException` trả **`HTTP 403 Forbidden`**.
   - **Anti-IDOR:** Mọi lỗi quyền sở hữu từ `S03`/`S12` trả **`HTTP 403 Forbidden`**.
   - **Phản hồi:** `HTTP 200 OK { title, message }` hoặc `HTTP 400 BadRequest` kèm lý do nghiệp vụ rõ ràng (chưa đủ điều kiện gia hạn, quá tổng số trang, ...).

---

## 4. Yêu Cầu Giao Diện An Toàn Chống XSS

- Khung chat ở cả `Views/Modules/S13_GeminiAiAssistant/Index.cshtml` lẫn Widget AI nổi `_Layout.cshtml` phải dựng DOM Action Card và Command Chip bằng `document.createElement(...)` + `textContent` / `innerText` (không nối chuỗi HTML trực tiếp từ dữ liệu AI).
- Thẻ Action Card sử dụng nền sáng hài hòa (`--portal-surface`, viền tinh tế), nút **"Xác nhận thực hiện"** kích hoạt spinner chống bấm đúp qua `apiFetch()`.
