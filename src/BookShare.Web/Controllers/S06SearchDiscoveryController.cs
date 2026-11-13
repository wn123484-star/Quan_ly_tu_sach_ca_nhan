using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S06SearchDiscoveryController : Controller
{
    private readonly IS06SearchDiscoveryService _searchService;

    public S06SearchDiscoveryController(IS06SearchDiscoveryService searchService)
    {
        _searchService = searchService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? keyword = null,
        string? author = null,
        string? isbn = null,
        int? categoryId = null,
        string? areaFilter = null,
        string ownerType = "all",
        string statusFilter = "all",
        int minTrustScore = 0,
        int page = 1,
        int pageSize = 6)
    {
        var filter = new S06SearchDiscoveryFilterDto
        {
            Keyword = keyword,
            Author = author,
            Isbn = isbn,
            CategoryId = categoryId,
            AreaFilter = areaFilter,
            OwnerType = string.IsNullOrWhiteSpace(ownerType) ? "all" : ownerType,
            StatusFilter = string.IsNullOrWhiteSpace(statusFilter) ? "all" : statusFilter,
            MinTrustScore = minTrustScore,
            Page = page,
            PageSize = pageSize
        };

        var vm = await _searchService.SearchCopiesAsync(filter);
        return View("~/Views/Modules/S06_SearchDiscovery/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickRequestLoan([FromBody] QuickLoanRequestDto request)
    {
        var borrowerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, loanRequestId) = await _searchService.CreateQuickLoanRequestAsync(borrowerUserId, request);

        if (!success)
        {
            return BadRequest(new { message });
        }

        return Ok(new
        {
            title = "Đã tạo phiếu mượn sách thành công (S06)",
            message,
            loanRequestId
        });
    }
}
