using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

/// <summary>
/// [S16] Giao diện dịch vụ Hạng Hội viên 4 cấp, Chuyển đổi gói và Hủy gói.
/// </summary>
/// <remarks>Triển khai bởi: S16MembershipTierService.</remarks>
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

/// <summary>
/// [S17] Giao diện dịch vụ Ví Bảo Lãnh tiền ảo, Tạo đơn thanh toán, Xác thực chữ ký HMAC-SHA512 và Hoàn cọc.
/// </summary>
/// <remarks>Triển khai bởi: S17SandboxPaymentService.</remarks>
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

/// <summary>
/// [S18] Giao diện dịch vụ Trung tâm Thông báo, Xác minh OTP Email và Gửi chiến dịch nhắc hạn.
/// </summary>
/// <remarks>Triển khai bởi: S18NotificationHubService.</remarks>
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

/// <summary>
/// [S19] Giao diện dịch vụ Khiếu nại & Phân xử Tranh chấp tình trạng sách.
/// </summary>
/// <remarks>Triển khai bởi: S19DisputeResolutionService.</remarks>
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

/// <summary>
/// [S20] Giao diện dịch vụ Quản trị Câu lạc bộ Sách & Điểm trạm giao nhận cộng đồng.
/// </summary>
/// <remarks>Triển khai bởi: S20ExchangeHubClubService.</remarks>
public interface IS20ExchangeHubClubService
{
    Task<S20ExchangeHubClubPageViewModel> GetExchangeHubPageAsync(
        string currentUserId,
        string currentUserRole,
        int? selectedHubId = null,
        string? clubKeyword = null,
        string hubTypeFilter = "all",
        string memberStatusFilter = "all",
        string orderStatusFilter = "all",
        int memberPage = 1,
        int orderPage = 1);

    Task<(bool Success, string Message)> RegisterCopyDropoffAtHubAsync(
        string currentUserId,
        string currentUserRole,
        S20RegisterCopyDropoffDto request);

    Task<(bool Success, string Message)> CuratorVerifyPhysicalCopyAsync(
        string curatorUserId,
        string curatorUserRole,
        S20CuratorVerifyCopyDto request);

    Task<(bool Success, string Message)> CuratorProcessHubHandoverAsync(
        string curatorUserId,
        string curatorUserRole,
        S20CuratorHandoverActionDto request);

    Task<(bool Success, string Message)> UpdateHubStationInfoAsync(
        string curatorUserId,
        string curatorUserRole,
        S20UpdateHubStationDto request);

    Task<(bool Success, string Message, int HubId)> CreateClubAsync(
        string adminUserId,
        S20CreateClubDto request);

    Task<(bool Success, string Message)> AssignClubLeaderAsync(
        string currentUserId,
        string currentUserRole,
        S20AssignClubLeaderDto request);

    Task<(bool Success, string Message)> AddClubMemberAsync(
        string currentUserId,
        string currentUserRole,
        S20AddClubMemberDto request);

    Task<(bool Success, string Message)> JoinClubAsync(
        string currentUserId,
        S20JoinClubDto request);

    Task<(bool Success, string Message)> LeaveClubAsync(
        string currentUserId,
        int hubId);

    Task<(bool Success, string Message)> ReviewClubMemberAsync(
        string currentUserId,
        string currentUserRole,
        S20ReviewClubMemberDto request);

    Task<(bool Success, string Message)> UpdateClubMemberRoleAsync(
        string currentUserId,
        string currentUserRole,
        S20UpdateClubMemberRoleDto request);

    Task<(bool Success, string Message)> RemoveClubMemberAsync(
        string currentUserId,
        string currentUserRole,
        S20RemoveClubMemberDto request);

    Task<(bool Success, string Message)> CreateClubPostAsync(
        string currentUserId,
        string currentUserRole,
        S20CreateClubPostDto request);

    Task<(bool Success, string Message)> DeleteClubPostAsync(
        string currentUserId,
        string currentUserRole,
        S20DeleteClubPostDto request);
}

/// <summary>
/// [S21] Giao diện dịch vụ Quầy Nghiệp vụ Thủ thư hợp nhất (Quét mã vạch, Cho mượn trực tiếp, Xử lý phiếu mượn & Quản lý tồn kho).
/// </summary>
/// <remarks>Triển khai bởi: S21LibrarianDeskService.</remarks>
public interface IS21LibrarianDeskService
{
    Task<S21LibrarianDeskPageViewModel> GetLibrarianDeskPageAsync(
        string currentUserId,
        string currentUserRole,
        string? loanKeyword = null,
        string loanChannel = "all",
        string loanStatus = "all",
        int loanPage = 1,
        string? copyKeyword = null,
        string copyStatus = "all",
        int? copyLibraryId = null,
        int copyPage = 1);

    Task<(bool Success, string Message, string ActionPerformed)> ProcessQuickBarcodeScanAsync(
        string librarianUserId,
        S21QuickScanBarcodeDto request);

    Task<(bool Success, string Message)> ProcessUnifiedLoanActionAsync(
        string staffUserId,
        string staffUserRole,
        S21ProcessLoanActionDto request);

    Task<(bool Success, string Message)> DirectLendBookAtDeskAsync(
        string staffUserId,
        string staffUserRole,
        S21DirectLendBookDto request);

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

/// <summary>
/// [S22] Giao diện dịch vụ Quản trị Hệ thống, Thống kê biểu đồ, Phân quyền tài khoản và Duyệt Chủ nhiệm CLB.
/// </summary>
/// <remarks>Triển khai bởi: S22AdminAnalyticsService.</remarks>
public interface IS22AdminAnalyticsService
{
    Task<S22AdminAnalyticsPageViewModel> GetAdminAnalyticsPageAsync(
        string currentUserId,
        string currentUserRole,
        S22AdminAnalyticsFilterDto? filter = null);

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
