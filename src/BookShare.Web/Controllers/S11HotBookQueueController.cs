using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S11HotBookQueueController : Controller
{
    private readonly IS11HotBookQueueService _queueService;

    public S11HotBookQueueController(IS11HotBookQueueService queueService)
    {
        _queueService = queueService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _queueService.GetHotBookQueuePageAsync(currentUserId);
        return View("~/Views/Modules/S11_HotBookQueue/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enqueue([FromBody] EnqueueReservationRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, queuePosition) = await _queueService.EnqueueReservationAsync(currentUserId, request.BookId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đặt trước hàng chờ sách Hot thành công";
        TempData["StatusDetail"] = message;

        return Ok(new { title = "Đã xếp vào hàng chờ sách Hot", message, queuePosition });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NotifyTop([FromBody] NotifyTopQueueRequestDto request)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        if (User.Identity?.IsAuthenticated != true || (role != "Librarian" && role != "Curator" && role != "Admin"))
        {
            var displayRole = string.IsNullOrWhiteSpace(role) ? "Khách (Chưa đăng nhập)" : role;
            return StatusCode(403, new
            {
                message = $"MÃ 403 FORBIDDEN: Tài khoản vai trò [{displayRole}] không có quyền phát lệnh gọi nhận sách hàng chờ! Chỉ Thủ thư, Chủ nhiệm CLB hoặc Admin mới được phép."
            });
        }

        var (success, message) = await _queueService.NotifyFirstInQueueAsync(request.BookId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã thông báo cho độc giả đứng đầu hàng chờ";
        TempData["StatusDetail"] = message;

        return Ok(new { title = "Đã kích hoạt thông báo hàng chờ", message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel([FromBody] CancelReservationRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message) = await _queueService.CancelReservationAsync(currentUserId, request.ReservationId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã hủy đặt trước & cập nhật hàng chờ";
        TempData["StatusDetail"] = message;

        return Ok(new { title = "Đã hủy phiếu đặt trước", message });
    }
}
