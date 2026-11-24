using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S07LibraryCatalogController : Controller
{
    private readonly IS07LibraryCatalogService _catalogService;

    public S07LibraryCatalogController(IS07LibraryCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int? libraryId = null,
        string statusFilter = "all",
        string? keyword = null,
        int page = 1,
        int pageSize = 6)
    {
        var vm = await _catalogService.GetLibraryCatalogAsync(libraryId, statusFilter, keyword, page, pageSize);
        return View("~/Views/Modules/S07_LibraryCatalog/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize(Roles = "Librarian,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateShelfLocation([FromBody] S07UpdateShelfRequestDto request)
    {
        var (success, message) = await _catalogService.UpdateCopyShelfLocationAsync(request.BookCopyId, request.ShelfLocation);
        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Cập nhật vị trí kệ sách thành công",
            message
        });
    }
}
