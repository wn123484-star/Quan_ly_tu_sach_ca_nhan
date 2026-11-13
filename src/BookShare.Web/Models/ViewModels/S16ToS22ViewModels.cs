namespace BookShare.Web.Models.ViewModels;

// =========================================================================
// S16: MEMBERSHIP TIER (FREE vs PREMIUM & QUOTA CALCULATOR)
// =========================================================================
public class S16MembershipTierPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public string CurrentUserEmail { get; set; } = string.Empty;
    public int CurrentTrustScore { get; set; }
    public string ActiveTier { get; set; } = "Free"; // Free / Premium
    public int BaseMaxConcurrentLoans { get; set; } = 2;
    public int LendQuotaBonus { get; set; }
    public int TotalAllowedLoans => BaseMaxConcurrentLoans + LendQuotaBonus;
    public int CurrentActiveLoansCount { get; set; }
    public int RemainingLoanQuota => Math.Max(0, TotalAllowedLoans - CurrentActiveLoansCount);
    public DateTime StartDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int DaysRemaining { get; set; }
    public List<S16MembershipRecordDto> AllMemberships { get; set; } = new();
}

public class S16MembershipRecordDto
{
    public int MembershipId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string Tier { get; set; } = "Free";
    public int MaxConcurrentLoans { get; set; }
    public int LendQuotaBonus { get; set; }
    public int TotalQuota => MaxConcurrentLoans + LendQuotaBonus;
    public DateTime StartDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsActive { get; set; }
}

public class S16SwitchMembershipRequestDto
{
    public string TargetTier { get; set; } = "Premium"; // Free / Premium
    public int DurationMonths { get; set; } = 12;
    public string PaymentGateway { get; set; } = "VNPaySandbox";
}

// =========================================================================
// S17: SANDBOX PAYMENT (VNPAY / MOMO SANDBOX + HMAC-SHA512 VERIFICATION)
// =========================================================================
public class S17SandboxPaymentPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public string CurrentTier { get; set; } = "Free";
    public decimal TotalCompletedAmount { get; set; }
    public decimal TotalPendingAmount { get; set; }
    public int TotalVerifiedHmacCount { get; set; }
    public List<S17UnpaidFineLoanDto> UnpaidFineLoans { get; set; } = new();
    public List<S17TransactionItemDto> Transactions { get; set; } = new();
}

public class S17UnpaidFineLoanDto
{
    public int LoanId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public decimal FineAmount { get; set; }
}

