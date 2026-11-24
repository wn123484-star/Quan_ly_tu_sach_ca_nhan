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
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUserRole = User.Identity?.IsAuthenticated == true
            ? (User.FindFirstValue(ClaimTypes.Role) ?? "User")
            : "Guest";
        var vm = await _hubService.GetExchangeHubPageAsync(currentUserId, currentUserRole);
        return View("~/Views/Modules/S20_ExchangeHubClub/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterDropoff([FromBody] S20RegisterCopyDropoffDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var (success, message) = await _hubService.RegisterCopyDropoffAtHubAsync(currentUserId, currentUserRole, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã đăng ký ký gửi sách tại Điểm trạm CLB";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã đăng ký ký gửi sách tại Điểm trạm",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Curator,Admin")]
    public async Task<IActionResult> VerifyPhysicalBook([FromBody] S20CuratorVerifyCopyDto request)
    {
        var curatorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _hubService.CuratorVerifyPhysicalCopyAsync(curatorUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Chủ nhiệm CLB đã xác nhận kiểm định sách tại Trạm";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Kiểm định sách thật thành công",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Curator,Admin")]
    public async Task<IActionResult> ProcessHubHandover([FromBody] S20CuratorHandoverActionDto request)
    {
        var curatorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _hubService.CuratorProcessHubHandoverAsync(curatorUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã xác nhận giao nhận trung gian tại Điểm trạm CLB";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã xác nhận giao nhận tại Điểm trạm CLB",
            message
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Curator,Admin")]
    public async Task<IActionResult> UpdateHubStation([FromBody] S20UpdateHubStationDto request)
    {
        var curatorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _hubService.UpdateHubStationInfoAsync(curatorUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã cập nhật thông tin Điểm trạm CLB/Hub";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Đã cập nhật Điểm trạm CLB/Hub", message });
    }
}
