using System.Diagnostics;
using System.Security.Claims;
using BookShare.Web.Models;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class HomeController : Controller
{
    private static readonly (string Prefix, string Url, string Label, string[] Keywords)[] ModuleRoutes =
    {
        ("S01", "/S01AuthOtpGoogle", "Đăng Nhập & Tài Khoản", new[] { "login", "signin", "register", "signup", "auth", "otp", "google", "profile", "account", "dang-nhap", "dang-ky", "tai-khoan", "ho-so" }),
        ("S02", "/S02PersonalShelf", "Tủ Sách Cá Nhân", new[] { "pers", "shelf", "bookshelf", "tu-sach", "tu-sach-ca-nhan", "ke-sach" }),
        ("S03", "/S03ReadingTracker", "Tiến Độ Đọc Sách", new[] { "read", "tracker", "progress", "tien-do", "doc-sach" }),
        ("S04", "/S04BookNotesQuotes", "Sổ Tay Ghi Chú", new[] { "note", "quote", "ghi-chu", "so-tay", "trich-dan" }),
        ("S05", "/S05IsbnAutoCatalog", "Quét Mã & Biên Mục Sách", new[] { "isbn", "catalog", "bien-muc", "quet-ma" }),
        ("S06", "/S06SearchDiscovery", "Tìm Kiếm & Khám Phá", new[] { "search", "discover", "discovery", "find", "tim-kiem", "kham-pha" }),
        ("S07", "/S07LibraryCatalog", "Kho Thư Viện Đối Tác", new[] { "library", "libcatalog", "kho-thu-vien", "thu-vien" }),
        ("S08", "/S08LibraryLoanFlow", "Mượn Sách Kho Thư Viện", new[] { "libraryloan", "muon-thu-vien", "muon-kho" }),
        ("S09", "/S09P2PLoanFlow", "Mượn Cộng Đồng P2P", new[] { "p2p", "peer", "cong-dong", "muon-p2p" }),
        ("S10", "/S10ConditionReport", "Hộ Chiếu Kiểm Định Sách", new[] { "condition", "report", "kiem-dinh", "bien-ban" }),
        ("S11", "/S11HotBookQueue", "Đặt Trước Sách Hot", new[] { "queue", "hotbook", "reserve", "dat-truoc", "hang-cho" }),
        ("S12", "/S12LoanRenewalFine", "Gia Hạn & Phí Trễ Hạn", new[] { "renew", "renewal", "fine", "gia-han", "phi-tre" }),
        ("S13", "/S13GeminiAiAssistant", "Trợ Lý Đọc Sách AI", new[] { "ai", "assistant", "gemini", "copilot", "tro-ly", "tro-ly-ai" }),
        ("S14", "/S14AiRecommendation", "Gợi Ý Sách Cá Nhân Hóa", new[] { "recommend", "advice", "goi-y", "de-xuat" }),
        ("S15", "/S15TrustScoreReview", "Điểm Uy Tín & Đánh Giá", new[] { "trust", "review", "uy-tin", "danh-gia" }),
        ("S16", "/S16MembershipTier", "Hạng Thành Viên", new[] { "member", "membership", "tier", "premium", "hang-thanh-vien" }),
        ("S17", "/S17SandboxPayment", "Ví Bảo Lãnh & Thanh Toán", new[] { "pay", "payment", "sandbox", "wallet", "thanh-toan", "vi-bao-lanh" }),
        ("S18", "/S18NotificationHub", "Trung Tâm Thông Báo", new[] { "notif", "notification", "alert", "thong-bao" }),
        ("S19", "/S19DisputeResolution", "Khiếu Nại & Tranh Chấp", new[] { "dispute", "khieu-nai", "tranh-chap" }),
        ("S20", "/S20ExchangeHubClub", "Trạm CLB & Giao Nhận Sách", new[] { "hub", "club", "exchange", "tram", "clb" }),
        ("S21", "/S21LibrarianDesk", "Quầy Thủ Thư", new[] { "desk", "librarian", "quay-thu-thu", "thu-thu" }),
        ("S22", "/S22AdminAnalytics", "Trung Tâm Quản Trị Hệ Thống", new[] { "admin", "analytics", "dashboard", "quan-tri" })
    };

    private static readonly string[] StaticPathPrefixes =
    {
        "/css/", "/js/", "/images/", "/uploads/", "/vendor/", "/lib/", "/modules/", "/fonts/"
    };

    private static readonly string[] StaticExtensions =
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".ico",
        ".css", ".js", ".map", ".woff", ".woff2", ".ttf", ".eot", ".json"
    };

    private readonly IPortalDiscoveryService _portalService;
    private readonly IS13GeminiAiService _geminiAiService;
    private readonly ISystemIconService _iconService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IPortalDiscoveryService portalService,
        IS13GeminiAiService geminiAiService,
        ISystemIconService iconService,
        ILogger<HomeController> logger)
    {
        _portalService = portalService;
        _geminiAiService = geminiAiService;
        _iconService = iconService;
        _logger = logger;
    }

    [HttpGet("/assets/system-icons/{iconKey}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetSystemIconAsset(string iconKey)
    {
        var (bytes, contentType) = await _iconService.GetCustomIconBinaryAsync(iconKey);
        if (bytes is not { Length: > 0 })
        {
            return NotFound();
        }

        return File(bytes, contentType);
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword = null, string source = "all", string? category = null)
    {
        var vm = await _portalService.GetPortalHomeAsync(keyword, source, category);
        return View(vm);
    }

    [HttpGet("/api/ai-chat/history")]
    public async Task<IActionResult> GetQuickAiHistory()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId) || User.Identity?.IsAuthenticated != true)
        {
            return Ok(new
            {
                authenticated = false,
                sessionId = 0,
                roleName = "Guest",
                userFullName = "Khách",
                userEmail = string.Empty,
                hasValidEmail = false,
                messages = Array.Empty<object>()
            });
        }

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var history = await _geminiAiService.GetQuickWidgetHistoryAsync(userId, role);
        return Ok(history);
    }

    [HttpPost("/api/ai-chat/reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetQuickAiChat([FromBody] BookShare.Web.Models.ViewModels.ResetAiChatRequestDto? request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId) || User.Identity?.IsAuthenticated != true)
        {
            return Ok(new
            {
                title = "Đã làm mới khung chat",
                message = "Đã đặt lại cuộc trò chuyện chế độ xem nhanh.",
                sessionId = 0,
                greeting = new
                {
                    sender = "Gemini",
                    messageText = "Xin chào! Mình là Trợ lý AI BookShare. Bạn cần tìm sách mượn miễn phí 0đ hay đổi phong cách giao diện (Nét vẽ nghệch ngoặc, Vàng mộng mơ, Hồng ấm áp...)?"
                }
            });
        }

        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message, sessionId, greeting) = await _geminiAiService.ResetChatSessionAsync(userId, role, request?.SessionId);
            if (!success)
            {
                return BadRequest(new { message });
            }

            return Ok(new { title = "Đã đặt lại cuộc trò chuyện AI", message, sessionId, greeting });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost("/api/ai-chat/quick-ask")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickAiAsk([FromBody] QuickAiPromptDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var prompt = dto.Message ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(userId) && User.Identity?.IsAuthenticated == true)
        {
            var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
            var copilotRes = await _geminiAiService.SendCopilotMessageAsync(
                userId,
                currentUserRole,
                null,
                prompt);

            return Ok(new
            {
                title = $"Trợ lý AI Thủ thư ({copilotRes.SourceMode})",
                message = "Đã trả lời dựa trên kho sách và dữ liệu đọc của bạn.",
                reply = copilotRes.ReplyText,
                sessionId = copilotRes.SessionId,
                sourceMode = copilotRes.SourceMode,
                clientCommands = copilotRes.ClientCommands,
                actionCards = copilotRes.ActionCards
            });
        }

        var guestReply = await _portalService.GetQuickAiReplyAsync(null, prompt);
        var guestCommands = new List<BookShare.Web.Models.ViewModels.AiClientCommandDto>();
        var pLower = prompt.ToLowerInvariant();
        if (pLower.Contains("chế độ tối") || pLower.Contains("nền tối") || pLower.Contains("dark mode"))
        {
            guestCommands.Add(new BookShare.Web.Models.ViewModels.AiClientCommandDto
            {
                Command = "set_theme",
                Label = "Chuyển giao diện Tối",
                Args = new() { ["theme"] = "dark" }
            });
        }
        else if (pLower.Contains("chế độ sáng") || pLower.Contains("nền sáng") || pLower.Contains("light mode"))
        {
            guestCommands.Add(new BookShare.Web.Models.ViewModels.AiClientCommandDto
            {
                Command = "set_theme",
                Label = "Chuyển giao diện Sáng",
                Args = new() { ["theme"] = "light" }
            });
        }

        return Ok(new
        {
            title = "Trợ lý AI Thủ thư",
            message = "Đã tra cứu danh mục sách sẵn sàng cho mượn.",
            reply = guestReply,
            clientCommands = guestCommands,
            actionCards = Array.Empty<object>()
        });
    }

    [Route("/Home/Error")]
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        DisableStatusCodeReExecute();

        var exceptionFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        string requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        string? originalPath = SanitizeOriginalPath(exceptionFeature?.Path);
        int statusCode = exceptionFeature?.Error is UnauthorizedAccessException
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status500InternalServerError;

        if (exceptionFeature?.Error != null)
        {
            _logger.LogError(
                exceptionFeature.Error,
                "Unhandled exception for path {OriginalPath} [RequestId={RequestId}]",
                originalPath ?? HttpContext.Request.Path.Value,
                requestId);
        }
        else
        {
            _logger.LogError(
                "Unhandled error handler invoked without exception feature for {Path} [RequestId={RequestId}]",
                originalPath ?? HttpContext.Request.Path.Value,
                requestId);
        }

        Response.StatusCode = statusCode;

        if (IsApiOrAjax(HttpContext, originalPath))
        {
            var (_, apiTitle, apiDescription, _) = BuildCopyForStatusCode(statusCode, originalPath, null);
            return new JsonResult(new
            {
                success = false,
                statusCode,
                title = apiTitle,
                message = apiDescription,
                requestId
            })
            {
                StatusCode = statusCode,
                ContentType = "application/json; charset=utf-8"
            };
        }

        var model = BuildErrorViewModel(statusCode, originalPath, requestId);
        return View("~/Views/Shared/Error.cshtml", model);
    }

    [Route("/Home/StatusCode")]
    [Route("/Home/StatusCode/{routeCode:int}")]
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult StatusCodePage(int? routeCode = null, int? code = null, string? returnUrl = null)
    {
        DisableStatusCodeReExecute();

        int effectiveCode = routeCode ?? code ?? StatusCodes.Status500InternalServerError;
        if (effectiveCode < 400 || effectiveCode > 599)
        {
            effectiveCode = StatusCodes.Status500InternalServerError;
        }

        var statusFeature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        string? rawOriginal = statusFeature != null
            ? $"{statusFeature.OriginalPath}{statusFeature.OriginalQueryString}"
            : returnUrl;
        string? originalPath = SanitizeOriginalPath(rawOriginal);
        string requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        if (IsStaticAssetPath(originalPath))
        {
            return new StatusCodeResult(effectiveCode);
        }

        if (effectiveCode >= 500)
        {
            _logger.LogError(
                "HTTP {StatusCode} returned for {OriginalPath} [RequestId={RequestId}]",
                effectiveCode,
                originalPath ?? HttpContext.Request.Path.Value,
                requestId);
        }
        else
        {
            _logger.LogWarning(
                "HTTP {StatusCode} on {OriginalPath} [RequestId={RequestId}]",
                effectiveCode,
                originalPath ?? HttpContext.Request.Path.Value,
                requestId);
        }

        Response.StatusCode = effectiveCode;

        if (IsApiOrAjax(HttpContext, originalPath))
        {
            var (_, apiTitle, apiDescription, _) = BuildCopyForStatusCode(effectiveCode, originalPath, null);
            return new JsonResult(new
            {
                success = false,
                statusCode = effectiveCode,
                title = apiTitle,
                message = apiDescription,
                requestId
            })
            {
                StatusCode = effectiveCode,
                ContentType = "application/json; charset=utf-8"
            };
        }

        var model = BuildErrorViewModel(effectiveCode, originalPath, requestId);
        return View("~/Views/Shared/Error.cshtml", model);
    }

    private ErrorViewModel BuildErrorViewModel(int statusCode, string? originalPath, string requestId)
    {
        var suggestion = TryResolveSuggestedRoute(originalPath);
        string? suggestedLabel = suggestion?.Label;
        var (normalizedCode, title, description, guidance) = BuildCopyForStatusCode(statusCode, originalPath, suggestedLabel);

        bool isAuth = User.Identity?.IsAuthenticated ?? false;
        string? role = isAuth ? (User.FindFirstValue(ClaimTypes.Role) ?? "User") : null;

        return new ErrorViewModel
        {
            StatusCode = normalizedCode,
            Title = title,
            Description = description,
            Guidance = guidance,
            OriginalPath = originalPath,
            SuggestedUrl = normalizedCode == 404 ? suggestion?.Url : null,
            SuggestedLabel = normalizedCode == 404 ? suggestion?.Label : null,
            IsAuthenticated = isAuth,
            CurrentRole = role,
            RequestId = requestId
        };
    }

    private static (int Code, string Title, string Description, string Guidance) BuildCopyForStatusCode(
        int statusCode,
        string? originalPath,
        string? areaLabel)
    {
        return statusCode switch
        {
            StatusCodes.Status404NotFound => (
                404,
                "Không tìm thấy trang bạn yêu cầu",
                "Đường dẫn bạn truy cập không tồn tại hoặc đã được thay đổi.",
                !string.IsNullOrWhiteSpace(areaLabel)
                    ? $"Có phải bạn đang muốn mở mục \"{areaLabel}\"? Hãy chọn nút gợi ý bên dưới để đi thẳng tới trang đúng."
                    : "Vui lòng kiểm tra lại địa chỉ đường dẫn hoặc sử dụng các lối tắt nhanh để tiếp tục khám phá kho sách cộng đồng."
            ),
            StatusCodes.Status403Forbidden => (
                403,
                "Không có quyền truy cập khu vực này",
                !string.IsNullOrWhiteSpace(areaLabel)
                    ? $"Tài khoản hiện tại chưa được cấp quyền mở khu vực {areaLabel}."
                    : "Tài khoản hiện tại chưa được cấp quyền mở khu vực này.",
                "Bạn có thể chuyển sang tài khoản được phân quyền phù hợp hoặc quay lại Trang chủ BookShare."
            ),
            StatusCodes.Status401Unauthorized => (
                401,
                "Vui lòng đăng nhập để tiếp tục",
                "Phiên đăng nhập đã hết hạn hoặc trang này yêu cầu xác thực tài khoản.",
                "Hãy đăng nhập lại bằng tài khoản thành viên hoặc mã QR để sử dụng đầy đủ tính năng."
            ),
            StatusCodes.Status400BadRequest or StatusCodes.Status405MethodNotAllowed => (
                statusCode,
                "Yêu cầu chưa hợp lệ",
                "Yêu cầu gửi lên không đúng định dạng hoặc phương thức không được hỗ trợ.",
                "Vui lòng quay lại trang trước và thao tác thông qua các nút chức năng trên giao diện."
            ),
            _ => (
                statusCode >= 500 ? statusCode : 500,
                "Hệ thống đang gặp gián đoạn tạm thời",
                "Hệ thống đang gặp sự cố gián đoạn ngoài ý muốn. Chúng tôi đã ghi nhận để xử lý.",
                "Vui lòng thử tải lại trang sau ít giây hoặc quay về Trang chủ. Nếu sự cố tiếp diễn, hãy gửi mã tra cứu hỗ trợ cho Quản trị viên."
            )
        };
    }

    private static (string Url, string Label)? TryResolveSuggestedRoute(string? originalPath)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return null;
        }

        string pathOnly = originalPath.Split('?', '#')[0].Trim();
        if (string.IsNullOrEmpty(pathOnly) || pathOnly == "/")
        {
            return null;
        }

        string normalized = pathOnly.Trim('/').ToLowerInvariant();
        string firstSegment = normalized.Split('/')[0];

        // 1. Khớp tiền tố S01..S22 hoặc gần đúng tên Route của 22 phân hệ
        foreach (var item in ModuleRoutes)
        {
            string codeLower = item.Prefix.ToLowerInvariant();
            string routeSlug = item.Url.Trim('/').ToLowerInvariant();

            if (firstSegment == routeSlug)
            {
                return (item.Url, item.Label);
            }
            if (firstSegment.StartsWith(codeLower, StringComparison.Ordinal) ||
                routeSlug.StartsWith(firstSegment, StringComparison.Ordinal) && firstSegment.Length >= 4)
            {
                return (item.Url, item.Label);
            }
        }

        // 2. Khớp từ khóa tiếng Việt / tiếng Anh phổ biến
        foreach (var item in ModuleRoutes)
        {
            foreach (var kw in item.Keywords)
            {
                if (normalized.Contains(kw, StringComparison.Ordinal))
                {
                    return (item.Url, item.Label);
                }
            }
        }

        return null;
    }

    private static string? SanitizeOriginalPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        string trimmed = rawPath.Trim();
        if (!trimmed.StartsWith('/') || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed.Length > 180 ? trimmed[..180] : trimmed;
    }

    private static bool IsStaticAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string pathOnly = path.Split('?', '#')[0];
        foreach (var prefix in StaticPathPrefixes)
        {
            if (pathOnly.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string ext = Path.GetExtension(pathOnly);
        return !string.IsNullOrEmpty(ext) && StaticExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsApiOrAjax(HttpContext context, string? originalPath)
    {
        string? checkPath = string.IsNullOrWhiteSpace(originalPath) ? context.Request.Path.Value : originalPath;
        if (!string.IsNullOrWhiteSpace(checkPath) &&
            checkPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string accept = context.Request.Headers.Accept.ToString();
        if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(context.Request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !HttpMethods.IsGet(context.Request.Method);
    }

    private void DisableStatusCodeReExecute()
    {
        var feature = HttpContext.Features.Get<IStatusCodePagesFeature>();
        if (feature != null)
        {
            feature.Enabled = false;
        }
    }
}

public class QuickAiPromptDto
{
    public string? Message { get; set; }
}