public class S17TransactionItemDto
{
    public int TransactionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public int? LoanId { get; set; }
    public string TransactionType { get; set; } = string.Empty; // Membership / Fine / SimulatedDeposit
    public decimal Amount { get; set; }
    public string PaymentGateway { get; set; } = "VNPaySandbox"; // VNPaySandbox / MoMoSandbox
    public string PaymentStatus { get; set; } = "Pending"; // Pending / Completed / Failed / Refunded
    public string? HmacSignature { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class S17CreateSandboxOrderDto
{
    public string TransactionType { get; set; } = "Membership"; // Membership / Fine / SimulatedDeposit
    public decimal Amount { get; set; } = 99000m;
    public string PaymentGateway { get; set; } = "VNPaySandbox"; // VNPaySandbox / MoMoSandbox
    public int? LoanId { get; set; }
}

public class S17SandboxCallbackDto
{
    public int TransactionId { get; set; }
    public string RawPayload { get; set; } = string.Empty;
    public string HmacSignature { get; set; } = string.Empty;
    public string ResponseCode { get; set; } = "00"; // 00 = Thành công, 24 = Hủy
}

public class S17RefundDepositDto
{
    public int TransactionId { get; set; }
}

// =========================================================================
// S18: NOTIFICATION HUB (WEB CENTER & RESEND EMAIL TRIGGERS)
// =========================================================================
public class S18NotificationHubPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
    public int EmailSentCount { get; set; }
    public string TypeFilter { get; set; } = "all";
    public List<S18NotificationItemDto> Notifications { get; set; } = new();
}

public class S18NotificationItemDto
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = "System"; // DueReminder / LoanApproved / QueueAvailable / Dispute / System
    public bool IsRead { get; set; }
    public bool EmailSentViaResend { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class S18MarkNotificationRequestDto
{
    public int NotificationId { get; set; }
}

public class S18TriggerResendCampaignDto
{
    public string CampaignType { get; set; } = "DueReminder"; // DueReminder / QueueAvailable / Custom
    public string? CustomTitle { get; set; }
    public string? CustomContent { get; set; }
}

// =========================================================================
// S19: DISPUTE RESOLUTION (USER REPORT & ADMIN ADJUDICATION)
// =========================================================================
public class S19DisputeResolutionPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = "User";
    public int OpenDisputesCount { get; set; }
    public int ResolvedDisputesCount { get; set; }
    public List<S19LoanForDisputeDto> EligibleLoans { get; set; } = new();
    public List<S19DisputeDetailDto> Disputes { get; set; } = new();
}

public class S19LoanForDisputeDto
{
    public int LoanId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string BorrowerId { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public string LoanStatus { get; set; } = string.Empty;
    public string? HandoverImageUrl { get; set; }
    public string? ReturnImageUrl { get; set; }
}

public class S19DisputeDetailDto
{
    public int DisputeId { get; set; }
    public int LoanId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string BorrowerId { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public int BorrowerTrustScore { get; set; }
    public string RaisedByUserId { get; set; } = string.Empty;
    public string RaisedByName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Open"; // Open / UnderReview / Resolved / Rejected
    public string? ResolvedByAdminName { get; set; }
    public string? ResolutionNote { get; set; }
    public string HandoverGrade { get; set; } = "Good";
    public string HandoverImageUrl { get; set; } = string.Empty;
    public string ReturnGrade { get; set; } = "Damaged";
    public string ReturnImageUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class S19CreateDisputeRequestDto
{
    public int LoanId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? EvidenceImageUrl { get; set; }
}

public class S19AdminResolveDisputeDto
{
    public int DisputeId { get; set; }
    public string DecisionStatus { get; set; } = "Resolved"; // Resolved / Rejected / UnderReview
    public int TrustScorePenalty { get; set; } = 10; // Điểm trừ TrustScore (0 - 30)
    public decimal DepositDeductionAmount { get; set; } = 0m;
    public string CopyTargetStatus { get; set; } = "Damaged"; // Available / Damaged / Lost
    public string ResolutionNote { get; set; } = string.Empty;
}

// =========================================================================
// S20: EXCHANGE HUB & BOOK CLUB (CURATOR P2P VERIFICATION & DROP-OFF HUB)
// =========================================================================
public class S20ExchangeHubClubPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = "User";
    public int TotalHubsCount { get; set; }
    public int TotalCopiesAtHubs { get; set; }
    public int PendingP2PHubOrdersCount { get; set; }
    public List<S20HubStationDto> Hubs { get; set; } = new();
    public List<S20HubBookCopyDto> HubCopies { get; set; } = new();
    public List<S20HubLoanOrderDto> HubP2POrders { get; set; } = new();
}

public class S20HubStationDto
{
    public int HubId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ManagerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string HubType { get; set; } = "ClubStation"; // Library / DropoffHub / ClubStation
    public string OpenHours { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int CopiesStoredCount { get; set; }
    public int ActiveLoanRequestsCount { get; set; }
}

public class S20HubBookCopyDto
{
    public int BookCopyId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerType { get; set; } = "User";
    public int? HubId { get; set; }
    public string HubName { get; set; } = string.Empty;
    public string ShelfLocation { get; set; } = string.Empty;
    public string Condition { get; set; } = "Good";
    public string Status { get; set; } = "Available";
    public bool IsLendable { get; set; }
}

public class S20HubLoanOrderDto
{
    public int LoanRequestId { get; set; }
    public int? LoanId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public int BorrowerTrustScore { get; set; }
    public string HubName { get; set; } = string.Empty;
    public string RequestStatus { get; set; } = string.Empty;
    public string CopyStatus { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
}

public class S20RegisterCopyDropoffDto
{
    public int BookCopyId { get; set; }
    public int HubId { get; set; }
    public string? Note { get; set; }
}

public class S20CuratorVerifyCopyDto
{
    public int BookCopyId { get; set; }
    public int HubId { get; set; }
    public string VerifiedCondition { get; set; } = "Good"; // New / Good / Fair / Worn
    public string ShelfLocation { get; set; } = "Trạm CLB-K1";
}

public class S20CuratorHandoverActionDto
{
    public int LoanRequestId { get; set; }
    public string ActionType { get; set; } = "CheckInDropoff"; // CheckInDropoff (InTransit) / HandoverToBorrower (Borrowed) / ConfirmReturnAtHub (Returned)
}

// =========================================================================
// S21: LIBRARIAN DESK (BARCODE QUICK SCAN, BATCH IMPORT, DAMAGED/LOST & OVERDUE)
// =========================================================================
public class S21LibrarianDeskPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = "User";
    public int TotalSystemCopies { get; set; }
    public int AvailableSystemCopies { get; set; }
    public int BorrowedSystemCopies { get; set; }
    public int DamagedOrLostCopies { get; set; }
    public int OverdueLoansCount { get; set; }
    public List<S21BookOptionDto> Books { get; set; } = new();
    public List<S21LibraryOptionDto> Libraries { get; set; } = new();
    public List<S21SystemCopyDeskItemDto> SystemCopies { get; set; } = new();
    public List<S21OverdueReaderDto> OverdueLoans { get; set; } = new();
}

public class S21BookOptionDto
{
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
}

public class S21LibraryOptionDto
{
    public int LibraryId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class S21SystemCopyDeskItemDto
{
    public int BookCopyId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string LibraryName { get; set; } = string.Empty;
    public string ShelfLocation { get; set; } = string.Empty;
    public string Condition { get; set; } = "Good";
    public string Status { get; set; } = "Available";
    public int? ActiveLoanId { get; set; }
    public int? PendingRequestId { get; set; }
    public string? CurrentBorrowerName { get; set; }
}

public class S21OverdueReaderDto
{
    public int LoanId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string BorrowerId { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public string BorrowerEmail { get; set; } = string.Empty;
    public int BorrowerTrustScore { get; set; }
    public DateTime DueDate { get; set; }
    public int OverdueDays { get; set; }
    public decimal EstimatedFine { get; set; }
}

public class S21QuickScanBarcodeDto
{
    public string Barcode { get; set; } = string.Empty;
    public string DeskAction { get; set; } = "AutoDetect"; // AutoDetect / Checkout / Checkin
    public string? BorrowerUserId { get; set; }
    public string ReturnCondition { get; set; } = "Good";
}

public class S21BatchImportCopiesDto
{
    public int BookId { get; set; }
    public int LibraryId { get; set; } = 1;
    public int Quantity { get; set; } = 2;
    public string ShelfLocation { get; set; } = "Kệ CNTT-A1";
    public string Condition { get; set; } = "New";
}

public class S21UpdateCopyStateDto
{
    public int BookCopyId { get; set; }
    public string NewStatus { get; set; } = "Damaged"; // Available / Damaged / Lost
    public string NewCondition { get; set; } = "Worn"; // New / Good / Fair / Worn
    public string? Note { get; set; }
}

// =========================================================================
// S22: ADMIN ANALYTICS, USER/ROLE GOVERNANCE & HANGFIRE DAILY JOBS
// =========================================================================
public class S22AdminAnalyticsPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = "User";

