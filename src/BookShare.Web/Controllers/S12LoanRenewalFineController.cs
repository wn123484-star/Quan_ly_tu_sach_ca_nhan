using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S12LoanRenewalFineController : Controller
{
    private readonly IS12LoanRenewalFineService _renewalFineService;

    public S12LoanRenewalFineController(IS12LoanRenewalFineService renewalFineService)
    {
        _renewalFineService = renewalFineService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUserRole = User.Identity?.IsAuthenticated == true
            ? (User.FindFirstValue(ClaimTypes.Role) ?? "User")
            : "Guest";
        var vm = await _renewalFineService.GetLoanRenewalFinePageAsync(currentUserId, currentUserRole);
        return View("~/Views/Modules/S12_LoanRenewalFine/Index.cshtml", vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenewLoan([FromBody] RenewLoanRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";
        try
        {
            var (success, message, newDueDate, newRenewCount) = await _renewalFineService.RenewLoanAsync(
                currentUserId,
                currentUserRole,
                request.LoanId);
            if (!success)
            {
                return BadRequest(new { message });
            }

            TempData["StatusCode"] = 200;
            TempData["StatusTitle"] = "Gia hạn thời gian mượn sách thành công";
            TempData["StatusDetail"] = message;

            return Ok(new
            {
                title = "Gia hạn thời gian mượn thành công",
                message,
                newDueDate = newDueDate?.ToString("dd/MM/yyyy"),
                newRenewCount
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize(Roles = "Librarian,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssessFine([FromBody] AssessOverdueFineRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var (success, message, fineAmount, transactionId) = await _renewalFineService.AssessOverdueFineAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã tính phí phạt trễ hạn & lập phiếu thu";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã tính phí phạt trễ hạn",
            message,
            fineAmount,
            transactionId
        });
    }
}
