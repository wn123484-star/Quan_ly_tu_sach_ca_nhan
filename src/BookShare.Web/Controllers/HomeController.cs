using System.Diagnostics;
using System.Security.Claims;
using BookShare.Web.Models;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class HomeController : Controller
{
    private readonly IPortalDiscoveryService _portalService;
    private readonly IS13GeminiAiService _geminiAiService;

    public HomeController(IPortalDiscoveryService portalService, IS13GeminiAiService geminiAiService)
    {
        _portalService = portalService;
        _geminiAiService = geminiAiService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword = null, string source = "all", string? category = null)
    {
        var vm = await _portalService.GetPortalHomeAsync(keyword, source, category);
        return View(vm);
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

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

public class QuickAiPromptDto
{
    public string? Message { get; set; }
}
