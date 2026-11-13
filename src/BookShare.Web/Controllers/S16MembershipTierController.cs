using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

public class S16MembershipTierController : Controller
{
    private readonly IS16MembershipTierService _membershipService;

    public S16MembershipTierController(IS16MembershipTierService membershipService)
    {
        _membershipService = membershipService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var vm = await _membershipService.GetMembershipTierPageAsync(currentUserId);
        return View("~/Views/Modules/S16_MembershipTier/Index.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchMembership([FromBody] S16SwitchMembershipRequestDto request)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        var (success, message, createdTransactionId) = await _membershipService.SwitchOrExtendMembershipAsync(currentUserId, request);
        if (!success)
        {
            return BadRequest(new { message });
        }

        TempData["StatusCode"] = 200;
        TempData["StatusTitle"] = "Đã cập nhật Gói Thành viên & Hạn mức mượn (S16)";
        TempData["StatusDetail"] = message;

        return Ok(new
        {
            title = "Đã cập nhật Gói Thành viên (S16)",
            message,
            createdTransactionId
        });
    }
}
