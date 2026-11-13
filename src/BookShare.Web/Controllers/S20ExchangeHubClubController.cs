using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S20ExchangeHubClubController : Controller
{
    private readonly IS20ExchangeHubClubService _hubService;

    public S20ExchangeHubClubController(IS20ExchangeHubClubService hubService)
    {
        _hubService = hubService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-curator-01";
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var vm = await _hubService.GetExchangeHubPageAsync(currentUserId, currentUserRole);
        return View("~/Views/Modules/S20_ExchangeHubClub/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterDropoff([FromBody] S20RegisterCopyDropoffDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _hubService.RegisterCopyDropoffAtHubAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã đăng ký ký gửi sách P2P tại Trạm CLB/Hub (S20)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã đăng ký ký gửi sách tại Hub (S20)",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Curator,Admin")]
    public async Task<IActionResult> VerifyPhysicalBook([FromBody] S20CuratorVerifyCopyDto request)
    {
        var curatorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-curator-01";
        var (success, message) = await _hubService.CuratorVerifyPhysicalCopyAsync(curatorUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Curator đã xác nhận kiểm định sách thật tại Hub (S20)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Kiểm định sách thật thành công (S20 Curator)",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Curator,Admin")]
    public async Task<IActionResult> ProcessHubHandover([FromBody] S20CuratorHandoverActionDto request)
    {
        var curatorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-curator-01";
        var (success, message) = await _hubService.CuratorProcessHubHandoverAsync(curatorUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Curator đã xác nhận giao nhận trung gian tại Hub (S20)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã xác nhận giao nhận tại Trạm CLB/Hub (S20)",
            message
        });
    }
}
