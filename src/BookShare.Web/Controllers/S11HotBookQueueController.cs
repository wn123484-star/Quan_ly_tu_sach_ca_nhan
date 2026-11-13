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
        TempData["StatusTitle"] = "Đặt trước hàng chờ sách Hot thành công (S11)";
        TempData["StatusDetail"] = message;

        return Ok(new { title = "Đã xếp vào hàng chờ sách Hot (S11)", message, queuePosition });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NotifyTop([FromBody] NotifyTopQueueRequestDto request)
    {
        var (success, message) = await _queueService.NotifyFirstInQueueAsync(request.BookId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã thông báo cho người đứng đầu hàng chờ (S11)";
        TempData["StatusDetail"] = message;

        return Ok(new { title = "Đã kích hoạt thông báo hàng chờ (S11)", message });
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
        TempData["StatusTitle"] = "Đã hủy đặt trước & cập nhật hàng chờ (S11)";
        TempData["StatusDetail"] = message;

        return Ok(new { title = "Đã hủy phiếu đặt trước (S11)", message });
    }
}
