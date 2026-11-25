using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

/// <summary>
/// [CORE-QR-HANDOVER] Controller Giao nhận & Hoàn trả sách trực tiếp bằng mã QR hai chiều (phát hành mã QR tạm thời và quét xác nhận bàn giao/trả sách).
/// </summary>
/// <remarks>RBAC: Yêu cầu đăng nhập; Service kiểm tra chặt quyền của Chủ sách, Người mượn, Trưởng/Phó CLB, Thủ thư hoặc Admin trước khi chốt giao dịch.</remarks>
[Route("api/handover-qr")]
public class HandoverQrController : Controller
{
    private readonly IBookHandoverQrService _handoverQrService;

    public HandoverQrController(IBookHandoverQrService handoverQrService)
    {
        _handoverQrService = handoverQrService;
    }

    [HttpPost("create")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateHandoverQrRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message, data) = await _handoverQrService.CreateHandoverQrAsync(
                currentUserId,
                currentUserRole,
                request);

            if (!success || data == null)
            {
                return BadRequest(new { message });
            }

            return Ok(new
            {
                title = "Đã khởi tạo mã QR bàn giao sách",
                message,
                data
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost("confirm")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm([FromBody] ConfirmHandoverQrRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message, actionPerformed) = await _handoverQrService.ConfirmHandoverQrAsync(
                currentUserId,
                currentUserRole,
                request);

            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = actionPerformed == "Handover"
                ? "Đã xác nhận nhận sách qua QR"
                : "Đã xác nhận hoàn trả sách qua QR";
            TempData["StatusDetail"] = message;

            return Ok(new
            {
                title = TempData["StatusTitle"]!.ToString(),
                message,
                actionPerformed
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
