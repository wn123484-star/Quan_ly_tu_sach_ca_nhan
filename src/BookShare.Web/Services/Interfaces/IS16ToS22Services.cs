using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

public interface IS16MembershipTierService
{
    Task<S16MembershipTierPageViewModel> GetMembershipTierPageAsync(string currentUserId);

    Task<(bool Success, string Message, int? CreatedTransactionId)> SwitchOrExtendMembershipAsync(
        string currentUserId,
        S16SwitchMembershipRequestDto request);
}

public interface IS17SandboxPaymentService
{
    Task<S17SandboxPaymentPageViewModel> GetSandboxPaymentPageAsync(string currentUserId);

    Task<(bool Success, string Message, int TransactionId, string RawPayload, string ExpectedHmacSignature)> CreateSandboxOrderAsync(
        string currentUserId,
        S17CreateSandboxOrderDto request);

    Task<(bool Success, string Message)> VerifyCallbackAndCompleteTransactionAsync(
        string currentUserId,
        S17SandboxCallbackDto callback);

    Task<(bool Success, string Message)> RefundSimulatedDepositAsync(
        string currentUserId,
        int transactionId);
}

public interface IS18NotificationHubService
{
    Task<S18NotificationHubPageViewModel> GetNotificationHubPageAsync(
        string currentUserId,
        string typeFilter = "all");

    Task<(bool Success, string Message)> MarkNotificationReadAsync(
        string currentUserId,
        int notificationId);

    Task<(bool Success, string Message, int UpdatedCount)> MarkAllNotificationsReadAsync(
        string currentUserId);

    Task<(bool Success, string Message, int SentCount)> TriggerResendEmailCampaignAsync(
        string currentUserId,
        S18TriggerResendCampaignDto request);
}

public interface IS19DisputeResolutionService
{
    Task<S19DisputeResolutionPageViewModel> GetDisputeResolutionPageAsync(
        string currentUserId,
        string currentUserRole);

    Task<(bool Success, string Message, int DisputeId)> CreateDisputeAsync(
        string currentUserId,
        S19CreateDisputeRequestDto request);

    Task<(bool Success, string Message)> ResolveDisputeByAdminAsync(
        string adminUserId,
        S19AdminResolveDisputeDto request);
}

public interface IS20ExchangeHubClubService
{
    Task<S20ExchangeHubClubPageViewModel> GetExchangeHubPageAsync(
        string currentUserId,
        string currentUserRole);

    Task<(bool Success, string Message)> RegisterCopyDropoffAtHubAsync(
        string currentUserId,
        S20RegisterCopyDropoffDto request);

    Task<(bool Success, string Message)> CuratorVerifyPhysicalCopyAsync(
        string curatorUserId,
        S20CuratorVerifyCopyDto request);

    Task<(bool Success, string Message)> CuratorProcessHubHandoverAsync(
        string curatorUserId,
        S20CuratorHandoverActionDto request);
}

public interface IS21LibrarianDeskService
{
    Task<S21LibrarianDeskPageViewModel> GetLibrarianDeskPageAsync(
        string currentUserId,
        string currentUserRole);

    Task<(bool Success, string Message, string ActionPerformed)> ProcessQuickBarcodeScanAsync(
        string librarianUserId,
        S21QuickScanBarcodeDto request);

    Task<(bool Success, string Message, List<string> GeneratedBarcodes)> BatchImportSystemCopiesAsync(
        string librarianUserId,
        S21BatchImportCopiesDto request);

    Task<(bool Success, string Message)> UpdateCopyDamageOrLostAsync(
        string librarianUserId,
        S21UpdateCopyStateDto request);
}

public interface IS22AdminAnalyticsService
{
    Task<S22AdminAnalyticsPageViewModel> GetAdminAnalyticsPageAsync(
        string currentUserId,
        string currentUserRole);

    Task<(bool Success, string Message)> ChangeUserRoleAsync(
        string adminUserId,
        S22ChangeUserRoleDto request);

    Task<(bool Success, string Message)> ToggleUserLockoutAsync(
        string adminUserId,
        S22ToggleUserLockDto request);
}
