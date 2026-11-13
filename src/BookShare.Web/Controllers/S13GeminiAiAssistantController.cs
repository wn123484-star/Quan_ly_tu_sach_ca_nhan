using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S13GeminiAiAssistantController : Controller
{
    private readonly IS13GeminiAiService _geminiService;

    public S13GeminiAiAssistantController(IS13GeminiAiService geminiService)
    {
        _geminiService = geminiService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? sessionId = null)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _geminiService.GetAssistantPageAsync(currentUserId, sessionId);
        return View("~/Views/Modules/S13_GeminiAiAssistant/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessage([FromBody] SendAiChatRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, replyText, sessionId, promptTokens, sourceMode) = await _geminiService.SendMessageWithRagAsync(
            currentUserId,
            request.SessionId,
            request.Message);

        if (!success)
        {
            return BadRequest(new { message = replyText });
        }

        return Ok(new
        {
            title = $"Trợ lý AI Gemini phản hồi ({sourceMode})",
            message = $"Đã lưu tin nhắn vào AiChatMessages (Session #{sessionId} • {promptTokens} tokens).",
            replyText,
            sessionId,
            promptTokens,
            sourceMode
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSession([FromBody] CreateAiSessionRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, sessionId) = await _geminiService.CreateSessionAsync(currentUserId, request.Title);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã tạo phiên hội thoại AI mới (S13)";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã tạo phiên hội thoại AI mới", message, sessionId });
    }
}
