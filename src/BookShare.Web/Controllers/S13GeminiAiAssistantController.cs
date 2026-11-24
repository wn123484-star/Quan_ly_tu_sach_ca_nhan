using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var vm = await _geminiService.GetAssistantPageAsync(currentUserId, sessionId);
        return View("~/Views/Modules/S13_GeminiAiAssistant/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessage([FromBody] SendAiChatRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var res = await _geminiService.SendCopilotMessageAsync(
            currentUserId,
            currentUserRole,
            request.SessionId,
            request.Message);

        if (!res.Success)
        {
            return BadRequest(new { message = res.ReplyText });
        }

        return Ok(new
        {
            title = $"Trợ lý AI phản hồi ({res.SourceMode})",
            message = "Đã lưu nội dung vào lịch sử hội thoại của bạn.",
            replyText = res.ReplyText,
            sessionId = res.SessionId,
            promptTokens = res.PromptTokens,
            sourceMode = res.SourceMode,
            clientCommands = res.ClientCommands,
            actionCards = res.ActionCards
        });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExecuteCopilotAction([FromBody] ExecuteCopilotActionRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _geminiService.ExecuteCopilotActionAsync(
                currentUserId,
                currentUserRole,
                request);

            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Trợ lý Copilot đã hoàn tất thao tác";
            TempData["StatusDetail"] = message;
            return Ok(new
            {
                title = "Đã thực hiện thao tác thành công",
                message
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSession([FromBody] CreateAiSessionRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, sessionId) = await _geminiService.CreateSessionAsync(currentUserId, request.Title);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã tạo phiên hội thoại AI mới";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã tạo phiên hội thoại AI mới", message, sessionId });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSession([FromBody] DeleteAiSessionRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                try
                {
                    var (success, message) = await _geminiService.DeleteSessionAsync(currentUserId, request.SessionId);
                    if (!success)
                    {
                        return BadRequest(new { message });
                    }

                    TempData["StatusCode"] = 200;
                    TempData["StatusTitle"] = "Đã xóa phiên hội thoại AI";
                    TempData["StatusDetail"] = message;
                    return Ok(new { title = "Đã xóa phiên hội thoại AI", message });
                }
                catch (UnauthorizedAccessException ex)
                {
                    return StatusCode(403, new { message = ex.Message });
                }
            }
        }
