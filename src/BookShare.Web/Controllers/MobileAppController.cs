using System.Security.Claims;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace BookShare.Web.Controllers;

/// <summary>
/// [BS-MOBILE-SUPERAPP] Controller riêng cho BookShare Mobile Super-App 5 Tab (/MobileApp).
/// Đồng bộ Cookie Auth 100% với Web PC, tuân thủ tuyệt đối kiến trúc 3 tầng (không gọi thẳng DbContext).
/// </summary>
public class MobileAppController : Controller
{
    private readonly IS01AuthOtpService _authService;
    private readonly IWalletService _walletService;
    private readonly IS03ReadingTrackerService _readingService;
    private readonly IS08LibraryLoanService _libraryLoanService;
    private readonly IS09P2PLoanService _p2pLoanService;
    private readonly IS18NotificationHubService _notifService;

    public MobileAppController(
        IS01AuthOtpService authService,
        IWalletService walletService,
        IS03ReadingTrackerService readingService,
        IS08LibraryLoanService libraryLoanService,
        IS09P2PLoanService p2pLoanService,
        IS18NotificationHubService notifService)
    {
        _authService = authService;
        _walletService = walletService;
        _readingService = readingService;
        _libraryLoanService = libraryLoanService;
        _p2pLoanService = p2pLoanService;
        _notifService = notifService;
    }

    [HttpGet("/MobileApp")]
    [HttpGet("/MobileApp/Index")]
    public async Task<IActionResult> Index(int? bookId = null)
    {
        bool isAuth = User.Identity?.IsAuthenticated ?? false;
        string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "u-user-01";
        string currentRole = User.FindFirstValue(ClaimTypes.Role) ?? "User";

        var authVm = await _authService.GetAuthPageViewModelAsync(isAuth ? currentUserId : null);
        var wallet = await _walletService.GetWalletSummaryAsync(currentUserId);
        var studio = await _readingService.GetReadingStudioAsync(currentUserId, bookId);
        var libVm = await _libraryLoanService.GetLibraryLoanFlowAsync(currentUserId, currentRole);
        var p2pVm = await _p2pLoanService.GetP2PLoanFlowAsync(currentUserId);
        var notifVm = await _notifService.GetNotificationHubPageAsync(currentUserId);

        var activeRentals = new List<MobileActiveRentalDto>();
        foreach (var l in libVm.LoanItems.Where(x => x.BorrowerId == currentUserId && x.LoanId.HasValue && x.LoanStatus != "Returned"))
        {
            activeRentals.Add(new MobileActiveRentalDto
            {
                LoanId = l.LoanId!.Value,
                BookId = l.BookId,
                Barcode = l.Barcode,
                BookTitle = l.BookTitle,
                LoanType = "Kho Thư Viện",
                DueDate = l.DueDate,
                RentalFeeTotal = l.RentalFeeTotal,
                DepositHeld = l.DepositHeld,
                EscrowStatus = l.EscrowStatus,
                Status = l.LoanStatus ?? "Active"
            });
        }
        foreach (var p in p2pVm.P2PItems.Where(x => x.BorrowerId == currentUserId && x.LoanId.HasValue && x.LoanStatus != "Returned"))
        {
            activeRentals.Add(new MobileActiveRentalDto
            {
                LoanId = p.LoanId!.Value,
                BookId = p.BookId,
                Barcode = p.Barcode,
                BookTitle = p.BookTitle,
                LoanType = "Thuê P2P",
                DueDate = p.DueDate,
                RentalFeeTotal = p.RentalFeeTotal,
                DepositHeld = p.DepositHeld,
                EscrowStatus = p.EscrowStatus,
                Status = p.LoanStatus ?? "Active"
            });
        }

        var vm = new MobileAppPageViewModel
        {
            IsAuthenticated = isAuth,
            CurrentUserId = currentUserId,
            FullName = isAuth ? (authVm.CurrentUserFullName ?? User.Identity?.Name ?? "Độc giả BookShare") : wallet.UserFullName,
            Email = authVm.CurrentUserEmail ?? string.Empty,
            Role = isAuth ? currentRole : "Guest",
            TrustScore = isAuth ? authVm.CurrentTrustScore : 100,
            Wallet = wallet,
            Studio = studio,
            ActiveRentals = activeRentals.OrderBy(r => r.DueDate).ToList(),
            Notifications = notifVm.Notifications.Take(12).ToList(),
            UnreadNotificationsCount = notifVm.UnreadCount,
            DemoAccounts = authVm.DemoAccounts
        };

        return View("~/Views/MobileApp/Index.cshtml", vm);
    }

    // [BS-MOBILE-AUTH] Đăng nhập ngay trong Mobile Super-App, cấp chung BookShare.AuthCookie với Web PC
    [HttpPost("/MobileApp/SignIn")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SignIn([FromBody] MobileSignInRequestDto request)
    {
        var result = await _authService.AuthenticateAsync(request.Email, request.Password);
        if (!result.Success || result.UserClaims == null)
        {
            return BadRequest(new { message = result.Message });
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.UserClaims.UserId),
            new(ClaimTypes.Email, result.UserClaims.Email),
            new(ClaimTypes.Name, result.UserClaims.FullName),
            new(ClaimTypes.Role, result.UserClaims.PrimaryRole),
            new("TrustScore", result.UserClaims.TrustScore.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });

        return Ok(new
        {
            title = $"Xin chào {result.UserClaims.FullName}",
            message = $"Đã đăng nhập BookShare Mobile (Vai trò: {result.UserClaims.PrimaryRole}) — đồng bộ phiên Cookie với Web PC!",
            fullName = result.UserClaims.FullName,
            role = result.UserClaims.PrimaryRole
        });
    }

    [HttpPost("/MobileApp/SignOut")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SignOutMobile()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { title = "Đã đăng xuất BookShare Mobile", message = "Phiên đăng nhập đã được đóng an toàn." });
    }
}
