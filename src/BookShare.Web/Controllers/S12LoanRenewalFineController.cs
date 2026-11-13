using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
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
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _renewalFineService.GetLoanRenewalFinePageAsync(currentUserId);
        return View("~/Views/Modules/S12_LoanRenewalFine/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenewLoan([FromBody] RenewLoanRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, newDueDate, newRenewCount) = await _renewalFineService.RenewLoanAsync(currentUserId, request.LoanId);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Gia hạn thời gian mượn sách thành công (S12)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Gia hạn thời gian mượn thành công (S12)",
            message,
            newDueDate = newDueDate?.ToString("dd/MM/yyyy"),
            newRenewCount
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssessFine([FromBody] AssessOverdueFineRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-lib-01";
        var (success, message, fineAmount, transactionId) = await _renewalFineService.AssessOverdueFineAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã tính phí phạt trễ hạn & lập phiếu thu (S12)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã tính phí phạt trễ hạn (S12)",
            message,
            fineAmount,
            transactionId
        });
    }
}
