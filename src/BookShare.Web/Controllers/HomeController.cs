using System.Diagnostics;
using System.Security.Claims;
using BookShare.Web.Models;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class HomeController : Controller
{
    private readonly IPortalDiscoveryService _portalService;

    public HomeController(IPortalDiscoveryService portalService)
    {
        _portalService = portalService;
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
        var reply = await _portalService.GetQuickAiReplyAsync(userId, dto.Message ?? string.Empty);
        return Ok(new
        {
            title = "Trợ lý AI Gemini 2.5 (S13 RAG)",
            message = "Đã truy vấn ngữ cảnh BookCopies & ReadingProgresses từ SQL Server.",
            reply
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
