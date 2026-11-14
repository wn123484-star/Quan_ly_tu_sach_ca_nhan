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

    // API gợi ý tức thì (Autocomplete) cho thanh tìm kiếm: top 5 đầu sách khớp nhất
    [HttpGet]
    public async Task<IActionResult> Suggest(string? q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Ok(Array.Empty<object>());
        }

        var vm = await _searchService.SearchCopiesAsync(new S06SearchDiscoveryFilterDto
        {
            Keyword = q.Trim(),
            OwnerType = "all",
            StatusFilter = "all",
            Page = 1,
            PageSize = 24
        });

        var suggestions = vm.Results
            .GroupBy(r => r.BookId)
            .Select(g => new
            {
                bookId = g.Key,
                title = g.First().Title,
                author = g.First().Author,
                isbn = g.First().ISBN,
                coverImageUrl = g.First().CoverImageUrl,
                availableCopies = g.Count(c => c.Status == "Available")
            })
            .Take(5);

        return Ok(suggestions);
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
