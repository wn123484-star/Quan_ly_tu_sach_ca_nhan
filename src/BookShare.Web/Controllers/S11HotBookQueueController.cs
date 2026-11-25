using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

/// <summary>
/// [S11] Controller Hàng chờ đặt trước Sách Hot (FIFO kết hợp ưu tiên Hạng hội viên) và Giữ chỗ 48 giờ khi có bản sao hoàn trả.
/// </summary>
/// <remarks>RBAC: Thành viên đăng ký/hủy lượt chờ của chính mình; Thủ thư & Admin có quyền kích hoạt thông báo thủ công tới vị trí #1.</remarks>
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
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var vm = await _queueService.GetHotBookQueuePageAsync(currentUserId);
        return View("~/Views/Modules/S11_HotBookQueue/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enqueue([FromBody] EnqueueReservationRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
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
    [Authorize(Roles = "Librarian,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NotifyTop([FromBody] NotifyTopQueueRequestDto request)
    {
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
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel([FromBody] CancelReservationRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var (success, message) = await _queueService.CancelReservationAsync(currentUserId, currentUserRole, request.ReservationId);
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