    // KPI Cards
    public int TotalUsers { get; set; }
    public int TotalBooks { get; set; }
    public int TotalCopies { get; set; }
    public int TotalLoans { get; set; }
    public int LibraryLoansCount { get; set; }
    public int P2PLoansCount { get; set; }
    public decimal TotalMembershipRevenue { get; set; }
    public decimal TotalFineRevenue { get; set; }
    public decimal TotalSimulatedDeposits { get; set; }
    public int TotalOpenDisputes { get; set; }
    public int TotalAiMessages { get; set; }

    // Charts Data
    public int AvailableCopiesCount { get; set; }
    public int BorrowedCopiesCount { get; set; }
    public int ReservedCopiesCount { get; set; }
    public int DamagedOrLostCopiesCount { get; set; }

    // Governance & Logs
    public List<S22AdminUserItemDto> Users { get; set; } = new();
    public List<S22SystemAuditLogDto> AuditLogs { get; set; } = new();
    public List<S22BackgroundJobInfoDto> BackgroundJobs { get; set; } = new();
}

public class S22AdminUserItemDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = "User";
    public int TrustScore { get; set; }
    public int LendQuotaBonus { get; set; }
    public string MembershipTier { get; set; } = "Free";
    public bool IsLockedOut { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class S22SystemAuditLogDto
{
    public string Category { get; set; } = string.Empty;
    public string BadgeClass { get; set; } = "bs-badge-info";
    public string ActorName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class S22BackgroundJobInfoDto
{
    public string JobKey { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
    public string CronSchedule { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Scheduled";
}

public class S22ChangeUserRoleDto
{
    public string UserId { get; set; } = string.Empty;
    public string NewRole { get; set; } = "User"; // User / Curator / Librarian / Admin
}

public class S22ToggleUserLockDto
{
    public string UserId { get; set; } = string.Empty;
    public bool LockAccount { get; set; }
}

public class S22TriggerJobRequestDto
{
    public string JobKey { get; set; } = "AllDailyJobs"; // DueReminder / OverdueFine / ExpireCleanup / AllDailyJobs
}
