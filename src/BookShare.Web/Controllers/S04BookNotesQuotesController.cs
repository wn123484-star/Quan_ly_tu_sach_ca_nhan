using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S04BookNotesQuotesController : Controller
{
    private readonly IS04BookNotesService _notesService;

    public S04BookNotesQuotesController(IS04BookNotesService notesService)
    {
        _notesService = notesService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? bookId = null, bool community = false)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var vm = await _notesService.GetBookNotesPageAsync(currentUserId, bookId, community);
        return View("~/Views/Modules/S04_BookNotesQuotes/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNote([FromBody] SaveBookNoteRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            var (success, message, note) = await _notesService.SaveBookNoteAsync(currentUserId, request);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã lưu ghi chú & trích dẫn sách";
            TempData["StatusDetail"] = message;

            return Ok(new
            {
                title = "Đã lưu ghi chú & trích dẫn sách",
                message,
                note
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
    public async Task<IActionResult> TogglePublic([FromBody] ToggleNotePublicRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            var (success, message) = await _notesService.TogglePublicAsync(
                currentUserId,
                request.NoteId,
                request.IsPublic);

            if (!success)
            {
                return BadRequest(new { message });
            }

            return Ok(new
            {
                title = "Cập nhật quyền chia sẻ ghi chú",
                message,
                isPublic = request.IsPublic
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
    public async Task<IActionResult> DeleteNote([FromBody] DeleteBookNoteRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            var (success, message) = await _notesService.DeleteBookNoteAsync(currentUserId, request.NoteId);
            if (!success)
            {
                return BadRequest(new { message });
            }

            return Ok(new
            {
                title = "Đã xóa ghi chú",
                message
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
