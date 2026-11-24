using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S13GeminiAiService : IS13GeminiAiService
{
    private static readonly HashSet<string> AllowedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/S01AuthOtpGoogle",
        "/S02PersonalShelf",
        "/S03ReadingTracker",
        "/S04BookNotesQuotes",
        "/S05IsbnAutoCatalog",
        "/S06SearchDiscovery",
        "/S07LibraryCatalog",
        "/S08LibraryLoanFlow",
        "/S09P2PLoanFlow",
        "/S10ConditionReport",
        "/S11HotBookQueue",
        "/S12LoanRenewalFine",
        "/S13GeminiAiAssistant",
        "/S14AiRecommendation",
        "/S15TrustScoreReview",
        "/S16MembershipTier",
        "/S17SandboxPayment",
        "/S18NotificationHub",
        "/S19DisputeResolution",
        "/S20ExchangeHubClub",
        "/S21LibrarianDesk",
        "/S22AdminAnalytics"
    };

    private readonly BookShareDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly IS03ReadingTrackerService _readingService;
    private readonly IS12LoanRenewalFineService _renewalService;
    private readonly IS18NotificationHubService _notificationService;
    private readonly IResendEmailService _resendEmailService;

    public S13GeminiAiService(
        BookShareDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        IS03ReadingTrackerService readingService,
        IS12LoanRenewalFineService renewalService,
        IS18NotificationHubService notificationService,
        IResendEmailService resendEmailService)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _readingService = readingService;
        _renewalService = renewalService;
        _notificationService = notificationService;
        _resendEmailService = resendEmailService;
    }

    public async Task<S13GeminiAssistantPageViewModel> GetAssistantPageAsync(string currentUserId, int? sessionId = null)
    {
        var user = !string.IsNullOrWhiteSpace(currentUserId)
            ? await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId)
            : null;
        if (user == null)
        {
            currentUserId = string.Empty;
        }

        string modelName = AuthConfigHelper.SanitizeEnvValue(_config["Gemini:Model"]
            ?? Environment.GetEnvironmentVariable("GEMINI_MODEL"));
        if (string.IsNullOrWhiteSpace(modelName)) modelName = "gemini-2.5-flash";
        string apiKey = AuthConfigHelper.SanitizeEnvValue(_config["Gemini:ApiKey"]
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
        bool hasKey = !string.IsNullOrWhiteSpace(apiKey) && !apiKey.StartsWith("demo", StringComparison.OrdinalIgnoreCase);

        var sessions = !string.IsNullOrWhiteSpace(currentUserId)
            ? await _db.AiChatSessions
                .AsNoTracking()
                .Where(s => s.UserId == currentUserId)
                .OrderByDescending(s => s.UpdatedAt)
                .Select(s => new S13ChatSessionDto
                {
                    SessionId = s.Id,
                    Title = s.Title,
                    ModelName = s.ModelName,
                    MessageCount = s.AiChatMessages.Count,
                    UpdatedAt = s.UpdatedAt
                })
                .ToListAsync()
            : new List<S13ChatSessionDto>();

        if (!sessions.Any() && user != null)
        {
            var newSession = new AiChatSession
            {
                UserId = currentUserId,
                Title = "Tư vấn chọn sách & Tra cứu hạn trả",
                ModelName = modelName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.AiChatSessions.Add(newSession);
            await _db.SaveChangesAsync();

            _db.AiChatMessages.Add(new AiChatMessage
            {
                SessionId = newSession.Id,
                UserId = currentUserId,
                Sender = "Gemini",
                MessageText = $"Xin chào {user.FullName}! Mình là Trợ lý Copilot BookShare. Bạn có thể hỏi chọn sách, tra cứu hạn trả, đổi giao diện Sáng/Tối, mở nhanh tính năng hoặc yêu cầu gia hạn sách ngay trong khung chat!",
                PromptTokens = 48,
                ReferencedBookIdsJson = "[1,2,3]",
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            sessions.Add(new S13ChatSessionDto
            {
                SessionId = newSession.Id,
                Title = newSession.Title,
                ModelName = newSession.ModelName,
                MessageCount = 1,
                UpdatedAt = newSession.UpdatedAt
            });
        }

        int activeSessionId = sessionId.HasValue && sessions.Any(s => s.SessionId == sessionId.Value)
            ? sessionId.Value
            : (sessions.FirstOrDefault()?.SessionId ?? 0);

        var messages = activeSessionId > 0
            ? await _db.AiChatMessages
                .AsNoTracking()
                .Where(m => m.SessionId == activeSessionId && m.UserId == currentUserId)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new S13ChatMessageDto
                {
                    MessageId = m.Id,
                    Sender = m.Sender,
                    MessageText = m.MessageText,
                    PromptTokens = m.PromptTokens,
                    ReferencedBookIdsJson = m.ReferencedBookIdsJson,
                    CreatedAt = m.CreatedAt
                })
                .ToListAsync()
            : new List<S13ChatMessageDto>();

        var ragBooks = await _db.Books
            .AsNoTracking()
            .Include(b => b.Category)
            .Include(b => b.BookCopies)
            .Where(b => b.BookCopies.Any(bc => bc.Status == "Available" && bc.IsLendable))
            .OrderByDescending(b => b.BookCopies.Count(bc => bc.Status == "Available"))
            .Take(8)
            .Select(b => new S13RagContextBookDto
            {
                BookId = b.Id,
                Title = b.Title,
                Author = b.Author,
                CategoryName = b.Category.Name,
                AvailableCopies = b.BookCopies.Count(bc => bc.Status == "Available" && bc.IsLendable)
            })
            .ToListAsync();

        int availableCopiesTotal = await _db.BookCopies
            .CountAsync(bc => bc.Status == "Available" && bc.IsLendable);

        int activeLoansCount = await _db.Loans
            .CountAsync(l => l.LoanRequest.BorrowerId == currentUserId && (l.Status == "Active" || l.Status == "Overdue"));

        int readingCount = await _db.ReadingProgresses
            .CountAsync(rp => rp.UserId == currentUserId);

        return new S13GeminiAssistantPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserName = user?.FullName ?? "Độc giả BookShare",
            ActiveModelName = modelName,
            HasConfiguredApiKey = hasKey,
            RagAvailableCopiesCount = availableCopiesTotal,
            UserActiveLoansCount = activeLoansCount,
            UserReadingBooksCount = readingCount,
            SelectedSessionId = activeSessionId,
            Sessions = sessions,
            Messages = messages,
            RagBooksPreview = ragBooks
        };
    }

    public async Task<(bool Success, string Message, int SessionId)> CreateSessionAsync(
        string currentUserId,
        string title)
    {
        string cleanTitle = string.IsNullOrWhiteSpace(title)
            ? $"Phiên tư vấn AI ({DateTime.Now:dd/MM HH:mm})"
            : title.Trim();

        string modelName = _config["Gemini:Model"] ?? "gemini-2.5-flash";

        var session = new AiChatSession
        {
            UserId = currentUserId,
            Title = cleanTitle,
            ModelName = modelName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.AiChatSessions.Add(session);
        await _db.SaveChangesAsync();

        _db.AiChatMessages.Add(new AiChatMessage
        {
            SessionId = session.Id,
            UserId = currentUserId,
            Sender = "Gemini",
            MessageText = $"Đã khởi tạo phiên hội thoại \"{cleanTitle}\". Hãy đặt câu hỏi để mình tra cứu thông tin sách & hỗ trợ thao tác cho bạn nhé!",
            PromptTokens = 32,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return (true, $"Đã tạo phiên hội thoại AI mới: \"{cleanTitle}\"!", session.Id);
    }

    public async Task<(bool Success, string ReplyText, int SessionId, int PromptTokens, string SourceMode)> SendMessageWithRagAsync(
        string currentUserId,
        int? sessionId,
        string userMessage)
    {
        var result = await SendCopilotMessageAsync(currentUserId, "User", sessionId, userMessage);
        return (result.Success, result.ReplyText, result.SessionId, result.PromptTokens, result.SourceMode);
    }

    public async Task<CopilotChatResultDto> SendCopilotMessageAsync(
        string currentUserId,
        string currentUserRole,
        int? sessionId,
        string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return new CopilotChatResultDto
            {
                Success = false,
                ReplyText = "Vui lòng nhập nội dung câu hỏi gửi cho Trợ lý AI.",
                SourceMode = "Validation"
            };
        }

        string cleanMsg = userMessage.Trim();
        string modelName = AuthConfigHelper.SanitizeEnvValue(_config["Gemini:Model"]
            ?? Environment.GetEnvironmentVariable("GEMINI_MODEL"));
        if (string.IsNullOrWhiteSpace(modelName)) modelName = "gemini-2.5-flash";
        string apiKey = AuthConfigHelper.SanitizeEnvValue(_config["Gemini:ApiKey"]
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY"));

        // 1. Xác định Session thuộc đúng chủ sở hữu (Anti-IDOR)
        AiChatSession? session = null;
        if (sessionId.HasValue && sessionId.Value > 0)
        {
            session = await _db.AiChatSessions.FirstOrDefaultAsync(s => s.Id == sessionId.Value && s.UserId == currentUserId);
        }
        if (session == null)
        {
            session = await _db.AiChatSessions
                .Where(s => s.UserId == currentUserId)
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefaultAsync();
        }
        if (session == null)
        {
            session = new AiChatSession
            {
                UserId = currentUserId,
                Title = "Hội thoại Trợ lý AI Copilot",
                ModelName = modelName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.AiChatSessions.Add(session);
            await _db.SaveChangesAsync();
        }

        // 2. Nạp lịch sử hội thoại Multi-turn (tối đa 8 tin nhắn gần nhất trong phiên của User)
        var recentHistory = await _db.AiChatMessages
            .AsNoTracking()
            .Where(m => m.SessionId == session.Id && m.UserId == currentUserId)
            .OrderByDescending(m => m.Id)
            .Take(8)
            .OrderBy(m => m.Id)
            .ToListAsync();

        var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
        var availableCopies = await _db.BookCopies
            .AsNoTracking()
            .Include(bc => bc.Book).ThenInclude(b => b.Category)
            .Include(bc => bc.Library)
            .Where(bc => bc.Status == "Available" && bc.IsLendable)
            .Take(10)
            .ToListAsync();

        var activeLoans = await _db.Loans
            .AsNoTracking()
            .Include(l => l.LoanRequest).ThenInclude(lr => lr.BookCopy).ThenInclude(bc => bc.Book)
            .Where(l => l.LoanRequest.BorrowerId == currentUserId && (l.Status == "Active" || l.Status == "Overdue"))
            .ToListAsync();

        var readingList = await _db.ReadingProgresses
            .AsNoTracking()
            .Include(rp => rp.Book)
            .Where(rp => rp.UserId == currentUserId)
            .ToListAsync();

        var referencedIds = availableCopies.Select(c => c.BookId).Distinct().Take(5).ToList();
        string referencedJson = JsonSerializer.Serialize(referencedIds);

        _db.AiChatMessages.Add(new AiChatMessage
        {
            SessionId = session.Id,
            UserId = currentUserId,
            Sender = "User",
            MessageText = cleanMsg,
            PromptTokens = Math.Max(12, cleanMsg.Length / 3),
            ReferencedBookIdsJson = referencedJson,
            CreatedAt = DateTime.UtcNow
        });

        var ragBuilder = BuildSystemInstruction(user, currentUserRole, availableCopies, activeLoans, readingList);
        string? replyText = null;
        string sourceMode = "Kho tri thức BookShare";
        int promptTokens = 185;
        var clientCommands = new List<AiClientCommandDto>();
        var actionCards = new List<AiActionCardDto>();

        // 3. Gọi Gemini 2.5 Flash với Multi-turn & Function Declarations (Timeout 20s)
        if (!string.IsNullOrWhiteSpace(apiKey) &&
            !apiKey.StartsWith("demo", StringComparison.OrdinalIgnoreCase) &&
            !apiKey.Contains("DIEN_KEY", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(20);
                var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

                var contents = new List<object>();
                foreach (var hist in recentHistory)
                {
                    contents.Add(new
                    {
                        role = hist.Sender == "Gemini" ? "model" : "user",
                        parts = new object[] { new { text = hist.MessageText } }
                    });
                }
                contents.Add(new
                {
                    role = "user",
                    parts = new object[] { new { text = cleanMsg } }
                });

                var payload = new
                {
                    system_instruction = new { parts = new[] { new { text = ragBuilder.ToString() } } },
                    contents,
                    tools = BuildGeminiToolsPayload(currentUserRole)
                };

                var httpRes = await client.PostAsync(
                    endpoint,
                    new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

                if (httpRes.IsSuccessStatusCode)
                {
                    var body = await httpRes.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(body);
                    ParseGeminiResponse(doc, currentUserRole, activeLoans, readingList, user,
                        out replyText, clientCommands, actionCards);
                    sourceMode = $"Google Gemini Copilot ({modelName})";
                }
            }
            catch
            {
                // Tự động chuyển sang bộ phân tích ý định & tri thức cục bộ nếu API ngoài không phản hồi
            }
        }

        // 4. Fallback phân tích ý định cục bộ (chạy khi không có API key hoặc Gemini chưa gắn đủ lệnh)
        AnalyzeLocalCopilotIntents(cleanMsg, currentUserRole, user, availableCopies, activeLoans, readingList, clientCommands, actionCards);

        if (string.IsNullOrWhiteSpace(replyText))
        {
            replyText = BuildSmartSqlRagFallback(cleanMsg, user, availableCopies, activeLoans, readingList, clientCommands, actionCards);
        }

        _db.AiChatMessages.Add(new AiChatMessage
        {
            SessionId = session.Id,
            UserId = currentUserId,
            Sender = "Gemini",
            MessageText = replyText,
            PromptTokens = promptTokens,
            ReferencedBookIdsJson = referencedJson,
            CreatedAt = DateTime.UtcNow
        });

        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new CopilotChatResultDto
        {
            Success = true,
            ReplyText = replyText,
            SessionId = session.Id,
            PromptTokens = promptTokens,
            SourceMode = sourceMode,
            ClientCommands = clientCommands,
            ActionCards = actionCards
        };
    }

    public async Task<(bool Success, string Message)> ExecuteCopilotActionAsync(
        string currentUserId,
        string currentUserRole,
        ExecuteCopilotActionRequestDto request)
    {
        string key = (request.ActionKey ?? string.Empty).Trim().ToLowerInvariant();
        var parameters = request.Parameters ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        switch (key)
        {
            case "renew_loan":
            {
                int loanId = ExtractIntArg(parameters, "loanId");
                if (loanId <= 0)
                {
                    return (false, "Không xác định được mã hợp đồng mượn cần gia hạn.");
                }
                var (ok, msg, _, _) = await _renewalService.RenewLoanAsync(currentUserId, currentUserRole, loanId);
                return (ok, msg);
            }

            case "update_reading_progress":
            {
                int progressId = ExtractIntArg(parameters, "progressId");
                int currentPage = ExtractIntArg(parameters, "currentPage");
                if (progressId <= 0)
                {
                    return (false, "Không xác định được sách trong danh sách theo dõi tiến độ đọc.");
                }
                var (ok, msg, _, _) = await _readingService.UpdateReadingProgressAsync(
                    currentUserId,
                    new UpdateReadingProgressRequestDto
                    {
                        ProgressId = progressId,
                        CurrentPage = Math.Max(0, currentPage)
                    });
                return (ok, msg);
            }

            case "send_my_reminder_email":
            {
                var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
                if (user == null || !AuthConfigHelper.IsValidEmailFormat(user.Email))
                {
                    return (false, "Bạn chưa có địa chỉ Email hợp lệ. Vui lòng vào Cài Đặt Tài Khoản (/S01AuthOtpGoogle) để điền Email trước khi gửi thư!");
                }

                var myLoans = await _db.Loans
                    .AsNoTracking()
                    .Include(l => l.LoanRequest).ThenInclude(lr => lr.BookCopy).ThenInclude(bc => bc.Book)
                    .Where(l => l.LoanRequest.BorrowerId == currentUserId && (l.Status == "Active" || l.Status == "Overdue"))
                    .ToListAsync();

                var htmlBody = new StringBuilder();
                htmlBody.Append($"<div style='font-family:Roboto,sans-serif;padding:18px;border:1px solid #cbd5e1;border-radius:12px;'>");
                htmlBody.Append($"<h3 style='color:#1e3a8a;'>Nhắc lịch mượn &amp; hạn trả sách — BookShare</h3>");
                htmlBody.Append($"<p>Xin chào <strong>{System.Net.WebUtility.HtmlEncode(user.FullName)}</strong>,</p>");
                if (myLoans.Any())
                {
                    htmlBody.Append("<ul>");
                    foreach (var l in myLoans)
                    {
                        htmlBody.Append($"<li><strong>{System.Net.WebUtility.HtmlEncode(l.LoanRequest.BookCopy.Book.Title)}</strong> ([{l.LoanRequest.BookCopy.Barcode}]) — Hạn trả: <strong>{l.DueDate:dd/MM/yyyy}</strong> ({l.Status})</li>");
                    }
                    htmlBody.Append("</ul>");
                }
                else
                {
                    htmlBody.Append("<p>Hiện tại bạn không có hợp đồng mượn sách nào đang mở.</p>");
                }
                htmlBody.Append("</div>");

                var (_, _, statusNote) = await _resendEmailService.SendNotificationEmailAsync(
                    user.Email!,
                    "[BookShare] Tóm tắt lịch hạn trả sách cá nhân",
                    htmlBody.ToString());

                _db.Notifications.Add(new Notification
                {
                    UserId = user.Id,
                    Title = "Tóm tắt lịch hạn trả sách qua Email",
                    Content = statusNote,
                    Type = "DueReminder",
                    IsRead = false,
                    EmailSentViaResend = true,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();

                return (true, $"Đã gửi email nhắc hạn trả cá nhân cho {user.Email}: {statusNote}");
            }

            case "send_chat_summary_email":
            {
                int sid = ExtractIntArg(parameters, "sessionId");
                var (ok, msg, _) = await SendChatSummaryEmailAsync(currentUserId, sid > 0 ? sid : null, null);
                return (ok, msg);
            }

            case "send_due_reminders_batch":
            {
                if (currentUserRole is not ("Librarian" or "Admin"))
                {
                    throw new UnauthorizedAccessException("Chỉ Thủ thư hoặc Quản trị viên mới được phép gửi chiến dịch email nhắc hạn hàng loạt.");
                }
                var (ok, msg, _) = await _notificationService.TriggerResendEmailCampaignAsync(
                    currentUserId,
                    new S18TriggerResendCampaignDto { CampaignType = "DueReminder" });
                return (ok, msg);
            }

            default:
                return (false, "Thao tác Copilot yêu cầu chưa được hỗ trợ.");
        }
    }

    public async Task<(bool Success, string Message)> DeleteSessionAsync(string currentUserId, int sessionId)
    {
        var session = await _db.AiChatSessions
            .Include(s => s.AiChatMessages)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
        {
            return (false, "Không tìm thấy phiên hội thoại AI cần xóa.");
        }

        if (session.UserId != currentUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xóa phiên hội thoại AI của độc giả khác.");
        }

        if (session.AiChatMessages.Any())
        {
            _db.AiChatMessages.RemoveRange(session.AiChatMessages);
        }

        _db.AiChatSessions.Remove(session);
        await _db.SaveChangesAsync();

        return (true, $"Đã xóa phiên hội thoại \"{session.Title}\" cùng toàn bộ lịch sử tin nhắn!");
    }

    public async Task<AiQuickHistoryResultDto> GetQuickWidgetHistoryAsync(string currentUserId, string currentUserRole)
    {
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return new AiQuickHistoryResultDto { Authenticated = false, RoleName = "Guest" };
        }

        var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (user == null)
        {
            return new AiQuickHistoryResultDto { Authenticated = false, RoleName = "Guest" };
        }

        string modelName = AuthConfigHelper.SanitizeEnvValue(_config["Gemini:Model"] ?? Environment.GetEnvironmentVariable("GEMINI_MODEL"));
        if (string.IsNullOrWhiteSpace(modelName)) modelName = "gemini-2.5-flash";

        var session = await _db.AiChatSessions
            .Where(s => s.UserId == currentUserId)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync();

        if (session == null)
        {
            session = new AiChatSession
            {
                UserId = currentUserId,
                Title = $"Hội thoại {ResolveRoleVietnamese(currentUserRole)}",
                ModelName = modelName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.AiChatSessions.Add(session);
            await _db.SaveChangesAsync();

            _db.AiChatMessages.Add(new AiChatMessage
            {
                SessionId = session.Id,
                UserId = currentUserId,
                Sender = "Gemini",
                MessageText = BuildRoleGreeting(user.FullName, currentUserRole),
                PromptTokens = 40,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        var messages = await _db.AiChatMessages
            .AsNoTracking()
            .Where(m => m.SessionId == session.Id && m.UserId == currentUserId)
            .OrderByDescending(m => m.Id)
            .Take(30)
            .OrderBy(m => m.Id)
            .Select(m => new S13ChatMessageDto
            {
                MessageId = m.Id,
                Sender = m.Sender,
                MessageText = m.MessageText,
                PromptTokens = m.PromptTokens,
                ReferencedBookIdsJson = m.ReferencedBookIdsJson,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();

        return new AiQuickHistoryResultDto
        {
            Authenticated = true,
            SessionId = session.Id,
            RoleName = currentUserRole,
            UserFullName = user.FullName,
            UserEmail = user.Email ?? string.Empty,
            HasValidEmail = AuthConfigHelper.IsValidEmailFormat(user.Email),
            Messages = messages
        };
    }

    public async Task<(bool Success, string Message, int SessionId, S13ChatMessageDto? Greeting)> ResetChatSessionAsync(
        string currentUserId,
        string currentUserRole,
        int? sessionId = null)
    {
        var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản người dùng.", 0, null);
        }

        AiChatSession? session = null;
        if (sessionId.HasValue && sessionId.Value > 0)
        {
            session = await _db.AiChatSessions
                .Include(s => s.AiChatMessages)
                .FirstOrDefaultAsync(s => s.Id == sessionId.Value);
            if (session != null && session.UserId != currentUserId)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền đặt lại phiên hội thoại của thành viên khác.");
            }
        }

        if (session == null)
        {
            session = await _db.AiChatSessions
                .Include(s => s.AiChatMessages)
                .Where(s => s.UserId == currentUserId)
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefaultAsync();
        }

        string modelName = AuthConfigHelper.SanitizeEnvValue(_config["Gemini:Model"] ?? Environment.GetEnvironmentVariable("GEMINI_MODEL"));
        if (string.IsNullOrWhiteSpace(modelName)) modelName = "gemini-2.5-flash";

        if (session == null)
        {
            session = new AiChatSession
            {
                UserId = currentUserId,
                Title = $"Hội thoại {ResolveRoleVietnamese(currentUserRole)}",
                ModelName = modelName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.AiChatSessions.Add(session);
            await _db.SaveChangesAsync();
        }
        else if (session.AiChatMessages.Any())
        {
            _db.AiChatMessages.RemoveRange(session.AiChatMessages);
        }

        var greeting = new AiChatMessage
        {
            SessionId = session.Id,
            UserId = currentUserId,
            Sender = "Gemini",
            MessageText = BuildRoleGreeting(user.FullName, currentUserRole),
            PromptTokens = 40,
            CreatedAt = DateTime.UtcNow
        };
        _db.AiChatMessages.Add(greeting);
        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var dto = new S13ChatMessageDto
        {
            MessageId = greeting.Id,
            Sender = greeting.Sender,
            MessageText = greeting.MessageText,
            PromptTokens = greeting.PromptTokens,
            CreatedAt = greeting.CreatedAt
        };

        return (true, "Đã làm mới cuộc trò chuyện Trợ lý AI sạch sẽ!", session.Id, dto);
    }

    public async Task<(bool Success, string Message, bool RequiresEmailSetup)> SendChatSummaryEmailAsync(
        string currentUserId,
        int? sessionId = null,
        string? customNote = null)
    {
        var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (user == null)
        {
            return (false, "Không tìm thấy tài khoản người dùng.", false);
        }

        if (!AuthConfigHelper.IsValidEmailFormat(user.Email))
        {
            return (false,
                "Tài khoản của bạn chưa có địa chỉ Email hợp lệ. Vui lòng vào Cài Đặt Tài Khoản để nhập Email trước khi gửi thư!",
                true);
        }

        var session = sessionId.HasValue && sessionId.Value > 0
            ? await _db.AiChatSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId.Value && s.UserId == currentUserId)
            : await _db.AiChatSessions.AsNoTracking().Where(s => s.UserId == currentUserId).OrderByDescending(s => s.UpdatedAt).FirstOrDefaultAsync();

        if (session == null)
        {
            return (false, "Chưa có nội dung hội thoại Trợ lý AI nào để gửi qua Email.", false);
        }

        var recentMsgs = await _db.AiChatMessages
            .AsNoTracking()
            .Where(m => m.SessionId == session.Id && m.UserId == currentUserId)
            .OrderByDescending(m => m.Id)
            .Take(10)
            .OrderBy(m => m.Id)
            .ToListAsync();

        if (!recentMsgs.Any())
        {
            return (false, "Phiên hội thoại hiện đang trống, chưa có nội dung để gửi Email.", false);
        }

        var html = new StringBuilder();
        html.Append("<div style='font-family:Roboto,Arial,sans-serif;padding:20px;border:1px solid #cbd5e1;border-radius:14px;max-width:680px;'>");
        html.Append($"<h3 style='color:#1d4ed8;margin-top:0;'>Bản ghi hội thoại Trợ lý AI BookShare</h3>");
        html.Append($"<p>Xin chào <strong>{System.Net.WebUtility.HtmlEncode(user.FullName)}</strong>,</p>");
        html.Append($"<p>Dưới đây là nội dung mới nhất từ phiên <strong>{System.Net.WebUtility.HtmlEncode(session.Title)}</strong>:</p>");
        if (!string.IsNullOrWhiteSpace(customNote))
        {
            html.Append($"<p style='background:#f8fafc;padding:10px;border-left:4px solid #2563eb;'><em>Ghi chú của bạn: {System.Net.WebUtility.HtmlEncode(customNote.Trim())}</em></p>");
        }
        foreach (var m in recentMsgs)
        {
            bool isBot = m.Sender.Equals("Gemini", StringComparison.OrdinalIgnoreCase);
            string bg = isBot ? "#eff6ff" : "#f1f5f9";
            string who = isBot ? "Trợ lý AI BookShare" : user.FullName;
            html.Append($"<div style='margin:10px 0;padding:10px 12px;border-radius:10px;background:{bg};'>");
            html.Append($"<div style='font-size:12px;color:#475569;margin-bottom:4px;'><strong>{System.Net.WebUtility.HtmlEncode(who)}</strong> • {m.CreatedAt:dd/MM/yyyy HH:mm}</div>");
            html.Append($"<div style='white-space:pre-line;color:#0f172a;'>{System.Net.WebUtility.HtmlEncode(m.MessageText)}</div>");
            html.Append("</div>");
        }
        html.Append("</div>");

        var (sentViaApi, _, statusNote) = await _resendEmailService.SendNotificationEmailAsync(
            user.Email!,
            $"[BookShare AI] Bản lưu hội thoại: {session.Title}",
            html.ToString());

        _db.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Title = $"Đã gửi tóm tắt chat AI tới {user.Email}",
            Content = statusNote,
            Type = "System",
            IsRead = false,
            EmailSentViaResend = sentViaApi,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return (true, $"Đã gửi bản lưu hội thoại Trợ lý AI tới địa chỉ {user.Email}! ({statusNote})", false);
    }

    private static string ResolveRoleVietnamese(string role) => role switch
    {
        "Admin" => "Quản Trị Viên",
        "Librarian" => "Thủ Thư",
        "Curator" => "Chủ Nhiệm CLB",
        _ => "Độc Giả"
    };

    private static string BuildRoleGreeting(string fullName, string role) => role switch
    {
        "Admin" => $"Xin chào Quản trị viên **{fullName}**! Mình là Trợ lý AI BookShare. Bạn có thể hỏi thống kê hệ thống, gửi email nhắc hạn hàng loạt, đổi phong cách giao diện (Nét vẽ nghệch ngoặc, Vàng mộng mơ, Hồng ấm áp...), chỉnh font/cỡ chữ hoặc gửi tóm tắt qua Email.",
        "Librarian" => $"Xin chào Thủ thư **{fullName}**! Mình có thể hỗ trợ tra cứu kho sách, gửi nhắc hạn trả sách, đổi kiểu giao diện/font/cỡ chữ hoặc gửi tóm tắt chat qua Email.",
        "Curator" => $"Xin chào Chủ nhiệm CLB **{fullName}**! Mình có thể hỗ trợ kiểm tra lịch giao nhận tại Trạm CLB, gợi ý sách, đổi giao diện/font/cỡ chữ hoặc gửi tóm tắt qua Email.",
        _ => $"Xin chào **{fullName}**! Mình là Trợ lý AI BookShare. Bạn có thể tìm sách mượn miễn phí 0đ, kiểm tra hạn trả, đổi giao diện (Nét vẽ nghệch ngoặc, Vàng mộng mơ, Hồng ấm áp, Tối/Sáng), đổi phông chữ/cỡ chữ hoặc gửi tóm tắt qua Email!"
    };

    private static StringBuilder BuildSystemInstruction(
        AspNetUser? user,
        string currentUserRole,
        List<BookCopy> availableCopies,
        List<Loan> activeLoans,
        List<ReadingProgress> readingList)
    {
        var ragBuilder = new StringBuilder();
        ragBuilder.AppendLine("Bạn là Trợ lý AI Copilot của BookShare — Nền tảng quản lý tủ sách cá nhân và chia sẻ sách giấy 0đ (TUYỆT ĐỐI KHÔNG BÁN SÁCH).");
        ragBuilder.AppendLine($"Độc giả: {user?.FullName ?? "Độc giả BookShare"} | Vai trò: {currentUserRole} | Điểm uy tín: {user?.TrustScore ?? 100}.");
        ragBuilder.AppendLine("Bản sao sách sẵn sàng:");
        foreach (var c in availableCopies)
        {
            ragBuilder.AppendLine($"- [{c.Barcode}] \"{c.Book.Title}\" — {c.Book.Author} ({c.Book.Category.Name}, {(c.OwnerType == "System" ? "Kho thư viện" : "Thành viên")}, {c.ShelfLocation})");
        }
        ragBuilder.AppendLine("Hợp đồng mượn hiện tại:");
        foreach (var l in activeLoans)
        {
            ragBuilder.AppendLine($"- LoanId={l.Id}: \"{l.LoanRequest.BookCopy.Book.Title}\" ([{l.LoanRequest.BookCopy.Barcode}]), Hạn: {l.DueDate:dd/MM/yyyy}, Đã gia hạn: {l.RenewCount}/2, Trạng thái: {l.Status}.");
        }
        ragBuilder.AppendLine("Tiến độ đọc hiện tại:");
        foreach (var rp in readingList)
        {
            ragBuilder.AppendLine($"- ProgressId={rp.Id}: \"{rp.Book.Title}\" ({rp.CurrentPage}/{rp.TotalPages} trang, {rp.Status}).");
        }
        ragBuilder.AppendLine("Nếu người dùng muốn đổi giao diện/điều hướng/tìm sách/gia hạn/cập nhật trang/gửi email nhắc hạn, hãy gọi function tool tương ứng và trả lời tiếng Việt ngắn gọn, chuyên nghiệp.");
        return ragBuilder;
    }

    private static object[] BuildGeminiToolsPayload(string currentUserRole)
    {
        var declarations = new List<object>
        {
            new
            {
                name = "set_theme",
                description = "Đổi giao diện Sáng (light) hoặc Tối (dark) theo yêu cầu người dùng.",
                parameters = new { type = "OBJECT", properties = new { theme = new { type = "STRING", description = "light hoặc dark" } }, required = new[] { "theme" } }
            },
            new
            {
                name = "navigate_to",
                description = "Mở một phân hệ trong hệ thống BookShare.",
                parameters = new { type = "OBJECT", properties = new { url = new { type = "STRING" }, label = new { type = "STRING" } }, required = new[] { "url" } }
            },
            new
            {
                name = "search_catalog",
                description = "Tìm kiếm sách giấy theo từ khóa trong kho cộng đồng & thư viện.",
                parameters = new { type = "OBJECT", properties = new { keyword = new { type = "STRING" } }, required = new[] { "keyword" } }
            },
            new
            {
                name = "propose_renew_loan",
                description = "Đề xuất thẻ xác nhận gia hạn hợp đồng mượn +7 ngày cho người dùng bấm đồng ý.",
                parameters = new { type = "OBJECT", properties = new { loanId = new { type = "INTEGER" } }, required = new[] { "loanId" } }
            },
            new
            {
                name = "propose_update_reading_progress",
                description = "Đề xuất thẻ xác nhận cập nhật trang sách đang đọc.",
                parameters = new { type = "OBJECT", properties = new { progressId = new { type = "INTEGER" }, currentPage = new { type = "INTEGER" } }, required = new[] { "progressId", "currentPage" } }
            },
            new
            {
                name = "propose_send_my_reminder_email",
                description = "Đề xuất thẻ xác nhận gửi email tóm tắt hạn trả tới chính người dùng đang hỏi."
            }
        };

        if (currentUserRole is "Librarian" or "Admin")
        {
            declarations.Add(new
            {
                name = "propose_send_due_reminders_batch",
                description = "Chỉ dành cho Thủ thư/Quản trị viên: đề xuất gửi email nhắc hạn trả hàng loạt."
            });
        }

        return new object[] { new { function_declarations = declarations } };
    }

    private static void ParseGeminiResponse(
        JsonDocument doc,
        string currentUserRole,
        List<Loan> activeLoans,
        List<ReadingProgress> readingList,
        AspNetUser? user,
        out string? replyText,
        List<AiClientCommandDto> clientCommands,
        List<AiActionCardDto> actionCards)
    {
        replyText = null;
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return;
        if (!candidates[0].TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts))
            return;

        var textParts = new List<string>();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.String)
            {
                var txt = textNode.GetString();
                if (!string.IsNullOrWhiteSpace(txt)) textParts.Add(txt);
            }

            if (part.TryGetProperty("functionCall", out var fc))
            {
                string fnName = fc.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? string.Empty) : string.Empty;
                var argsEl = fc.TryGetProperty("args", out var aEl) ? aEl : default;
                MapGeminiToolCall(fnName, argsEl, currentUserRole, activeLoans, readingList, user, clientCommands, actionCards);
            }
        }

        if (textParts.Count > 0)
        {
            replyText = string.Join("\n", textParts);
        }
    }

    private static void MapGeminiToolCall(
        string fnName,
        JsonElement argsEl,
        string currentUserRole,
        List<Loan> activeLoans,
        List<ReadingProgress> readingList,
        AspNetUser? user,
        List<AiClientCommandDto> clientCommands,
        List<AiActionCardDto> actionCards)
    {
        switch (fnName)
        {
            case "set_theme":
            {
                string theme = argsEl.ValueKind == JsonValueKind.Object && argsEl.TryGetProperty("theme", out var t)
                    && string.Equals(t.GetString(), "dark", StringComparison.OrdinalIgnoreCase)
                    ? "dark"
                    : "light";
                AddUniqueCommand(clientCommands, "set_theme", theme == "dark" ? "Bật chế độ tối" : "Chuyển giao diện sáng",
                    new() { ["theme"] = theme });
                break;
            }
            case "navigate_to":
            {
                string rawUrl = argsEl.ValueKind == JsonValueKind.Object && argsEl.TryGetProperty("url", out var u)
                    ? (u.GetString() ?? "/")
                    : "/";
                string baseRoute = rawUrl.Split('?', '#')[0];
                if (AllowedRoutes.Contains(baseRoute))
                {
                    string lbl = argsEl.ValueKind == JsonValueKind.Object && argsEl.TryGetProperty("label", out var l)
                        && !string.IsNullOrWhiteSpace(l.GetString())
                        ? l.GetString()!
                        : "Mở trang tương ứng";
                    AddUniqueCommand(clientCommands, "navigate_to", lbl, new() { ["url"] = rawUrl });
                }
                break;
            }
            case "search_catalog":
            {
                string kw = argsEl.ValueKind == JsonValueKind.Object && argsEl.TryGetProperty("keyword", out var k)
                    ? (k.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(kw))
                {
                    AddUniqueCommand(clientCommands, "search_catalog", $"Tìm sách \"{kw}\"",
                        new() { ["url"] = $"/S06SearchDiscovery?keyword={Uri.EscapeDataString(kw)}", ["keyword"] = kw });
                }
                break;
            }
            case "propose_renew_loan":
            {
                int loanId = argsEl.ValueKind == JsonValueKind.Object && argsEl.TryGetProperty("loanId", out var l) && l.TryGetInt32(out int lid)
                    ? lid
                    : (activeLoans.FirstOrDefault(x => x.Status == "Active" && x.RenewCount < 2)?.Id ?? 0);
                var matchedLoan = activeLoans.FirstOrDefault(x => x.Id == loanId);
                if (matchedLoan != null)
                {
                    AddRenewLoanCard(actionCards, matchedLoan);
                }
                break;
            }
            case "propose_update_reading_progress":
            {
                int progId = argsEl.ValueKind == JsonValueKind.Object && argsEl.TryGetProperty("progressId", out var p) && p.TryGetInt32(out int pid)
                    ? pid
                    : (readingList.FirstOrDefault()?.Id ?? 0);
                int curPage = argsEl.ValueKind == JsonValueKind.Object && argsEl.TryGetProperty("currentPage", out var cp) && cp.TryGetInt32(out int pageNum)
                    ? pageNum
                    : 0;
                var rp = readingList.FirstOrDefault(r => r.Id == progId);
                if (rp != null && curPage >= 0)
                {
                    AddUpdateProgressCard(actionCards, rp, curPage);
                }
                break;
            }
            case "propose_send_my_reminder_email":
                AddPersonalReminderCard(actionCards, user?.Email ?? string.Empty);
                break;
            case "propose_send_due_reminders_batch":
                if (currentUserRole is "Librarian" or "Admin")
                {
                    AddBatchReminderCard(actionCards);
                }
                break;
        }
    }

    private static void AnalyzeLocalCopilotIntents(
        string cleanMsg,
        string currentUserRole,
        AspNetUser? user,
        List<BookCopy> availableCopies,
        List<Loan> activeLoans,
        List<ReadingProgress> readingList,
        List<AiClientCommandDto> clientCommands,
        List<AiActionCardDto> actionCards)
    {
        var q = cleanMsg.ToLowerInvariant();

        if (q.Contains("chế độ tối") || q.Contains("nền tối") || q.Contains("dark mode") || q.Contains("giao diện tối") || q.Contains("tối đi"))
        {
            AddUniqueCommand(clientCommands, "set_theme", "Bật chế độ tối", new() { ["theme"] = "dark" });
        }
        else if (q.Contains("chế độ sáng") || q.Contains("nền sáng") || q.Contains("light mode") || q.Contains("giao diện sáng"))
        {
            AddUniqueCommand(clientCommands, "set_theme", "Chuyển giao diện sáng", new() { ["theme"] = "light" });
        }

        // Lệnh đổi Phong cách Bootstrap (Sketchy / Morph / Flatly / Cyborg / Standard)
        if (q.Contains("nghệch ngoặc") || q.Contains("sketchy") || q.Contains("vẽ tay") || q.Contains("phác thảo"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Bật phong cách Nét vẽ nghệch ngoặc", new() { ["style"] = "sketchy", ["font"] = "patrick" });
        }
        else if (q.Contains("kính mờ") || q.Contains("morph"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Bật phong cách Kính mờ Morph", new() { ["style"] = "morph" });
        }
        else if (q.Contains("flatly") || q.Contains("phẳng"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Bật phong cách Phẳng Flatly", new() { ["style"] = "flatly" });
        }
        else if (q.Contains("cyborg") || q.Contains("công nghệ"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Bật phong cách Đêm Cyborg", new() { ["style"] = "cyborg", ["theme"] = "dark" });
        }
        else if (q.Contains("style mặc định") || q.Contains("phong cách chuẩn") || q.Contains("giao diện mặc định"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Khôi phục phong cách Chuẩn", new() { ["style"] = "standard", ["font"] = "roboto" });
        }

        // Lệnh đổi Bảng màu (Vàng mộng mơ, Hồng ấm áp, Tím oải hương, Giấy cổ...)
        if (q.Contains("vàng mộng mơ") || q.Contains("màu vàng"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đổi màu Vàng Mộng Mơ", new() { ["palette"] = "gold" });
        }
        else if (q.Contains("hồng ấm áp") || q.Contains("màu hồng") || q.Contains("sakura"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đổi màu Hồng Ấm Áp", new() { ["palette"] = "rose" });
        }
        else if (q.Contains("oải hương") || q.Contains("lavender"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đổi màu Tím Oải Hương", new() { ["palette"] = "lavender" });
        }
        else if (q.Contains("giấy cổ") || q.Contains("sepia"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đổi màu Giấy Cổ Thư Viện", new() { ["palette"] = "sepia" });
        }
        else if (q.Contains("lục bảo") || q.Contains("xanh lá"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đổi màu Lục Bảo Thư Viện", new() { ["palette"] = "emerald" });
        }

        // Lệnh đổi Font chữ & Cỡ chữ
        if (q.Contains("font viết tay") || q.Contains("chữ viết tay") || q.Contains("patrick"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đổi font Viết tay Patrick Hand", new() { ["font"] = "patrick" });
        }
        else if (q.Contains("font có chân") || q.Contains("font sách") || q.Contains("lora"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đổi font Sách giấy Lora", new() { ["font"] = "lora" });
        }
        else if (q.Contains("be vietnam") || q.Contains("font hiện đại"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đổi font Be Vietnam Pro", new() { ["font"] = "bevietnam" });
        }
        else if (q.Contains("jetbrains") || q.Contains("font code") || q.Contains("đơn cách"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đổi font JetBrains Mono", new() { ["font"] = "jetbrains" });
        }

        if (q.Contains("chữ rất lớn") || q.Contains("cỡ chữ lớn nhất") || q.Contains("cỡ xl"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đặt cỡ chữ Rất lớn (20px)", new() { ["fontSize"] = "xl" });
        }
        else if (q.Contains("chữ lớn") || q.Contains("to chữ") || q.Contains("tăng cỡ chữ"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đặt cỡ chữ Lớn (18px)", new() { ["fontSize"] = "lg" });
        }
        else if (q.Contains("chữ nhỏ") || q.Contains("giảm cỡ chữ"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đặt cỡ chữ Nhỏ (14px)", new() { ["fontSize"] = "sm" });
        }
        else if (q.Contains("cỡ chữ chuẩn") || q.Contains("chữ mặc định"))
        {
            AddUniqueCommand(clientCommands, "set_ui_prefs", "Đặt cỡ chữ Tiêu chuẩn (16px)", new() { ["fontSize"] = "md" });
        }

        if (q.Contains("tủ sách") || q.Contains("kệ sách"))
        {
            AddUniqueCommand(clientCommands, "navigate_to", "Mở Tủ sách Cá nhân", new() { ["url"] = "/S02PersonalShelf" });
        }
        else if (q.Contains("mở tiến độ") || q.Contains("không gian đọc") || q.Contains("theo dõi đọc"))
        {
            AddUniqueCommand(clientCommands, "navigate_to", "Mở Tiến độ Đọc sách", new() { ["url"] = "/S03ReadingTracker" });
        }
        else if (q.Contains("mở thông báo") || q.Contains("trung tâm thông báo"))
        {
            AddUniqueCommand(clientCommands, "navigate_to", "Mở Trung tâm Thông báo", new() { ["url"] = "/S18NotificationHub" });
        }
        else if (q.Contains("hồ sơ") || q.Contains("qr"))
        {
            AddUniqueCommand(clientCommands, "navigate_to", "Mở Hồ sơ & Xác thực", new() { ["url"] = "/S01AuthOtpGoogle" });
        }

        var searchMatch = Regex.Match(cleanMsg, @"(?:tìm\s+sách|tìm\s+kiếm)\s+([^\.,!\?\n]{2,45})", RegexOptions.IgnoreCase);
        if (searchMatch.Success)
        {
            string kw = searchMatch.Groups[1].Value.Trim();
            AddUniqueCommand(clientCommands, "search_catalog", $"Tìm sách \"{kw}\"",
                new() { ["url"] = $"/S06SearchDiscovery?keyword={Uri.EscapeDataString(kw)}", ["keyword"] = kw });
        }

        if (q.Contains("gia hạn"))
        {
            var renewable = activeLoans.FirstOrDefault(l => l.Status == "Active" && l.RenewCount < 2);
            if (renewable != null)
            {
                AddRenewLoanCard(actionCards, renewable);
            }
        }

        if (q.Contains("cập nhật trang") || q.Contains("đọc tới trang") || q.Contains("đến trang"))
        {
            var pageMatch = Regex.Match(q, @"(?:trang\s*)(\d+)");
            if (pageMatch.Success && int.TryParse(pageMatch.Groups[1].Value, out int targetPage))
            {
                var targetProgress = readingList.FirstOrDefault(r => r.Status == "Reading") ?? readingList.FirstOrDefault();
                if (targetProgress != null)
                {
                    AddUpdateProgressCard(actionCards, targetProgress, Math.Clamp(targetPage, 0, targetProgress.TotalPages));
                }
            }
        }

        if ((q.Contains("hàng loạt") || q.Contains("tất cả độc giả") || q.Contains("toàn bộ độc giả"))
            && (q.Contains("email") || q.Contains("nhắc hạn")))
        {
            if (currentUserRole is "Librarian" or "Admin")
            {
                AddBatchReminderCard(actionCards);
            }
        }
        else if ((q.Contains("gửi mail") || q.Contains("gửi email") || q.Contains("email") || q.Contains("nhắc hạn cho tôi") || q.Contains("gửi nhắc")) && user != null)
        {
            if (q.Contains("tóm tắt") || q.Contains("hội thoại") || q.Contains("chat"))
            {
                if (!actionCards.Any(c => c.ActionKey == "send_chat_summary_email"))
                {
                    bool hasMail = AuthConfigHelper.IsValidEmailFormat(user.Email);
                    actionCards.Add(new AiActionCardDto
                    {
                        ActionKey = "send_chat_summary_email",
                        Title = hasMail ? "Gửi Bản Lưu Hội Thoại AI Qua Email" : "Cần Bổ Sung Email Trước Khi Gửi Thư",
                        Description = hasMail
                            ? $"Gửi nội dung hội thoại hiện tại tới địa chỉ {user.Email}."
                            : "Tài khoản chưa có Email nhận thư hợp lệ. Vui lòng cập nhật Email tại Cài Đặt Tài Khoản.",
                        ConfirmLabel = hasMail ? "Xác nhận gửi Email ngay" : "Kiểm tra & gửi Email",
                        Parameters = new Dictionary<string, JsonElement>
                        {
                            ["email"] = JsonSerializer.SerializeToElement(user.Email ?? string.Empty)
                        }
                    });
                }
            }
            else
            {
                AddPersonalReminderCard(actionCards, user.Email ?? string.Empty);
            }
        }
    }

    private static string BuildSmartSqlRagFallback(
        string question,
        AspNetUser? user,
        List<BookCopy> availableCopies,
        List<Loan> activeLoans,
        List<ReadingProgress> readingList,
        List<AiClientCommandDto> clientCommands,
        List<AiActionCardDto> actionCards)
    {
        var qLower = question.ToLowerInvariant();
        var sb = new StringBuilder();

        if (clientCommands.Any() || actionCards.Any())
        {
            sb.AppendLine($"🤖 Mình đã phân tích yêu cầu của {user?.FullName ?? "bạn"}:");
            foreach (var cmd in clientCommands)
            {
                sb.AppendLine($"• Thao tác giao diện: **{cmd.Label}**.");
            }
            foreach (var card in actionCards)
            {
                sb.AppendLine($"• Đề xuất hành động: **{card.Title}** — {card.Description} Bấm nút xác nhận bên dưới để thực hiện.");
            }
            return sb.ToString().TrimEnd();
        }

        if (qLower.Contains("hạn trả") || qLower.Contains("đang mượn") || qLower.Contains("gia hạn") || qLower.Contains("tiến độ"))
        {
            sb.AppendLine($"📊 [Thông tin mượn trả & tiến độ đọc của {user?.FullName ?? "Độc giả BookShare"}]:");
            if (activeLoans.Any())
            {
                sb.AppendLine($"• Hợp đồng đang mượn ({activeLoans.Count} cuốn):");
                foreach (var l in activeLoans)
                {
                    sb.AppendLine($"  - Hợp đồng #{l.Id}: \"{l.LoanRequest.BookCopy.Book.Title}\" (Mã vạch: {l.LoanRequest.BookCopy.Barcode}) — Hạn trả: {l.DueDate:dd/MM/yyyy} | Đã gia hạn: {l.RenewCount}/2 lần.");
                }
            }
            else
            {
                sb.AppendLine("• Hiện tại bạn không có hợp đồng mượn nào đang mở.");
            }

            if (readingList.Any())
            {
                sb.AppendLine("• Tiến độ đọc trong Không gian đọc:");
                foreach (var rp in readingList.Take(3))
                {
                    int pct = rp.TotalPages > 0 ? (int)Math.Round(rp.CurrentPage * 100.0 / rp.TotalPages) : 0;
                    sb.AppendLine($"  - \"{rp.Book.Title}\": Trang {rp.CurrentPage}/{rp.TotalPages} ({pct}% — {rp.Status}).");
                }
            }
            return sb.ToString();
        }

        if (qLower.Contains("p2p") || qLower.Contains("uy tín") || qLower.Contains("trustscore") || qLower.Contains("quy định"))
        {
            sb.AppendLine($"🤝 [Hướng dẫn Chia sẻ Cộng đồng & Điểm Uy Tín cho {user?.FullName ?? "bạn"}]:");
            sb.AppendLine($"• Điểm Uy tín hiện tại của bạn là {user?.TrustScore ?? 100} điểm (Hạn mức thưởng cho mượn: +{user?.LendQuotaBonus ?? 0} cuốn).");
            sb.AppendLine("• Khi chia sẻ sách qua Tủ sách cá nhân thành công, bạn được cộng +1 suất thưởng và +3 Điểm Uy tín (100% phi thương mại 0đ).");
            return sb.ToString();
        }

        sb.AppendLine("📚 [Gợi ý Sách Sẵn Sàng Cho Mượn Trong Kho BookShare]:");
        foreach (var copy in availableCopies.Take(4))
        {
            string sourceLabel = copy.OwnerType == "System" ? "Kho chung Thư viện" : "Tủ sách Cộng đồng";
            sb.AppendLine($"• [{copy.Barcode}] \"{copy.Book.Title}\" — Tác giả: {copy.Book.Author} ({copy.Book.Category.Name}, {copy.Book.PageCount} trang) | Nguồn: {sourceLabel} ({copy.ShelfLocation}).");
        }
        sb.Append("👉 Bạn có thể mở Cổng Tìm kiếm hoặc Mượn Kho Thư viện để gửi yêu cầu mượn miễn phí 0đ ngay!");
        return sb.ToString();
    }

    private static void AddUniqueCommand(List<AiClientCommandDto> list, string cmd, string label, Dictionary<string, string> args)
    {
        if (!list.Any(c => c.Command == cmd && c.Label == label))
        {
            list.Add(new AiClientCommandDto { Command = cmd, Label = label, Args = args });
        }
    }

    private static void AddRenewLoanCard(List<AiActionCardDto> cards, Loan loan)
    {
        if (cards.Any(c => c.ActionKey == "renew_loan")) return;
        cards.Add(new AiActionCardDto
        {
            ActionKey = "renew_loan",
            Title = $"Gia hạn hợp đồng #{loan.Id} (+7 ngày)",
            Description = $"Sách \"{loan.LoanRequest.BookCopy.Book.Title}\" — Hạn hiện tại {loan.DueDate:dd/MM/yyyy} ({loan.RenewCount}/2 lần gia hạn).",
            ConfirmLabel = "Xác nhận gia hạn (+7 ngày)",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["loanId"] = JsonSerializer.SerializeToElement(loan.Id),
                ["bookTitle"] = JsonSerializer.SerializeToElement(loan.LoanRequest.BookCopy.Book.Title)
            }
        });
    }

    private static void AddUpdateProgressCard(List<AiActionCardDto> cards, ReadingProgress rp, int currentPage)
    {
        if (cards.Any(c => c.ActionKey == "update_reading_progress")) return;
        cards.Add(new AiActionCardDto
        {
            ActionKey = "update_reading_progress",
            Title = $"Cập nhật trang đọc ({currentPage}/{rp.TotalPages})",
            Description = $"Cập nhật tiến độ cuốn \"{rp.Book.Title}\" từ trang {rp.CurrentPage} lên trang {currentPage}.",
            ConfirmLabel = "Xác nhận lưu trang đọc",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["progressId"] = JsonSerializer.SerializeToElement(rp.Id),
                ["currentPage"] = JsonSerializer.SerializeToElement(currentPage),
                ["bookTitle"] = JsonSerializer.SerializeToElement(rp.Book.Title)
            }
        });
    }

    private static void AddPersonalReminderCard(List<AiActionCardDto> cards, string email)
    {
        if (cards.Any(c => c.ActionKey == "send_my_reminder_email")) return;
        cards.Add(new AiActionCardDto
        {
            ActionKey = "send_my_reminder_email",
            Title = "Gửi Email Tóm Tắt Hạn Trả Cá Nhân",
            Description = $"Gửi thông tin lịch mượn và hạn trả hiện tại về địa chỉ {email}.",
            ConfirmLabel = "Xác nhận gửi Email cho tôi",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["email"] = JsonSerializer.SerializeToElement(email)
            }
        });
    }

    private static void AddBatchReminderCard(List<AiActionCardDto> cards)
    {
        if (cards.Any(c => c.ActionKey == "send_due_reminders_batch")) return;
        cards.Add(new AiActionCardDto
        {
            ActionKey = "send_due_reminders_batch",
            Title = "Gửi Email Nhắc Hạn Hàng Loạt",
            Description = "Kích hoạt chiến dịch gửi email nhắc hạn tới các độc giả sắp tới hạn trả sách.",
            ConfirmLabel = "Xác nhận phát chiến dịch",
            RequiresRole = "Librarian,Admin",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["campaignType"] = JsonSerializer.SerializeToElement("DueReminder")
            }
        });
    }

    private static int ExtractIntArg(Dictionary<string, JsonElement> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var el)) return 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int num)) return num;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out int parsed)) return parsed;
        return 0;
    }
}
