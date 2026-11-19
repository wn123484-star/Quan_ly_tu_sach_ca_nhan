namespace BookShare.Web.Models.ViewModels;

// [BS-MOBILE-SUPERAPP] ViewModel tổng hợp cho BookShare Mobile Super-App 5 Tab (/MobileApp)
public class MobileAppPageViewModel
{
    public bool IsAuthenticated { get; set; }
    public string CurrentUserId { get; set; } = string.Empty;
    public string FullName { get; set; } = "Khách BookShare";
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public int TrustScore { get; set; } = 100;
    public WalletSummaryDto Wallet { get; set; } = new();
    public ReadingStudioPageViewModel Studio { get; set; } = new();
    public List<MobileActiveRentalDto> ActiveRentals { get; set; } = new();
    public List<S18NotificationItemDto> Notifications { get; set; } = new();
    public int UnreadNotificationsCount { get; set; }
    public List<DemoRoleAccountDto> DemoAccounts { get; set; } = new();
}

public class MobileActiveRentalDto
{
    public int LoanId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string LoanType { get; set; } = "Library";
    public DateTime? DueDate { get; set; }
    public decimal RentalFeeTotal { get; set; }
    public decimal DepositHeld { get; set; }
    public string EscrowStatus { get; set; } = "None";
    public string Status { get; set; } = "Active";
    public int BookId { get; set; }
}

public class MobileSignInRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
