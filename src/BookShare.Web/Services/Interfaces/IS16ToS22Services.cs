using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

public interface IS16MembershipTierService
{
    Task<S16MembershipTierPageViewModel> GetMembershipTierPageAsync(string currentUserId);

    Task<(bool Success, string Message, S16TierSwitchPreviewResultDto? Preview)> PreviewTierSwitchAsync(
        string currentUserId,
        string targetTier,
        int durationMonths);

    Task<(bool Success, string Message, int? CreatedTransactionId, string? RawPayload, string? ExpectedHmacSignature)> SwitchOrExtendMembershipAsync(
        string currentUserId,
        S16SwitchMembershipRequestDto request);

    Task<(bool Success, string Message)> CancelMembershipAsync(
        string currentUserId,
        string? reason = null);
}

public interface IS17SandboxPaymentService
{
    Task<S17SandboxPaymentPageViewModel> GetSandboxPaymentPageAsync(
        string currentUserId,
        string currentUserRole = "User",
        int? selectedTxId = null);

    Task<(bool Success, string Message, int TransactionId, string RawPayload, string ExpectedHmacSignature)> CreateSandboxOrderAsync(
        string currentUserId,
        S17CreateSandboxOrderDto request);

    Task<(bool Success, string Message)> VerifyCallbackAndCompleteTransactionAsync(
        string currentUserId,
        string currentUserRole,
        S17SandboxCallbackDto callback);

    Task<(bool Success, string Message)> RefundSimulatedDepositAsync(
        string currentUserId,
        string currentUserRole,
        int transactionId);

    Task<(bool Success, string Message)> CancelPendingTransactionAsync(
        string currentUserId,
        string currentUserRole,
        int transactionId);

    Task<decimal> GetWalletBalanceAsync(string userId);

    Task<(bool Success, string Message, decimal NewBalance, int TransactionId, bool RateLimited)> TopUpVirtualWalletAsync(
        string currentUserId,
        decimal amount,
        string? note = null,
        string? idempotencyKey = null);

    Task<(bool Success, string Message, S17QrInvoicePreviewResultDto? Invoice)> PreviewQrInvoiceAsync(
        string currentUserId,
        string currentUserRole,
        string qrPayload);

    Task<(bool Success, string Message, decimal NewBalance)> ConfirmQrInvoicePaymentAsync(
        string currentUserId,
        string currentUserRole,
        int transactionId);

    Task<(bool Found, string Status, decimal Amount, string? Description)> GetInvoiceStatusAsync(
        string currentUserId,
        string currentUserRole,
        int transactionId);

    string BuildCanonicalPayload(
        int txId,
        string userId,
        string txType,
        decimal amount,
        string gateway,
        int? loanId);

    string ComputeHmacSha512(string rawData);
}

public interface IS18NotificationHubService
{
    Task<S18NotificationHubPageViewModel> GetNotificationHubPageAsync(
        string currentUserId,
        string currentUserRole = "User",
        string typeFilter = "all",
        bool viewAll = false);

    Task<S18RecentPopupResultDto> GetRecentPopupNotificationsAsync(
        string currentUserId,
        int take = 8);

    Task<(bool Success, string Message)> MarkNotificationReadAsync(
        string currentUserId,
        int notificationId);

    Task<(bool Success, string Message, int UpdatedCount)> MarkAllNotificationsReadAsync(
        string currentUserId);

    Task<(bool Success, string Message, int SentCount)> TriggerResendEmailCampaignAsync(
        string currentUserId,
        S18TriggerResendCampaignDto request);

    Task<(bool Success, string Message)> DeleteNotificationAsync(
        string currentUserId,
        string currentUserRole,
        int notificationId);
}

public interface IS19DisputeResolutionService
{
    Task<S19DisputeResolutionPageViewModel> GetDisputeResolutionPageAsync(
        string currentUserId,
        string currentUserRole);

    Task<(bool Success, string Message, int DisputeId)> CreateDisputeAsync(
        string currentUserId,
        string currentUserRole,
        S19CreateDisputeRequestDto request);

    Task<(bool Success, string Message, S19ResolveDisputeOutcomeDto? Outcome)> ResolveDisputeByAdminAsync(
        string adminUserId,
        S19AdminResolveDisputeDto request);

    Task<(bool Success, string Message)> UpdateDisputeReasonAsync(
        string currentUserId,
        string currentUserRole,
        S19UpdateDisputeRequestDto request);

    Task<(bool Success, string Message)> DeleteDisputeAsync(
        string currentUserId,
        string currentUserRole,
        int disputeId);
}

public interface IS20ExchangeHubClubService
{
    Task<S20ExchangeHubClubPageViewModel> GetExchangeHubPageAsync(
        string currentUserId,
        string currentUserRole);

    Task<(bool Success, string Message)> RegisterCopyDropoffAtHubAsync(
        string currentUserId,
        string currentUserRole,
        S20RegisterCopyDropoffDto request);

    Task<(bool Success, string Message)> CuratorVerifyPhysicalCopyAsync(
        string curatorUserId,
        S20CuratorVerifyCopyDto request);

        Task<(bool Success, string Message)> CuratorProcessHubHandoverAsync(
            string curatorUserId,
            S20CuratorHandoverActionDto request);

        Task<(bool Success, string Message)> UpdateHubStationInfoAsync(
            string curatorUserId,
            S20UpdateHubStationDto request);
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

        Task<(bool Success, string Message)> DeleteSystemCopyAsync(
            string librarianUserId,
            int bookCopyId);
    }

public interface IS22AdminAnalyticsService
{
    Task<S22AdminAnalyticsPageViewModel> GetAdminAnalyticsPageAsync(
        string currentUserId,
        string currentUserRole);

    Task<(bool Success, string Message)> ChangeUserRoleAsync(
        string adminUserId,
        S22ChangeUserRoleDto request);

    Task<(bool Success, string Message)> ReviewRoleUpgradeAsync(
        string adminUserId,
        ReviewRoleUpgradeRequestDto request);

        Task<(bool Success, string Message)> ToggleUserLockoutAsync(
            string adminUserId,
            S22ToggleUserLockDto request);

        Task<(bool Success, string Message)> ManageSampleDataAsync(
            string adminUserId,
            string actionMode);

        Task<(bool Success, string Message)> DeleteUserAsync(
            string adminUserId,
            string targetUserId);
    }
