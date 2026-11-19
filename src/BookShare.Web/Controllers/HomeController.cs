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

    // [BS-AGENT-AI-COPILOT] Endpoint AI Copilot Hành Động trả về { reply, sessionId, actions, emailSubject }
    [HttpPost("/api/ai-chat/quick-ask")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> QuickAiAsk([FromBody] QuickAiPromptDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, replyText, sessionId, _, sourceMode, actions, emailSubject) = await _geminiAiService.SendCopilotMessageAsync(
            userId,
            null,
            dto.Message ?? string.Empty);

        return Ok(new
        {
            title = $"AI Copilot BookShare ({sourceMode})",
            message = $"Đã lưu lịch sử vào AiChatMessages (Session #{sessionId}).",
            reply = replyText,
            sessionId,
            actions,
            emailSubject
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
