using BookShare.Web.Services.Interfaces;
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
    public async Task<IActionResult> Index(int? libraryId = null, string statusFilter = "all", string? keyword = null)
    {
        var vm = await _catalogService.GetLibraryCatalogAsync(libraryId, statusFilter, keyword);
        return View("~/Views/Modules/S07_LibraryCatalog/Index.cshtml", vm);
    }
}
