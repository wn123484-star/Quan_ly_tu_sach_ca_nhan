using System.Security.Claims;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

[Route("api/mobile")]
public class MobileApiController : Controller
{
    private readonly IMobileCompanionService _mobileService;

    public MobileApiController(IMobileCompanionService mobileService)
    {
        _mobileService = mobileService;
    }

    [HttpGet("notifications/unread")]
    [Authorize]
    public async Task<IActionResult> GetUnreadNotifications([FromQuery] int sinceId = 0)
    {
        string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var result = await _mobileService.GetUnreadNotificationsAsync(currentUserId, sinceId);
        return Ok(result);
    }

    [HttpGet("session")]
    public async Task<IActionResult> GetSession()
    {
        bool isAuth = User.Identity?.IsAuthenticated ?? false;
        string? currentUserId = isAuth ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        string? role = isAuth ? User.FindFirstValue(ClaimTypes.Role) : null;
        string? fullName = isAuth ? User.Identity?.Name : null;

        var result = await _mobileService.GetSessionStateAsync(currentUserId, role, fullName);
        return Ok(result);
    }
}
