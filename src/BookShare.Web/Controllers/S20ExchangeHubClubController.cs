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
    public async Task<IActionResult> Index(
        int? hubId = null,
        string? clubKeyword = null,
        string hubType = "all",
        string memberStatus = "all",
        string orderStatus = "all",
        int memberPage = 1,
        int orderPage = 1)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUserRole = User.Identity?.IsAuthenticated == true
            ? (User.FindFirstValue(ClaimTypes.Role) ?? "User")
            : "Guest";
        var vm = await _hubService.GetExchangeHubPageAsync(
            currentUserId,
            currentUserRole,
            hubId,
            clubKeyword,
            hubType,
            memberStatus,
            orderStatus,
            memberPage,
            orderPage);
        return View("~/Views/Modules/S20_ExchangeHubClub/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateClub([FromBody] S20CreateClubDto request)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, hubId) = await _hubService.CreateClubAsync(adminUserId, request);
        if (!success) return BadRequest(new { message });

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã thành lập Câu lạc bộ mới";
        TempData["StatusDetail"] = message;
        return Ok(new { title = "Thành lập CLB thành công", message, hubId });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignClubLeader([FromBody] S20AssignClubLeaderDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _hubService.AssignClubLeaderAsync(currentUserId, currentUserRole, request);
            if (!success) return BadRequest(new { message });
            return Ok(new { title = "Đã cập nhật Trưởng nhóm CLB", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddClubMember([FromBody] S20AddClubMemberDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _hubService.AddClubMemberAsync(currentUserId, currentUserRole, request);
            if (!success) return BadRequest(new { message });
            return Ok(new { title = "Đã thêm thành viên vào nhóm CLB", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> JoinClub([FromBody] S20JoinClubDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _hubService.JoinClubAsync(currentUserId, request);
        if (!success) return BadRequest(new { message });
        return Ok(new { title = "Cập nhật gia nhập CLB", message });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LeaveClub([FromBody] S20JoinClubDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message) = await _hubService.LeaveClubAsync(currentUserId, request.HubId);
        if (!success) return BadRequest(new { message });
        return Ok(new { title = "Đã rời khỏi CLB", message });
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviewClubMember([FromBody] S20ReviewClubMemberDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _hubService.ReviewClubMemberAsync(currentUserId, currentUserRole, request);
            if (!success) return BadRequest(new { message });
            return Ok(new { title = "Đã xét duyệt thành viên CLB", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateClubMemberRole([FromBody] S20UpdateClubMemberRoleDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _hubService.UpdateClubMemberRoleAsync(currentUserId, currentUserRole, request);
            if (!success) return BadRequest(new { message });
            return Ok(new { title = "Đã cập nhật vai trò thành viên CLB", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveClubMember([FromBody] S20RemoveClubMemberDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _hubService.RemoveClubMemberAsync(currentUserId, currentUserRole, request);
            if (!success) return BadRequest(new { message });
            return Ok(new { title = "Đã mời thành viên rời CLB", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateClubPost([FromBody] S20CreateClubPostDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _hubService.CreateClubPostAsync(currentUserId, currentUserRole, request);
            if (!success) return BadRequest(new { message });
            return Ok(new { title = "Đã đăng thông báo CLB", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteClubPost([FromBody] S20DeleteClubPostDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _hubService.DeleteClubPostAsync(currentUserId, currentUserRole, request);
            if (!success) return BadRequest(new { message });
            return Ok(new { title = "Đã xóa thông báo CLB", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterDropoff([FromBody] S20RegisterCopyDropoffDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _hubService.RegisterCopyDropoffAtHubAsync(currentUserId, currentUserRole, request);
            if (!success) return BadRequest(new { message });

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã đăng ký ký gửi sách tại Điểm trạm CLB";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã đăng ký ký gửi sách tại Điểm trạm", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> VerifyPhysicalBook([FromBody] S20CuratorVerifyCopyDto request)
    {
        var curatorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var curatorUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _hubService.CuratorVerifyPhysicalCopyAsync(curatorUserId, curatorUserRole, request);
            if (!success) return BadRequest(new { message });

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Trưởng nhóm CLB đã xác nhận kiểm định sách tại Trạm";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Kiểm định sách thật thành công", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ProcessHubHandover([FromBody] S20CuratorHandoverActionDto request)
    {
        var curatorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var curatorUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _hubService.CuratorProcessHubHandoverAsync(curatorUserId, curatorUserRole, request);
            if (!success) return BadRequest(new { message });

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã xác nhận giao nhận trung gian tại Điểm trạm CLB";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã xác nhận giao nhận tại Điểm trạm CLB", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> UpdateHubStation([FromBody] S20UpdateHubStationDto request)
    {
        var curatorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var curatorUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message) = await _hubService.UpdateHubStationInfoAsync(curatorUserId, curatorUserRole, request);
            if (!success) return BadRequest(new { message });

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Đã cập nhật thông tin Điểm trạm CLB/Hub";
            TempData["StatusDetail"] = message;
            return Ok(new { title = "Đã cập nhật Điểm trạm CLB/Hub", message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
