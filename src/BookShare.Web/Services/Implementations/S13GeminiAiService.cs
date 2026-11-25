using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

/// <summary>
/// [S13] Dịch vụ tích hợp Google Gemini API kèm chế độ dự phòng nội bộ thông minh: lưu lịch sử chat theo vai trò, phân tích lệnh đổi giao diện/font/cỡ chữ/nền và gửi 7 loại báo cáo Email qua Resend.
/// </summary>
/// <remarks>Bảo mật: Chỉ gửi báo cáo đúng phạm vi dữ liệu mà vai trò người dùng được phép xem.</remarks>
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
    private readonly IAntiSpamRateLimiterService _rateLimiter;

    public S13GeminiAiService(
        BookShareDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        IS03ReadingTrackerService readingService,
        IS12LoanRenewalFineService renewalService,
        IS18NotificationHubService notificationService,
        IResendEmailService resendEmailService,
        IAntiSpamRateLimiterService rateLimiter)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _readingService = readingService;
        _renewalService = renewalService;
        _notificationService = notificationService;
        _resendEmailService = resendEmailService;
        _rateLimiter = rateLimiter;
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
        var (allowed, retrySec, rlMsg) = _rateLimiter.TryConsume(
            "s13-create-session",
            currentUserId,
            maxRequests: 12,
            window: TimeSpan.FromMinutes(5),
            customMessage: "Bạn đang tạo quá nhiều phiên hội thoại AI liên tục (tối đa 12 phiên / 5 phút). Vui lòng dùng lại phiên đang có.");
        if (!allowed)
        {
            throw new RateLimitExceededException(rlMsg, retrySec);
        }

        int existingSessions = await _db.AiChatSessions.CountAsync(s => s.UserId == currentUserId);
        if (existingSessions >= 30)
        {
            return (false, "Bạn đã đạt tối đa 30 phiên hội thoại AI. Vui lòng xóa bớt phiên cũ trước khi tạo mới.", 0);
        }

        string cleanTitle = string.IsNullOrWhiteSpace(title)
            ? $"Phiên tư vấn AI ({DateTime.Now:dd/MM HH:mm})"
            : title.Trim();
        if (cleanTitle.Length > 140) cleanTitle = cleanTitle[..140];

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
        if (cleanMsg.Length > 2000)
        {
            return new CopilotChatResultDto
            {
                Success = false,
                ReplyText = "Nội dung câu hỏi gửi Trợ lý AI tối đa 2.000 ký tự.",
                SourceMode = "Validation"
            };
        }

        var (allowedMsg, retryMsgSec, rlMsgText) = _rateLimiter.TryConsume(
            "s13-send-message",
            currentUserId,
            maxRequests: 25,
            window: TimeSpan.FromMinutes(1),
            customMessage: "Bạn đang gửi tin nhắn tới Trợ lý AI quá nhanh (tối đa 25 lượt / phút). Vui lòng đợi giây lát.");
        if (!allowedMsg)
        {
            throw new RateLimitExceededException(rlMsgText, retryMsgSec);
        }
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
                EnforceAiEmailQuota(currentUserId);

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

            case "send_reading_progress_email":
            {
                var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
                if (user == null || !AuthConfigHelper.IsValidEmailFormat(user.Email))
                {
                    return (false, "Bạn chưa có địa chỉ Email hợp lệ. Vui lòng vào Cài Đặt Tài Khoản (/S01AuthOtpGoogle) để điền Email trước khi gửi thư!");
                }
                EnforceAiEmailQuota(currentUserId);

                var progresses = await _db.ReadingProgresses
                    .AsNoTracking()
                    .Include(rp => rp.Book)
                    .Include(rp => rp.BookNotes)
                    .Where(rp => rp.UserId == currentUserId)
                    .OrderByDescending(rp => rp.UpdatedAt)
                    .Take(10)
                    .ToListAsync();

                var html = new StringBuilder();
                html.Append("<div style='font-family:Roboto,Arial,sans-serif;padding:20px;border:1px solid #cbd5e1;border-radius:14px;max-width:680px;'>");
                html.Append("<h3 style='color:#059669;margin-top:0;'>📖 Báo Cáo Tiến Độ Đọc Sách &amp; Trích Dẫn — BookShare</h3>");
                html.Append($"<p>Xin chào <strong>{System.Net.WebUtility.HtmlEncode(user.FullName)}</strong>,</p>");
                if (progresses.Any())
                {
                    int totalPagesRead = progresses.Sum(p => p.CurrentPage);
                    int finishedCount = progresses.Count(p => p.Status == "Completed");
                    html.Append($"<p>Tổng quan: <strong>{progresses.Count} cuốn sách</strong> đang theo dõi • <strong>{finishedCount} cuốn đã hoàn thành</strong> • <strong>{totalPagesRead:N0} trang</strong> đã đọc.</p><ul>");
                    foreach (var rp in progresses)
                    {
                        int pct = rp.TotalPages > 0 ? (int)Math.Round(rp.CurrentPage * 100.0 / rp.TotalPages) : 0;
                        html.Append($"<li><strong>{System.Net.WebUtility.HtmlEncode(rp.Book.Title)}</strong> — Trang {rp.CurrentPage}/{rp.TotalPages} ({pct}% • {rp.Status})");
                        var firstQuote = rp.BookNotes.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n.QuoteText));
                        if (firstQuote != null)
                        {
                            html.Append($"<br/><em style='color:#475569;'>“{System.Net.WebUtility.HtmlEncode(firstQuote.QuoteText)}”</em>");
                        }
                        html.Append("</li>");
                    }
                    html.Append("</ul>");
                }
                else
                {
                    html.Append("<p>Bạn chưa thêm cuốn sách nào vào Không gian theo dõi đọc sách. Hãy bắt đầu tại mục Tiến Độ Đọc Sách trên BookShare!</p>");
                }
                html.Append("</div>");

                var (sentViaApi, _, statusNote) = await _resendEmailService.SendNotificationEmailAsync(
                    user.Email!,
                    "[BookShare AI] Báo cáo tiến độ đọc sách & trích dẫn cá nhân",
                    html.ToString());

                _db.Notifications.Add(new Notification
                {
                    UserId = user.Id,
                    Title = "Đã gửi báo cáo tiến độ đọc sách qua Email",
                    Content = statusNote,
                    Type = "System",
                    IsRead = false,
                    EmailSentViaResend = sentViaApi,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
                return (true, $"Đã gửi báo cáo tiến độ đọc sách & trích dẫn tới {user.Email}: {statusNote}");
            }

            case "send_book_recommendations_email":
            {
                var user = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
                if (user == null || !AuthConfigHelper.IsValidEmailFormat(user.Email))
                {
                    return (false, "Bạn chưa có địa chỉ Email hợp lệ. Vui lòng vào Cài Đặt Tài Khoản (/S01AuthOtpGoogle) để điền Email trước khi gửi thư!");
                }
                EnforceAiEmailQuota(currentUserId);

                string topic = ExtractStringArg(parameters, "topic");
                var copyQuery = _db.BookCopies
                    .AsNoTracking()
                    .Include(bc => bc.Book).ThenInclude(b => b.Category)
                    .Include(bc => bc.Library)
                    .Where(bc => bc.Status == "Available" && bc.IsLendable);

                var allAvail = await copyQuery.OrderBy(bc => bc.Id).Take(60).ToListAsync();
                var picks = string.IsNullOrWhiteSpace(topic)
                    ? allAvail.Take(6).ToList()
                    : allAvail.Where(bc =>
                        bc.Book.Title.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
                        bc.Book.Author.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
                        bc.Book.Category.Name.Contains(topic, StringComparison.OrdinalIgnoreCase)).Take(6).ToList();
                if (!picks.Any()) picks = allAvail.Take(5).ToList();

                var html = new StringBuilder();
                html.Append("<div style='font-family:Roboto,Arial,sans-serif;padding:20px;border:1px solid #cbd5e1;border-radius:14px;max-width:680px;'>");
                html.Append("<h3 style='color:#2563eb;margin-top:0;'>✨ Danh Sách Gợi Ý Sách Mượn Miễn Phí (0đ) — BookShare</h3>");
                html.Append($"<p>Xin chào <strong>{System.Net.WebUtility.HtmlEncode(user.FullName)}</strong>,</p>");
                if (!string.IsNullOrWhiteSpace(topic))
                {
                    html.Append($"<p>Dựa trên chủ đề bạn quan tâm (<strong>{System.Net.WebUtility.HtmlEncode(topic)}</strong>), Trợ lý AI đề xuất các bản sao sẵn sàng cho mượn ngay:</p>");
                }
                else
                {
                    html.Append("<p>Dưới đây là những đầu sách chất lượng đang sẵn sàng cho mượn miễn phí 0đ tại BookShare:</p>");
                }
                html.Append("<ul>");
                foreach (var c in picks)
                {
                    string src = c.OwnerType == "System" ? (c.Library?.Name ?? "Kho Thư viện") : "Tủ sách Cộng đồng P2P";
                    html.Append($"<li><strong>{System.Net.WebUtility.HtmlEncode(c.Book.Title)}</strong> — {System.Net.WebUtility.HtmlEncode(c.Book.Author)} ({System.Net.WebUtility.HtmlEncode(c.Book.Category.Name)}) • Mã <code>{c.Barcode}</code> [{System.Net.WebUtility.HtmlEncode(src)}]</li>");
                }
                html.Append("</ul></div>");

                var (sentViaApi, _, statusNote) = await _resendEmailService.SendNotificationEmailAsync(
                    user.Email!,
                    "[BookShare AI] Gợi ý đầu sách hay sẵn sàng mượn 0đ dành riêng cho bạn",
                    html.ToString());

                _db.Notifications.Add(new Notification
                {
                    UserId = user.Id,
                    Title = "Đã gửi thư gợi ý sách AI qua Email",
                    Content = statusNote,
                    Type = "System",
                    IsRead = false,
                    EmailSentViaResend = sentViaApi,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
                return (true, $"Đã gửi danh sách gợi ý {picks.Count} đầu sách sẵn sàng mượn tới {user.Email}: {statusNote}");
            }

            case "send_wallet_membership_email":
            {
                var user = await _db.AspNetUsers
                    .AsNoTracking()
                    .Include(u => u.Memberships)
                    .Include(u => u.UserWallet)
                    .FirstOrDefaultAsync(u => u.Id == currentUserId);
                if (user == null || !AuthConfigHelper.IsValidEmailFormat(user.Email))
                {
                    return (false, "Bạn chưa có địa chỉ Email hợp lệ. Vui lòng vào Cài Đặt Tài Khoản (/S01AuthOtpGoogle) để điền Email trước khi gửi thư!");
                }
                EnforceAiEmailQuota(currentUserId);

                var policy = MembershipTierCatalog.GetEffectivePolicy(user.Memberships, DateTime.UtcNow);
                var activeMem = user.Memberships.Where(m => m.IsActive).OrderByDescending(m => m.StartDate).FirstOrDefault();
                var recentTxs = await _db.Transactions
                    .AsNoTracking()
                    .Where(t => t.UserId == currentUserId)
                    .OrderByDescending(t => t.CreatedAt)
                    .Take(5)
                    .ToListAsync();

                var html = new StringBuilder();
                html.Append("<div style='font-family:Roboto,Arial,sans-serif;padding:20px;border:1px solid #cbd5e1;border-radius:14px;max-width:680px;'>");
                html.Append("<h3 style='color:#0d9488;margin-top:0;'>💎 Sao Kê Ví Tiền Ảo &amp; Gói Hội Viên — BookShare</h3>");
                html.Append($"<p>Xin chào <strong>{System.Net.WebUtility.HtmlEncode(user.FullName)}</strong>,</p>");
                html.Append($"<p>• Hạng Hội viên hiện tại: <strong>{System.Net.WebUtility.HtmlEncode(policy.DisplayName)} ({policy.Tier})</strong> — Tối đa {policy.MaxConcurrentLoans + user.LendQuotaBonus} cuốn (Gốc {policy.MaxConcurrentLoans} + Thưởng P2P {user.LendQuotaBonus}), tối đa {policy.MaxBorrowDays} ngày/lượt.<br/>");
                html.Append($"• Số dư Ví tiền ảo miễn phí: <strong>{(user.UserWallet?.Balance ?? 0m):N0}đ ảo</strong> • Điểm Uy tín TrustScore: <strong>{user.TrustScore} điểm</strong>.<br/>");
                html.Append($"• Hiệu lực gói: {(activeMem?.ExpiryDate.HasValue == true ? activeMem.ExpiryDate.Value.ToString("dd/MM/yyyy") : "Không giới hạn")}</p>");
                if (recentTxs.Any())
                {
                    html.Append("<p><strong>5 giao dịch gần nhất:</strong></p><ul>");
                    foreach (var tx in recentTxs)
                    {
                        html.Append($"<li>#TX-{tx.Id} • {tx.TransactionType} • <strong>{tx.Amount:N0}đ ảo</strong> [{tx.PaymentStatus}] — {tx.CreatedAt:dd/MM/yyyy HH:mm}</li>");
                    }
                    html.Append("</ul>");
                }
                html.Append("</div>");

                var (sentViaApi, _, statusNote) = await _resendEmailService.SendNotificationEmailAsync(
                    user.Email!,
                    $"[BookShare AI] Sao kê Ví tiền ảo & Hạng hội viên {policy.DisplayName}",
                    html.ToString());

                _db.Notifications.Add(new Notification
                {
                    UserId = user.Id,
                    Title = "Đã gửi sao kê Ví & Hội viên qua Email",
                    Content = statusNote,
                    Type = "System",
                    IsRead = false,
                    EmailSentViaResend = sentViaApi,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
                return (true, $"Đã gửi sao kê Ví tiền ảo & Hạng hội viên tới {user.Email}: {statusNote}");
            }

            case "send_club_announcement_email":
            {
                bool isAdmin = string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
                bool isCurator = string.Equals(currentUserRole, "Curator", StringComparison.OrdinalIgnoreCase);
                int requestedHubId = ExtractIntArg(parameters, "hubId");

                var managedClubIds = await _db.ClubMembers
                    .AsNoTracking()
                    .Where(m => m.UserId == currentUserId && m.Status == "Approved" && (m.MemberRole == "Leader" || m.MemberRole == "ViceLeader"))
                    .Select(m => m.HubId)
                    .ToListAsync();

                if (!isAdmin && !isCurator && !managedClubIds.Any())
                {
                    throw new UnauthorizedAccessException("Chỉ Quản trị viên, Chủ nhiệm CLB hoặc Trưởng/Phó nhóm CLB mới được gửi thư mời / thông báo sinh hoạt tới thành viên CLB.");
                }

                Library? targetClub = null;
                if (requestedHubId > 0)
                {
                    targetClub = await _db.Libraries.AsNoTracking().FirstOrDefaultAsync(l => l.Id == requestedHubId);
                    if (targetClub == null)
                    {
                        return (false, "Không tìm thấy Câu lạc bộ được chỉ định để gửi thông báo.");
                    }
                    if (!isAdmin && targetClub.ManagerUserId != currentUserId && !managedClubIds.Contains(targetClub.Id))
                    {
                        throw new UnauthorizedAccessException("Bạn không phải Trưởng/Phó nhóm của Câu lạc bộ này.");
                    }
                }
                else
                {
                    targetClub = await _db.Libraries.AsNoTracking()
                        .Where(l => isAdmin || l.ManagerUserId == currentUserId || managedClubIds.Contains(l.Id))
                        .OrderByDescending(l => l.HubType == "ClubStation")
                        .FirstOrDefaultAsync();
                }

                if (targetClub == null)
                {
                    return (false, "Bạn chưa phụ trách Câu lạc bộ / Điểm trạm nào để gửi thông báo.");
                }
                EnforceAiEmailQuota(currentUserId);

                var sender = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
                string customTitle = ExtractStringArg(parameters, "title");
                string customContent = ExtractStringArg(parameters, "content");
                string mailTitle = string.IsNullOrWhiteSpace(customTitle)
                    ? $"[BookShare CLB] Thông báo sinh hoạt định kỳ — {targetClub.Name}"
                    : $"[BookShare CLB] {customTitle}";
                string mailContent = string.IsNullOrWhiteSpace(customContent)
                    ? $"Thân mời các thành viên tham gia buổi sinh hoạt đọc sách và giao lưu trao đổi sách giấy 0đ tại {targetClub.Name} ({targetClub.Address} • {targetClub.OpenHours})."
                    : customContent;

                var recipients = await _db.ClubMembers
                    .AsNoTracking()
                    .Include(m => m.User)
                    .Where(m => m.HubId == targetClub.Id && m.Status == "Approved")
                    .Select(m => m.User)
                    .ToListAsync();

                if (!recipients.Any() && sender != null)
                {
                    recipients.Add(sender);
                }

                int sentCount = 0;
                foreach (var r in recipients.Where(u => AuthConfigHelper.IsValidEmailFormat(u.Email)).Take(25))
                {
                    var body = $"<div style='font-family:Roboto,Arial,sans-serif;padding:20px;border:1px solid #cbd5e1;border-radius:14px;'>" +
                               $"<h3 style='color:#d97706;margin-top:0;'>⛺ {System.Net.WebUtility.HtmlEncode(targetClub.Name)}</h3>" +
                               $"<p>Xin chào <strong>{System.Net.WebUtility.HtmlEncode(r.FullName)}</strong>,</p>" +
                               $"<p>{System.Net.WebUtility.HtmlEncode(mailContent)}</p>" +
                               $"<p style='font-size:12px;color:#64748b;'>Người gửi: {System.Net.WebUtility.HtmlEncode(sender?.FullName ?? "Trưởng nhóm CLB")}</p></div>";
                    var (apiSent, _, _) = await _resendEmailService.SendNotificationEmailAsync(r.Email!, mailTitle, body);
                    _db.Notifications.Add(new Notification
                    {
                        UserId = r.Id,
                        Title = mailTitle,
                        Content = mailContent,
                        Type = "System",
                        IsRead = false,
                        EmailSentViaResend = apiSent,
                        CreatedAt = DateTime.UtcNow
                    });
                    sentCount++;
                }

                if (sentCount == 0)
                {
                    return (false, $"Chưa có thành viên nào trong CLB \"{targetClub.Name}\" cập nhật địa chỉ Email hợp lệ để nhận thư thông báo.");
                }

                await _db.SaveChangesAsync();
                return (true, $"Đã phát thư thông báo sinh hoạt CLB \"{targetClub.Name}\" tới {sentCount} thành viên có Email hợp lệ!");
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

            case "send_admin_analytics_report_email":
            {
                if (!string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("Chỉ Quản trị viên hệ thống mới được phép gửi báo cáo thống kê vận hành toàn hệ thống qua Email.");
                }

                var adminUser = await _db.AspNetUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId);
                if (adminUser == null || !AuthConfigHelper.IsValidEmailFormat(adminUser.Email))
                {
                    return (false, "Tài khoản Quản trị viên chưa có địa chỉ Email hợp lệ. Vui lòng cập nhật tại Cài Đặt Tài Khoản.");
                }
                EnforceAiEmailQuota(currentUserId);

                int totalUsers = await _db.AspNetUsers.CountAsync();
                int totalBooks = await _db.Books.CountAsync();
                int totalCopies = await _db.BookCopies.CountAsync();
                int activeLoansCount = await _db.Loans.CountAsync(l => l.Status == "Active" || l.Status == "Overdue");
                int overdueCount = await _db.Loans.CountAsync(l => l.Status == "Overdue" || (l.Status == "Active" && l.DueDate < DateTime.UtcNow));
                int openDisputes = await _db.Disputes.CountAsync(d => d.Status == "Open" || d.Status == "UnderReview");
                int clubsCount = await _db.Libraries.CountAsync(l => l.HubType == "ClubStation" || l.HubType == "DropoffHub");
                decimal membershipRev = await _db.Transactions
                    .Where(t => (t.TransactionType == "Membership" || t.TransactionType == "MembershipSwitch") && t.PaymentStatus == "Completed")
                    .SumAsync(t => (decimal?)t.Amount) ?? 0m;

                var html = $@"<div style='font-family:Roboto,Arial,sans-serif;padding:20px;border:1px solid #cbd5e1;border-radius:14px;max-width:680px;'>
                    <h3 style='color:#dc2626;margin-top:0;'>📊 Báo Cáo Tổng Quan Vận Hành Hệ Thống BookShare</h3>
                    <p>Kính gửi Quản trị viên <strong>{System.Net.WebUtility.HtmlEncode(adminUser.FullName)}</strong> ({DateTime.Now:dd/MM/yyyy HH:mm}),</p>
                    <ul>
                        <li>Tổng thành viên hệ thống: <strong>{totalUsers}</strong></li>
                        <li>Kho tri thức: <strong>{totalBooks} đầu sách</strong> / <strong>{totalCopies} bản sao vật lý</strong></li>
                        <li>Số Câu lạc bộ &amp; Điểm trạm cộng đồng: <strong>{clubsCount}</strong></li>
                        <li>Hợp đồng đang mượn: <strong>{activeLoansCount}</strong> (trong đó quá hạn: <strong style='color:#dc2626;'>{overdueCount}</strong>)</li>
                        <li>Hồ sơ tranh chấp đang mở: <strong>{openDisputes}</strong></li>
                        <li>Tổng giá trị đăng ký gói Hội viên tiền ảo: <strong>{membershipRev:N0}đ ảo</strong></li>
                    </ul>
                </div>";

                var (sentViaApi, _, statusNote) = await _resendEmailService.SendNotificationEmailAsync(
                    adminUser.Email!,
                    $"[BookShare Admin] Báo cáo thống kê vận hành hệ thống ({DateTime.Now:dd/MM/yyyy})",
                    html);

                _db.Notifications.Add(new Notification
                {
                    UserId = adminUser.Id,
                    Title = "Đã gửi báo cáo thống kê vận hành Admin qua Email",
                    Content = statusNote,
                    Type = "System",
                    IsRead = false,
                    EmailSentViaResend = sentViaApi,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
                return (true, $"Đã gửi báo cáo thống kê vận hành hệ thống tới Quản trị viên ({adminUser.Email}): {statusNote}");
            }

            default:
                return (false, "Thao tác Copilot yêu cầu chưa được hỗ trợ.");
        }
    }

    private void EnforceAiEmailQuota(string userId)
    {
        var (allowed, retrySec, rlMsg) = _rateLimiter.TryConsume(
            "s13-ai-email",
            userId,
            maxRequests: 8,
            window: TimeSpan.FromMinutes(5),
            customMessage: "Bạn đã gửi quá nhiều Email qua Trợ lý AI trong 5 phút (tối đa 8 thư / 5 phút). Vui lòng đợi giây lát để chống spam hộp thư.");
        if (!allowed)
        {
            throw new RateLimitExceededException(rlMsg, retrySec);
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

        EnforceAiEmailQuota(currentUserId);

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
        ragBuilder.AppendLine("Bạn có thể tùy biến toàn diện giao diện BookShare bằng tool `set_ui_prefs`:");
        ragBuilder.AppendLine("- theme: light (sáng) | dark (tối)");
        ragBuilder.AppendLine("- style (15 phong cách Bootstrap): standard, sketchy (Nét vẽ nghệch ngoặc), morph (Kính mờ), flatly (Phẳng), paper (Trang sách), neobrutal (Truyện tranh), pastel (Kẹo ngọt), lux (Sang trọng tối giản), zephyr (Hiện đại thoáng sạch), quartz (Pha lê trong suốt), journal (Tạp chí biên tập), minty (Bạc hà trẻ trung), vapor (Đêm neon), cyborg (Đêm công nghệ), terminal (Lập trình viên)");
        ragBuilder.AppendLine("- palette (12 bảng màu): blue, ocean, emerald, mint, gold (Vàng mộng mơ), rose (Hồng ấm áp), sakura, lavender, sepia, coral, royal, midnight");
        ragBuilder.AppendLine("- background (12 loại nền trang): solid, paper-grain (giấy sách), dot-grid (lưới chấm sổ tay), graph-grid (kẻ ô kỹ thuật), notebook-lines (vở kẻ ngang), soft-aurora (cực quang), sunset-glow (hoàng hôn), botanical (vườn xanh), starry-night (trời sao), warm-linen (vải bố), pastel-clouds (mây pastel), geometric-prism (hình học)");
        ragBuilder.AppendLine("- font (5 phông tiếng Việt): roboto, bevietnam, lora, patrick, jetbrains");
        ragBuilder.AppendLine("- fontSize (7 cỡ chữ): xs (13px), sm (14px), md (16px), lg (17.5px), xl (19px), 2xl (20.5px), 3xl (22px)");
        ragBuilder.AppendLine("Bạn cũng hỗ trợ đề xuất gửi 8 loại Email: nhắc hạn cá nhân, tóm tắt hội thoại AI, báo cáo tiến độ đọc & trích dẫn, gợi ý sách sẵn có, sao kê Ví & Gói hội viên, thư mời sinh hoạt CLB (Curator/Admin), nhắc hạn hàng loạt (Librarian/Admin), và báo cáo thống kê Admin (Admin).");
        ragBuilder.AppendLine("Khi người dùng yêu cầu đổi giao diện, loại nền, phông chữ, cỡ chữ hoặc gửi Email, PHẢI gọi tool tương ứng và trả lời tiếng Việt ngắn gọn, thân thiện.");
        return ragBuilder;
    }

    private static object[] BuildGeminiToolsPayload(string currentUserRole)
    {
        var declarations = new List<object>
        {
            new
            {
                name = "set_ui_prefs",
                description = "Đổi toàn diện giao diện người dùng BookShare: chế độ sáng/tối (theme), 15 phong cách Bootstrap (style), 12 bảng màu (palette), 12 loại nền trang (background), 5 phông chữ (font), và 7 cỡ chữ (fontSize).",
                parameters = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        theme = new { type = "STRING", description = "light hoặc dark" },
                        style = new { type = "STRING", description = "standard | sketchy | morph | flatly | paper | neobrutal | pastel | lux | zephyr | quartz | journal | minty | vapor | cyborg | terminal" },
                        palette = new { type = "STRING", description = "blue | ocean | emerald | mint | gold | rose | sakura | lavender | sepia | coral | royal | midnight" },
                        background = new { type = "STRING", description = "solid | paper-grain | dot-grid | graph-grid | notebook-lines | soft-aurora | sunset-glow | botanical | starry-night | warm-linen | pastel-clouds | geometric-prism" },
                        font = new { type = "STRING", description = "roboto | bevietnam | lora | patrick | jetbrains" },
                        fontSize = new { type = "STRING", description = "xs | sm | md | lg | xl | 2xl | 3xl" }
                    }
                }
            },
            new
            {
                name = "set_theme",
                description = "Đổi nhanh giao diện Sáng (light) hoặc Tối (dark) theo yêu cầu người dùng.",
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
                description = "Đề xuất thẻ xác nhận gửi email tóm tắt lịch mượn & hạn trả cá nhân."
            },
            new
            {
                name = "propose_send_reading_progress_email",
                description = "Đề xuất thẻ xác nhận gửi email báo cáo tiến độ đọc sách & trích dẫn cá nhân."
            },
            new
            {
                name = "propose_send_book_recommendations_email",
                description = "Đề xuất thẻ xác nhận gửi email danh sách gợi ý sách sẵn sàng cho mượn theo chủ đề.",
                parameters = new { type = "OBJECT", properties = new { topic = new { type = "STRING", description = "Chủ đề hoặc thể loại sách (tùy chọn)" } } }
            },
            new
            {
                name = "propose_send_wallet_membership_email",
                description = "Đề xuất thẻ xác nhận gửi email sao kê Ví tiền ảo, Hạng hội viên 4 cấp & giao dịch gần nhất."
            },
            new
            {
                name = "propose_send_chat_summary_email",
                description = "Đề xuất thẻ xác nhận gửi bản lưu hội thoại AI hiện tại qua Email."
            }
        };

        if (currentUserRole is "Curator" or "Admin")
        {
            declarations.Add(new
            {
                name = "propose_send_club_announcement_email",
                description = "Chỉ dành cho Trưởng nhóm CLB/Curator/Admin: đề xuất gửi thư mời / thông báo sinh hoạt CLB tới thành viên CLB.",
                parameters = new { type = "OBJECT", properties = new { title = new { type = "STRING" }, content = new { type = "STRING" } } }
            });
        }

        if (currentUserRole is "Librarian" or "Admin")
        {
            declarations.Add(new
            {
                name = "propose_send_due_reminders_batch",
                description = "Chỉ dành cho Thủ thư/Quản trị viên: đề xuất gửi email nhắc hạn trả hàng loạt."
            });
        }

        if (currentUserRole == "Admin")
        {
            declarations.Add(new
            {
                name = "propose_send_admin_analytics_report_email",
                description = "Chỉ dành cho Quản trị viên: đề xuất gửi email báo cáo thống kê vận hành toàn hệ thống."
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
            case "set_ui_prefs":
            {
                if (argsEl.ValueKind == JsonValueKind.Object)
                {
                    var allowedStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "standard", "sketchy", "morph", "flatly", "paper", "neobrutal", "pastel", "cyborg", "terminal",
                        "lux", "zephyr", "vapor", "quartz", "journal", "minty"
                    };
                    var allowedPalettes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "blue", "ocean", "emerald", "mint", "gold", "rose", "sakura", "lavender", "sepia", "coral", "royal", "midnight"
                    };
                    var allowedBackgrounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "solid", "paper-grain", "dot-grid", "graph-grid", "notebook-lines",
                        "soft-aurora", "sunset-glow", "botanical", "starry-night",
                        "warm-linen", "pastel-clouds", "geometric-prism"
                    };
                    var allowedFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "roboto", "bevietnam", "lora", "patrick", "jetbrains" };
                    var allowedSizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "xs", "sm", "md", "lg", "xl", "2xl", "3xl" };

                    var prefs = new Dictionary<string, string>();
                    var labelParts = new List<string>();

                    if (argsEl.TryGetProperty("theme", out var thEl) && thEl.ValueKind == JsonValueKind.String)
                    {
                        var th = thEl.GetString()?.Trim().ToLowerInvariant();
                        if (th is "light" or "dark")
                        {
                            prefs["theme"] = th;
                            labelParts.Add(th == "dark" ? "Nền tối" : "Nền sáng");
                        }
                    }
                    if (argsEl.TryGetProperty("style", out var stEl) && stEl.ValueKind == JsonValueKind.String)
                    {
                        var st = stEl.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
                        if (allowedStyles.Contains(st))
                        {
                            prefs["style"] = st;
                            labelParts.Add($"Style {st}");
                        }
                    }
                    if (argsEl.TryGetProperty("palette", out var plEl) && plEl.ValueKind == JsonValueKind.String)
                    {
                        var pl = plEl.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
                        if (allowedPalettes.Contains(pl))
                        {
                            prefs["palette"] = pl;
                            labelParts.Add($"Màu {pl}");
                        }
                    }
                    if (argsEl.TryGetProperty("background", out var bgEl) && bgEl.ValueKind == JsonValueKind.String)
                    {
                        var bg = bgEl.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
                        if (allowedBackgrounds.Contains(bg))
                        {
                            prefs["background"] = bg;
                            labelParts.Add($"Nền {bg}");
                        }
                    }
                    if (argsEl.TryGetProperty("font", out var fnEl) && fnEl.ValueKind == JsonValueKind.String)
                    {
                        var fn = fnEl.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
                        if (allowedFonts.Contains(fn))
                        {
                            prefs["font"] = fn;
                            labelParts.Add($"Font {fn}");
                        }
                    }
                    if (argsEl.TryGetProperty("fontSize", out var szEl) && szEl.ValueKind == JsonValueKind.String)
                    {
                        var sz = szEl.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
                        if (allowedSizes.Contains(sz))
                        {
                            prefs["fontSize"] = sz;
                            labelParts.Add($"Cỡ {sz}");
                        }
                    }

                    if (prefs.Count > 0)
                    {
                        AddUniqueCommand(clientCommands, "set_ui_prefs", $"Đổi giao diện: {string.Join(" • ", labelParts)}", prefs);
                    }
                }
                break;
            }
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
            case "propose_send_reading_progress_email":
                AddReadingProgressEmailCard(actionCards, user?.Email ?? string.Empty);
                break;
            case "propose_send_book_recommendations_email":
            {
                string topic = argsEl.ValueKind == JsonValueKind.Object && argsEl.TryGetProperty("topic", out var tp)
                    ? (tp.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                AddBookRecommendationEmailCard(actionCards, user?.Email ?? string.Empty, topic);
                break;
            }
            case "propose_send_wallet_membership_email":
                AddWalletMembershipEmailCard(actionCards, user?.Email ?? string.Empty);
                break;
            case "propose_send_chat_summary_email":
                AddChatSummaryEmailCard(actionCards, user?.Email ?? string.Empty);
                break;
            case "propose_send_club_announcement_email":
                if (currentUserRole is "Curator" or "Admin")
                {
                    string title = argsEl.ValueKind == JsonValueKind.Object && argsEl.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? string.Empty) : string.Empty;
                    string content = argsEl.ValueKind == JsonValueKind.Object && argsEl.TryGetProperty("content", out var cEl) ? (cEl.GetString() ?? string.Empty) : string.Empty;
                    AddClubAnnouncementEmailCard(actionCards, title, content);
                }
                break;
            case "propose_send_due_reminders_batch":
                if (currentUserRole is "Librarian" or "Admin")
                {
                    AddBatchReminderCard(actionCards);
                }
                break;
            case "propose_send_admin_analytics_report_email":
                if (currentUserRole == "Admin")
                {
                    AddAdminAnalyticsEmailCard(actionCards, user?.Email ?? string.Empty);
                }
                break;
        }
    }

    public static List<AiClientCommandDto> ExtractUiClientCommands(string cleanMsg)
    {
        var cmds = new List<AiClientCommandDto>();
        if (string.IsNullOrWhiteSpace(cleanMsg)) return cmds;
        var q = cleanMsg.ToLowerInvariant();

        if (q.Contains("chế độ tối") || q.Contains("nền tối") || q.Contains("dark mode") || q.Contains("giao diện tối") || q.Contains("tối đi"))
        {
            AddUniqueCommand(cmds, "set_theme", "Bật chế độ tối", new() { ["theme"] = "dark" });
        }
        else if (q.Contains("chế độ sáng") || q.Contains("nền sáng") || q.Contains("light mode") || q.Contains("giao diện sáng"))
        {
            AddUniqueCommand(cmds, "set_theme", "Chuyển giao diện sáng", new() { ["theme"] = "light" });
        }

        // Lệnh đổi Phong cách Giao diện (9 kiểu phong phú)
        if (q.Contains("nghệch ngoặc") || q.Contains("sketchy") || q.Contains("vẽ tay") || q.Contains("phác thảo"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Nét vẽ nghệch ngoặc", new() { ["style"] = "sketchy", ["font"] = "patrick" });
        }
        else if (q.Contains("kính mờ") || q.Contains("morph") || q.Contains("thủy tinh"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Kính mờ Morph", new() { ["style"] = "morph" });
        }
        else if (q.Contains("flatly") || q.Contains("phẳng"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Phẳng Flatly", new() { ["style"] = "flatly" });
        }
        else if (q.Contains("trang sách") || q.Contains("cổ điển") || q.Contains("hoài cổ") || q.Contains("paper"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Trang sách cổ điển", new() { ["style"] = "paper", ["font"] = "lora", ["palette"] = "sepia" });
        }
        else if (q.Contains("truyện tranh") || q.Contains("neobrutal") || q.Contains("brutal") || q.Contains("pop art"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Truyện tranh Pop", new() { ["style"] = "neobrutal" });
        }
        else if (q.Contains("kẹo ngọt") || q.Contains("pastel") || q.Contains("dịu mắt") || q.Contains("dễ thương"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Kẹo ngọt Pastel", new() { ["style"] = "pastel", ["palette"] = "sakura" });
        }
        else if (q.Contains("terminal") || q.Contains("lập trình viên") || q.Contains("hacker") || q.Contains("matrix"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Lập trình viên Terminal", new() { ["style"] = "terminal", ["font"] = "jetbrains", ["theme"] = "dark" });
        }
        else if (q.Contains("lux") || q.Contains("sang trọng") || q.Contains("tối giản cao cấp"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Sang trọng Lux", new() { ["style"] = "lux" });
        }
        else if (q.Contains("zephyr") || q.Contains("thoáng sạch"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Hiện đại Zephyr", new() { ["style"] = "zephyr" });
        }
        else if (q.Contains("vapor") || q.Contains("neon") || q.Contains("synthwave"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Đêm Neon Vapor", new() { ["style"] = "vapor", ["theme"] = "dark" });
        }
        else if (q.Contains("quartz") || q.Contains("pha lê"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Pha lê Quartz", new() { ["style"] = "quartz" });
        }
        else if (q.Contains("journal") || q.Contains("tạp chí") || q.Contains("biên tập"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Tạp chí Journal", new() { ["style"] = "journal", ["font"] = "lora" });
        }
        else if (q.Contains("minty") || q.Contains("phong cách bạc hà"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Bạc hà Minty", new() { ["style"] = "minty", ["palette"] = "mint" });
        }
        else if (q.Contains("cyborg") || q.Contains("công nghệ"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Bật phong cách Đêm Cyborg", new() { ["style"] = "cyborg", ["theme"] = "dark" });
        }
        else if (q.Contains("style mặc định") || q.Contains("phong cách chuẩn") || q.Contains("giao diện mặc định") || q.Contains("khôi phục giao diện"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Khôi phục giao diện Chuẩn Hiện Đại", new() { ["theme"] = "light", ["style"] = "standard", ["palette"] = "blue", ["font"] = "roboto", ["fontSize"] = "md" });
        }

        // Lệnh đổi Bảng màu (12 bảng màu hài hòa)
        if (q.Contains("vàng mộng mơ") || q.Contains("màu vàng"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Vàng Mộng Mơ", new() { ["palette"] = "gold" });
        }
        else if (q.Contains("hoa anh đào") || q.Contains("sakura"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Hoa Anh Đào Pastel", new() { ["palette"] = "sakura" });
        }
        else if (q.Contains("hồng ấm áp") || q.Contains("màu hồng"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Hồng Ấm Áp", new() { ["palette"] = "rose" });
        }
        else if (q.Contains("bạc hà") || q.Contains("mint"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Bạc Hà Tươi Mát", new() { ["palette"] = "mint" });
        }
        else if (q.Contains("oải hương") || q.Contains("lavender"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Tím Oải Hương", new() { ["palette"] = "lavender" });
        }
        else if (q.Contains("tím hoàng gia") || q.Contains("royal"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Tím Hoàng Gia", new() { ["palette"] = "royal" });
        }
        else if (q.Contains("giấy cổ") || q.Contains("sepia"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Giấy Cổ Thư Viện", new() { ["palette"] = "sepia" });
        }
        else if (q.Contains("lục bảo") || q.Contains("xanh lá"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Lục Bảo Thư Viện", new() { ["palette"] = "emerald" });
        }
        else if (q.Contains("đại dương") || q.Contains("ocean"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Xanh Đại Dương", new() { ["palette"] = "ocean" });
        }
        else if (q.Contains("hoàng hôn") || q.Contains("màu cam") || q.Contains("coral"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Cam Hoàng Hôn", new() { ["palette"] = "coral" });
        }
        else if (q.Contains("ngân hà") || q.Contains("midnight"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Đêm Ngân Hà", new() { ["palette"] = "midnight" });
        }
        else if (q.Contains("sapphire") || q.Contains("xanh mặc định"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi màu Xanh Sapphire", new() { ["palette"] = "blue" });
        }

        // Lệnh đổi Font chữ & Cỡ chữ
        if (q.Contains("font viết tay") || q.Contains("chữ viết tay") || q.Contains("patrick"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi font Viết tay Patrick Hand", new() { ["font"] = "patrick" });
        }
        else if (q.Contains("font có chân") || q.Contains("font sách") || q.Contains("lora"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi font Sách giấy Lora", new() { ["font"] = "lora" });
        }
        else if (q.Contains("be vietnam") || q.Contains("font hiện đại"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi font Be Vietnam Pro", new() { ["font"] = "bevietnam" });
        }
        else if (q.Contains("jetbrains") || q.Contains("font code") || q.Contains("đơn cách") || q.Contains("font lập trình"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi font JetBrains Mono", new() { ["font"] = "jetbrains" });
        }
        else if (q.Contains("font roboto") || q.Contains("font mặc định"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi font Roboto chuẩn", new() { ["font"] = "roboto" });
        }

        // Lệnh đổi Loại Nền Trang (12 nền nội bộ)
        if (q.Contains("lưới chấm") || q.Contains("dot grid") || q.Contains("sổ tay"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Lưới Chấm Sổ Tay", new() { ["background"] = "dot-grid" });
        }
        else if (q.Contains("kẻ ô") || q.Contains("ô vuông") || q.Contains("graph grid"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Kẻ Ô Kỹ Thuật", new() { ["background"] = "graph-grid" });
        }
        else if (q.Contains("kẻ ngang") || q.Contains("trang vở") || q.Contains("notebook"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Trang Vở Kẻ Ngang", new() { ["background"] = "notebook-lines" });
        }
        else if (q.Contains("giấy sách") || q.Contains("paper grain"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Giấy Sách Ấm Áp", new() { ["background"] = "paper-grain" });
        }
        else if (q.Contains("cực quang") || q.Contains("aurora"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Cực Quang Dịu Nhẹ", new() { ["background"] = "soft-aurora" });
        }
        else if (q.Contains("nền hoàng hôn") || q.Contains("sunset"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Ánh Hoàng Hôn Ấm", new() { ["background"] = "sunset-glow" });
        }
        else if (q.Contains("vườn sách") || q.Contains("botanical") || q.Contains("nền xanh mát"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Vườn Sách Xanh Mát", new() { ["background"] = "botanical" });
        }
        else if (q.Contains("trời sao") || q.Contains("starry"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Bầu Trời Sao Nhẹ", new() { ["background"] = "starry-night" });
        }
        else if (q.Contains("vải bố") || q.Contains("linen"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Vải Bố Cổ Điển", new() { ["background"] = "warm-linen" });
        }
        else if (q.Contains("mây pastel") || q.Contains("pastel cloud"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Mây Pastel Mộng Mơ", new() { ["background"] = "pastel-clouds" });
        }
        else if (q.Contains("hình học") || q.Contains("prism"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Hoa Văn Hình Học", new() { ["background"] = "geometric-prism" });
        }
        else if (q.Contains("nền trơn"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đổi nền Trơn Tiêu Chuẩn", new() { ["background"] = "solid" });
        }

        // Lệnh đổi 7 mức Cỡ chữ
        if (q.Contains("3xl") || q.Contains("siêu lớn") || q.Contains("cỡ chữ tối đa") || q.Contains("cỡ chữ lớn nhất"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đặt cỡ chữ Siêu lớn (22px)", new() { ["fontSize"] = "3xl" });
        }
        else if (q.Contains("2xl") || q.Contains("chữ rất lớn") || q.Contains("siêu to"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đặt cỡ chữ Rất lớn (20.5px)", new() { ["fontSize"] = "2xl" });
        }
        else if (q.Contains("cỡ xl") || q.Contains("chữ rõ nét"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đặt cỡ chữ Lớn rõ nét (19px)", new() { ["fontSize"] = "xl" });
        }
        else if (q.Contains("chữ lớn") || q.Contains("to chữ") || q.Contains("chữ to") || q.Contains("tăng cỡ chữ") || q.Contains("phóng to chữ"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đặt cỡ chữ Vừa lớn (17.5px)", new() { ["fontSize"] = "lg" });
        }
        else if (q.Contains("siêu nhỏ") || q.Contains("cỡ xs"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đặt cỡ chữ Siêu nhỏ (13px)", new() { ["fontSize"] = "xs" });
        }
        else if (q.Contains("chữ nhỏ") || q.Contains("giảm cỡ chữ") || q.Contains("thu nhỏ chữ"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đặt cỡ chữ Nhỏ (14px)", new() { ["fontSize"] = "sm" });
        }
        else if (q.Contains("cỡ chữ chuẩn") || q.Contains("chữ mặc định") || q.Contains("cỡ chữ vừa"))
        {
            AddUniqueCommand(cmds, "set_ui_prefs", "Đặt cỡ chữ Tiêu chuẩn (16px)", new() { ["fontSize"] = "md" });
        }

        return cmds;
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
        foreach (var uiCmd in ExtractUiClientCommands(cleanMsg))
        {
            AddUniqueCommand(clientCommands, uiCmd.Command, uiCmd.Label, uiCmd.Args);
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
            && (q.Contains("email") || q.Contains("mail") || q.Contains("nhắc hạn")))
        {
            if (currentUserRole is "Librarian" or "Admin")
            {
                AddBatchReminderCard(actionCards);
            }
        }
        else if ((q.Contains("gửi mail") || q.Contains("gửi email") || q.Contains("email") || q.Contains("gửi thư") || q.Contains("nhắc hạn cho tôi") || q.Contains("gửi nhắc")) && user != null)
        {
            if (q.Contains("thống kê") || q.Contains("vận hành") || q.Contains("báo cáo hệ thống") || q.Contains("báo cáo admin"))
            {
                if (currentUserRole == "Admin")
                {
                    AddAdminAnalyticsEmailCard(actionCards, user.Email ?? string.Empty);
                }
            }
            else if (q.Contains("clb") || q.Contains("câu lạc bộ") || q.Contains("thư mời") || q.Contains("sinh hoạt"))
            {
                if (currentUserRole is "Curator" or "Admin")
                {
                    AddClubAnnouncementEmailCard(actionCards, string.Empty, string.Empty);
                }
            }
            else if (q.Contains("tiến độ") || q.Contains("trích dẫn") || q.Contains("ghi chú") || q.Contains("đã đọc"))
            {
                AddReadingProgressEmailCard(actionCards, user.Email ?? string.Empty);
            }
            else if (q.Contains("gợi ý sách") || q.Contains("đề xuất sách") || q.Contains("danh sách sách"))
            {
                AddBookRecommendationEmailCard(actionCards, user.Email ?? string.Empty, string.Empty);
            }
            else if (q.Contains("ví") || q.Contains("sao kê") || q.Contains("hội viên") || q.Contains("gói"))
            {
                AddWalletMembershipEmailCard(actionCards, user.Email ?? string.Empty);
            }
            else if (q.Contains("tóm tắt") || q.Contains("hội thoại") || q.Contains("chat"))
            {
                AddChatSummaryEmailCard(actionCards, user.Email ?? string.Empty);
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

    private static void AddChatSummaryEmailCard(List<AiActionCardDto> cards, string email)
    {
        if (cards.Any(c => c.ActionKey == "send_chat_summary_email")) return;
        bool hasMail = AuthConfigHelper.IsValidEmailFormat(email);
        cards.Add(new AiActionCardDto
        {
            ActionKey = "send_chat_summary_email",
            Title = hasMail ? "Gửi Bản Lưu Hội Thoại AI Qua Email" : "Cần Bổ Sung Email Trước Khi Gửi Thư",
            Description = hasMail ? $"Gửi nội dung hội thoại hiện tại tới địa chỉ {email}." : "Tài khoản chưa có Email nhận thư hợp lệ.",
            ConfirmLabel = "Xác nhận gửi bản lưu Chat",
            Parameters = new Dictionary<string, JsonElement> { ["email"] = JsonSerializer.SerializeToElement(email) }
        });
    }

    private static void AddReadingProgressEmailCard(List<AiActionCardDto> cards, string email)
    {
        if (cards.Any(c => c.ActionKey == "send_reading_progress_email")) return;
        cards.Add(new AiActionCardDto
        {
            ActionKey = "send_reading_progress_email",
            Title = "Gửi Báo Cáo Tiến Độ Đọc & Trích Dẫn Qua Email",
            Description = $"Tổng hợp số trang đã đọc, tỷ lệ hoàn thành & trích dẫn nổi bật gửi tới {email}.",
            ConfirmLabel = "Gửi báo cáo tiến độ đọc",
            Parameters = new Dictionary<string, JsonElement> { ["email"] = JsonSerializer.SerializeToElement(email) }
        });
    }

    private static void AddBookRecommendationEmailCard(List<AiActionCardDto> cards, string email, string topic)
    {
        if (cards.Any(c => c.ActionKey == "send_book_recommendations_email")) return;
        cards.Add(new AiActionCardDto
        {
            ActionKey = "send_book_recommendations_email",
            Title = "Gửi Danh Sách Gợi Ý Sách Sẵn Có Qua Email",
            Description = string.IsNullOrWhiteSpace(topic)
                ? $"Gửi tuyển chọn các đầu sách sẵn sàng mượn 0đ về hộp thư {email}."
                : $"Gửi danh sách sách sẵn sàng mượn về chủ đề \"{topic}\" tới {email}.",
            ConfirmLabel = "Gửi thư gợi ý sách",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["email"] = JsonSerializer.SerializeToElement(email),
                ["topic"] = JsonSerializer.SerializeToElement(topic ?? string.Empty)
            }
        });
    }

    private static void AddWalletMembershipEmailCard(List<AiActionCardDto> cards, string email)
    {
        if (cards.Any(c => c.ActionKey == "send_wallet_membership_email")) return;
        cards.Add(new AiActionCardDto
        {
            ActionKey = "send_wallet_membership_email",
            Title = "Gửi Sao Kê Ví Tiền Ảo & Gói Hội Viên Qua Email",
            Description = $"Gửi thông tin số dư ví tiền ảo, hạng hội viên 4 cấp và 5 giao dịch gần nhất tới {email}.",
            ConfirmLabel = "Gửi sao kê Ví & Hội viên",
            Parameters = new Dictionary<string, JsonElement> { ["email"] = JsonSerializer.SerializeToElement(email) }
        });
    }

    private static void AddClubAnnouncementEmailCard(List<AiActionCardDto> cards, string title, string content)
    {
        if (cards.Any(c => c.ActionKey == "send_club_announcement_email")) return;
        cards.Add(new AiActionCardDto
        {
            ActionKey = "send_club_announcement_email",
            Title = "Gửi Thư Mời / Thông Báo Sinh Hoạt CLB",
            Description = "Phát email thông báo lịch sinh hoạt & giao lưu sách tới toàn bộ thành viên chính thức trong CLB bạn điều hành.",
            ConfirmLabel = "Phát thư thông báo CLB",
            RequiresRole = "Curator,Admin",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["title"] = JsonSerializer.SerializeToElement(title ?? string.Empty),
                ["content"] = JsonSerializer.SerializeToElement(content ?? string.Empty)
            }
        });
    }

    private static void AddAdminAnalyticsEmailCard(List<AiActionCardDto> cards, string email)
    {
        if (cards.Any(c => c.ActionKey == "send_admin_analytics_report_email")) return;
        cards.Add(new AiActionCardDto
        {
            ActionKey = "send_admin_analytics_report_email",
            Title = "Gửi Báo Cáo Thống Kê Vận Hành Hệ Thống (Admin)",
            Description = $"Tổng hợp số liệu thành viên, kho sách, lượt mượn, quá hạn & tranh chấp gửi tới {email}.",
            ConfirmLabel = "Gửi báo cáo thống kê Admin",
            RequiresRole = "Admin",
            Parameters = new Dictionary<string, JsonElement> { ["email"] = JsonSerializer.SerializeToElement(email) }
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

    private static string ExtractStringArg(Dictionary<string, JsonElement> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var el)) return string.Empty;
        return el.ValueKind == JsonValueKind.String ? (el.GetString() ?? string.Empty).Trim() : string.Empty;
    }
}
