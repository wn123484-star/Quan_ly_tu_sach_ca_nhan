namespace BookShare.Web.Models.ViewModels;

// =========================================================================
// S16: MEMBERSHIP TIER (4 CẤP HỘI VIÊN & HẠN MỨC MƯỢN SÁCH — TIỀN ẢO NỘI BỘ)
// =========================================================================
public class S16MembershipTierPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public string CurrentUserEmail { get; set; } = string.Empty;
    public int CurrentTrustScore { get; set; }
    public decimal WalletBalance { get; set; }
    public string ActiveTier { get; set; } = "Free"; // Free / Standard / Premium / Scholar
    public string ActiveTierDisplayName { get; set; } = "Độc Giả Cơ Bản";
    public int BaseMaxConcurrentLoans { get; set; } = 2;
    public int MaxBorrowDays { get; set; } = 14;
    public int MaxRenewals { get; set; } = 1;
    public string AllowedBooksSummary { get; set; } = string.Empty;
    public int LendQuotaBonus { get; set; }
    public int TotalAllowedLoans => BaseMaxConcurrentLoans + LendQuotaBonus;
    public int CurrentActiveLoansCount { get; set; }
    public int RemainingLoanQuota => Math.Max(0, TotalAllowedLoans - CurrentActiveLoansCount);
    public DateTime StartDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int DaysRemaining { get; set; }
    public List<MembershipTierPlanDto> TierPlans { get; set; } = new();
    public List<S16MembershipRecordDto> AllMemberships { get; set; } = new();
}

public class MembershipTierPlanDto
{
    public string Tier { get; set; } = "Free";
    public string DisplayName { get; set; } = string.Empty;
    public string ShortTag { get; set; } = string.Empty;
    public int MaxConcurrentLoans { get; set; }
    public int MaxBorrowDays { get; set; }
    public int MaxRenewals { get; set; }
    public int RenewalDays { get; set; }
    public int PriorityRank { get; set; }
    public decimal Price1Month { get; set; }
    public decimal Price6Months { get; set; }
    public decimal Price12Months { get; set; }
    public string AllowedBooksSummary { get; set; } = string.Empty;
    public string DepositPolicy { get; set; } = string.Empty;
    public string QueuePriorityLabel { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
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
    public string TargetTier { get; set; } = "Premium"; // Free / Standard / Premium / Scholar
    public int DurationMonths { get; set; } = 12;
    public string PaymentGateway { get; set; } = "QrPay"; // QrPay / VirtualWallet
    public string PaymentMode { get; set; } = "QrInvoice"; // QrInvoice (mặc định tạo hóa đơn QR) / InstantWallet (trừ thẳng ví)
    public string? IdempotencyKey { get; set; }
}

public class S16PreviewTierSwitchDto
{
    public string TargetTier { get; set; } = "Premium";
    public int DurationMonths { get; set; } = 12;
}

public class S16TierSwitchPreviewResultDto
{
    public string CurrentTier { get; set; } = "Free";
    public string CurrentTierDisplayName { get; set; } = "Độc Giả Cơ Bản";
    public string TargetTier { get; set; } = "Premium";
    public string TargetTierDisplayName { get; set; } = string.Empty;
    public int DurationMonths { get; set; } = 12;
    public bool IsSwitch { get; set; }
    public bool IsSameActiveTier { get; set; }
    public int RemainingDays { get; set; }
    public decimal NewPlanPrice { get; set; }
    public decimal ProrationCredit { get; set; }
    public decimal SwitchFee { get; set; }
    public decimal TotalPayable { get; set; }
    public decimal WalletBalance { get; set; }
    public decimal ShortfallAmount => Math.Max(0m, TotalPayable - WalletBalance);
    public bool HasSufficientBalance => WalletBalance >= TotalPayable;
    public string FormulaNote { get; set; } = string.Empty;
}

public class S16CancelMembershipRequestDto
{
    public string? Reason { get; set; }
}

// =========================================================================
// S17: VÍ TIỀN ẢO MIỄN PHÍ & QUÉT QR THANH TOÁN NỘI BỘ (KHÔNG NGÂN HÀNG THẬT)
// =========================================================================
public class S17SandboxPaymentPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public string CurrentTier { get; set; } = "Free";
    public decimal WalletBalance { get; set; }
    public decimal TotalToppedUp { get; set; }
    public decimal TotalSpent { get; set; }
    public decimal TotalCompletedAmount { get; set; }
    public decimal TotalPendingAmount { get; set; }
    public int TotalVerifiedHmacCount { get; set; }
    public int? SelectedPendingTxId { get; set; }
    public string? SelectedQrPayload { get; set; }
    public string? SelectedDescription { get; set; }
    public decimal SelectedPendingAmount { get; set; }
    public string? SelectedRawPayload { get; set; }
    public string? SelectedExpectedHmac { get; set; }
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
    public string TransactionType { get; set; } = string.Empty; // Membership / Fine / SimulatedDeposit / WalletTopUp
    public decimal Amount { get; set; }
    public string PaymentGateway { get; set; } = "QrPay";
    public string PaymentStatus { get; set; } = "Pending"; // Pending / Completed / Failed / Refunded
    public string? TargetTier { get; set; }
    public int? DurationMonths { get; set; }
    public string? Description { get; set; }
    public string? ActionType { get; set; }
    public decimal SwitchFee { get; set; }
    public decimal ProrationCredit { get; set; }
    public decimal? BalanceBefore { get; set; }
    public decimal? BalanceAfter { get; set; }
    public string QrPayload => $"BOOKSHARE-PAY:{TransactionId}";
    public string? HmacSignature { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class S17CreateSandboxOrderDto
{
    public string TransactionType { get; set; } = "Membership"; // Membership / MembershipSwitch / Fine / SimulatedDeposit
    public decimal Amount { get; set; } = 49000m;
    public string PaymentGateway { get; set; } = "QrPay";
    public int? LoanId { get; set; }
    public string? TargetTier { get; set; }
    public int? DurationMonths { get; set; }
    public string? Description { get; set; }
    public string? ActionType { get; set; }
    public decimal SwitchFee { get; set; }
    public decimal ProrationCredit { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class S17WalletTopUpDto
{
    public decimal Amount { get; set; } = 100000m;
    public string? Note { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class S17PreviewQrInvoiceDto
{
    public string QrPayload { get; set; } = string.Empty;
}

public class S17QrInvoicePreviewResultDto
{
    public int TransactionId { get; set; }
    public string InvoiceCode => $"#TX-{TransactionId}";
    public string QrPayload => $"BOOKSHARE-PAY:{TransactionId}";
    public string OwnerFullName { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string ServiceLabel { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? TargetTier { get; set; }
    public int? DurationMonths { get; set; }
    public decimal Amount { get; set; }
    public decimal SwitchFee { get; set; }
    public decimal ProrationCredit { get; set; }
    public string? ActionType { get; set; }
    public string PaymentStatus { get; set; } = "Pending";
    public decimal WalletBalance { get; set; }
    public decimal BalanceAfterPayment => Math.Max(0m, WalletBalance - Amount);
    public bool HasSufficientBalance => WalletBalance >= Amount;
    public decimal ShortfallAmount => Math.Max(0m, Amount - WalletBalance);
    public DateTime CreatedAt { get; set; }
}

public class S17ConfirmQrInvoiceDto
{
    public int TransactionId { get; set; }
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

public class S17CancelTransactionDto
{
    public int TransactionId { get; set; }
}

public class S22DeleteUserDto
{
    public string UserId { get; set; } = string.Empty;
}

// =========================================================================
// S18: NOTIFICATION HUB (WEB CENTER & RESEND EMAIL TRIGGERS)
// =========================================================================
public class S18NotificationHubPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public string CurrentUserEmail { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
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
    public string DetailUrl { get; set; } = "/S18NotificationHub";
}

public class S18RecentPopupResultDto
{
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
    public List<S18NotificationItemDto> Items { get; set; } = new();
}

public class S18MarkNotificationRequestDto
{
    public int NotificationId { get; set; }
}

public class S18DeleteNotificationRequestDto
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

public class S19UpdateDisputeRequestDto
{
    public int DisputeId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class S19DeleteDisputeRequestDto
{
    public int DisputeId { get; set; }
}

public class S19AdminResolveDisputeDto
{
    public int DisputeId { get; set; }
    public string DecisionStatus { get; set; } = "Resolved"; // Resolved / Rejected / UnderReview
    public int TrustScorePenalty { get; set; } = 10; // Điểm trừ TrustScore (0 - 30)
    public decimal DepositDeductionAmount { get; set; } = 0m;
    public decimal CompensationAmount { get; set; } = 0m; // Khoản phạt bồi thường cần thanh toán qua S17 để mở khóa
    public string CopyTargetStatus { get; set; } = "Damaged"; // Available / Damaged / Lost
    public string ResolutionNote { get; set; } = string.Empty;
}

// =========================================================================
// S20: EXCHANGE HUB & BOOK CLUB (PER-CLUB GROUP GOVERNANCE, LEADER & DROP-OFF)
// =========================================================================
public class S20ExchangeHubClubPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = "User";
    public int TotalHubsCount { get; set; }
    public int TotalClubsCount { get; set; }
    public int TotalCopiesAtHubs { get; set; }
    public int PendingP2PHubOrdersCount { get; set; }

    // Club Filter & Selection
    public int SelectedHubId { get; set; }
    public string? ClubKeyword { get; set; }
    public string HubTypeFilter { get; set; } = "all"; // all / ClubStation / DropoffHub / Library
    public string OrderStatusFilter { get; set; } = "all";
    public string MemberStatusFilter { get; set; } = "all"; // all / Approved / Pending
    public int MemberPage { get; set; } = 1;
    public int MemberPageSize { get; set; } = 10;
    public int TotalFilteredMembers { get; set; }
    public int MemberTotalPages => MemberPageSize > 0 ? Math.Max(1, (int)Math.Ceiling(TotalFilteredMembers / (double)MemberPageSize)) : 1;
    public int OrderPage { get; set; } = 1;
    public int OrderPageSize { get; set; } = 10;
    public int TotalFilteredOrders { get; set; }
    public int OrderTotalPages => OrderPageSize > 0 ? Math.Max(1, (int)Math.Ceiling(TotalFilteredOrders / (double)OrderPageSize)) : 1;

    // Selected Club Permission Context
    public S20HubStationDto? SelectedHub { get; set; }
    public bool CanManageSelectedClub { get; set; }
    public bool IsSelectedClubLeader { get; set; }
    public bool IsCurrentUserClubMember { get; set; }
    public string? CurrentUserClubRole { get; set; }
    public string? CurrentUserClubStatus { get; set; }

    public List<S20HubStationDto> Hubs { get; set; } = new();
    public List<S20ClubMemberDto> SelectedClubMembers { get; set; } = new();
    public List<S20ClubPostDto> SelectedClubPosts { get; set; } = new();
    public List<S20UserOptionDto> CandidateUsers { get; set; } = new();
    public List<S20HubBookCopyDto> HubCopies { get; set; } = new();
    public List<S20HubLoanOrderDto> HubP2POrders { get; set; } = new();
}

public class S20HubStationDto
{
    public int HubId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ManagerUserId { get; set; } = string.Empty;
    public string ManagerName { get; set; } = string.Empty;
    public string ManagerEmail { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string HubType { get; set; } = "ClubStation"; // Library / DropoffHub / ClubStation
    public string OpenHours { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ClubRules { get; set; }
    public bool IsPublicClub { get; set; } = true;
    public bool IsActive { get; set; }
    public int CopiesStoredCount { get; set; }
    public int ActiveLoanRequestsCount { get; set; }
    public int ApprovedMembersCount { get; set; }
    public int PendingMembersCount { get; set; }
}

public class S20ClubMemberDto
{
    public int MemberId { get; set; }
    public int HubId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SystemRole { get; set; } = "User";
    public int TrustScore { get; set; }
    public string MemberRole { get; set; } = "Member"; // Leader / ViceLeader / Member
    public string Status { get; set; } = "Approved";   // Approved / Pending / Rejected
    public string? JoinNote { get; set; }
    public string? AddedByName { get; set; }
    public DateTime JoinedAt { get; set; }
}

public class S20ClubPostDto
{
    public int PostId { get; set; }
    public int HubId { get; set; }
    public string AuthorUserId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorClubRole { get; set; } = "Leader";
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class S20UserOptionDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = "User";
    public int TrustScore { get; set; }
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
    public int? PickupHubId { get; set; }
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

public class S20UpdateHubStationDto
{
    public int HubId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string OpenHours { get; set; } = "08:00 - 18:00";
    public string Phone { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ClubRules { get; set; }
    public bool? IsPublicClub { get; set; }
}

public class S20CreateClubDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string HubType { get; set; } = "ClubStation"; // ClubStation / DropoffHub / Library
    public string? LeaderUserId { get; set; }
    public string OpenHours { get; set; } = "09:00 - 20:00 (Cả tuần)";
    public string Phone { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ClubRules { get; set; }
    public bool IsPublicClub { get; set; } = true;
}

public class S20AssignClubLeaderDto
{
    public int HubId { get; set; }
    public string LeaderUserId { get; set; } = string.Empty;
}

public class S20AddClubMemberDto
{
    public int HubId { get; set; }
    public string? TargetUserId { get; set; }
    public string? TargetEmail { get; set; }
    public string MemberRole { get; set; } = "Member"; // Member / ViceLeader
}

public class S20JoinClubDto
{
    public int HubId { get; set; }
    public string? JoinNote { get; set; }
}

public class S20ReviewClubMemberDto
{
    public int HubId { get; set; }
    public int MemberId { get; set; }
    public bool Approve { get; set; } = true;
}

public class S20UpdateClubMemberRoleDto
{
    public int HubId { get; set; }
    public int MemberId { get; set; }
    public string NewRole { get; set; } = "Member"; // ViceLeader / Member
}

public class S20RemoveClubMemberDto
{
    public int HubId { get; set; }
    public int MemberId { get; set; }
}

public class S20CreateClubPostDto
{
    public int HubId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
}

public class S20DeleteClubPostDto
{
    public int HubId { get; set; }
    public int PostId { get; set; }
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
// S21: LIBRARIAN DESK (UNIFIED LENDING HUB, BARCODE SCAN, BATCH IMPORT & OVERDUE)
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
    public int PendingLoansCount { get; set; }
    public int ActiveLoansCount { get; set; }

    // Unified Loan Queue Filters & Pagination
    public string? LoanKeyword { get; set; }
    public string LoanChannelFilter { get; set; } = "all"; // all / Library / P2P
    public string LoanStatusFilter { get; set; } = "all";  // all / Pending / Approved / Active / Overdue / Returned / Rejected
    public int LoanPage { get; set; } = 1;
    public int LoanPageSize { get; set; } = 10;
    public int TotalFilteredLoans { get; set; }
    public int LoanTotalPages => LoanPageSize > 0 ? Math.Max(1, (int)Math.Ceiling(TotalFilteredLoans / (double)LoanPageSize)) : 1;

    // System Copies Filters & Pagination
    public string? CopyKeyword { get; set; }
    public string CopyStatusFilter { get; set; } = "all"; // all / Available / Borrowed / Reserved / Damaged / Lost
    public int? CopyLibraryFilter { get; set; }
    public int CopyPage { get; set; } = 1;
    public int CopyPageSize { get; set; } = 20;
    public int TotalFilteredCopies { get; set; }
    public int CopyTotalPages => CopyPageSize > 0 ? Math.Max(1, (int)Math.Ceiling(TotalFilteredCopies / (double)CopyPageSize)) : 1;

    public List<S21BookOptionDto> Books { get; set; } = new();
    public List<S21LibraryOptionDto> Libraries { get; set; } = new();
    public List<S21BorrowerOptionDto> Borrowers { get; set; } = new();
    public List<S21AvailableCopyOptionDto> AvailableLendableCopies { get; set; } = new();
    public List<S21UnifiedLoanItemDto> UnifiedLoans { get; set; } = new();
    public List<S21SystemCopyDeskItemDto> SystemCopies { get; set; } = new();
    public List<S21SystemCopyDeskItemDto> SampleQuickBarcodes { get; set; } = new();
    public List<S21OverdueReaderDto> OverdueLoans { get; set; } = new();
}

public class S21AvailableCopyOptionDto
{
    public int BookCopyId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string OwnerType { get; set; } = "System";
    public string LocationOrOwner { get; set; } = string.Empty;
}

public class S21UnifiedLoanItemDto
{
    public int LoanRequestId { get; set; }
    public int? LoanId { get; set; }
    public int BookCopyId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string LoanType { get; set; } = "Library"; // Library / P2P
    public string SourceDisplay { get; set; } = string.Empty;
    public string BorrowerId { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public string BorrowerEmail { get; set; } = string.Empty;
    public int BorrowerTrustScore { get; set; }
    public string RequestStatus { get; set; } = "Pending";
    public string CopyStatus { get; set; } = "Available";
    public string? LoanStatus { get; set; }
    public int DesiredDays { get; set; } = 14;
    public DateTime RequestedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public decimal FineAmount { get; set; }
    public string? RequestNote { get; set; }
}

public class S21ProcessLoanActionDto
{
    public int LoanRequestId { get; set; }
    public string Action { get; set; } = "Approve"; // Approve / Reject / Handover / Return
    public string ReturnCondition { get; set; } = "Good"; // Good / Fair / Damaged
    public string? Note { get; set; }
}

public class S21DirectLendBookDto
{
    public int BookCopyId { get; set; }
    public string BorrowerUserId { get; set; } = string.Empty;
    public int DesiredDays { get; set; } = 14;
    public bool ImmediateHandover { get; set; } = true;
    public string? Note { get; set; }
}

public class S21BorrowerOptionDto
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
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
    public int DesiredDays { get; set; } = 14;
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

public class S21DeleteSystemCopyDto
{
    public int BookCopyId { get; set; }
}

public class S21UpdateCopyStateDto
{
    public int BookCopyId { get; set; }
    public string NewStatus { get; set; } = "Damaged"; // Available / Damaged / Lost
    public string NewCondition { get; set; } = "Worn"; // New / Good / Fair / Worn
    public string? Note { get; set; }
}

// =========================================================================
// S22: ADMIN ANALYTICS, USER/ROLE GOVERNANCE, FILTERS & HANGFIRE DAILY JOBS
// =========================================================================
public class S22AdminAnalyticsFilterDto
{
    public string TimeRange { get; set; } = "all";       // all / 7d / 30d / 90d / 365d
    public string ChannelFilter { get; set; } = "all";   // all / Library / P2P
    public int CategoryId { get; set; } = 0;             // 0 = all
    public string? UserKeyword { get; set; }
    public string UserRoleFilter { get; set; } = "all";  // all / User / Curator / Librarian / Admin
    public string UserLockFilter { get; set; } = "all";  // all / Active / Locked
    public string UserTierFilter { get; set; } = "all";  // all / Free / Standard / Premium / Scholar
    public int UserPage { get; set; } = 1;
    public int UserPageSize { get; set; } = 20;
    public string AuditCategoryFilter { get; set; } = "all"; // all / TRANSACTION / TRANH CHẤP / EMAIL / THÔNG BÁO
    public string UpgradeStatusFilter { get; set; } = "Pending"; // Pending / Approved / Rejected / all
}

public class S22CategoryOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class S22AdminAnalyticsPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = "User";

    // Active Filter State
    public string TimeRange { get; set; } = "all";
    public string ChannelFilter { get; set; } = "all";
    public int CategoryId { get; set; } = 0;
    public string? UserKeyword { get; set; }
    public string UserRoleFilter { get; set; } = "all";
    public string UserLockFilter { get; set; } = "all";
    public string UserTierFilter { get; set; } = "all";
    public int UserPage { get; set; } = 1;
    public int UserPageSize { get; set; } = 20;
    public int TotalFilteredUsers { get; set; }
    public int UserTotalPages => UserPageSize > 0 ? Math.Max(1, (int)Math.Ceiling(TotalFilteredUsers / (double)UserPageSize)) : 1;
    public string AuditCategoryFilter { get; set; } = "all";
    public string UpgradeStatusFilter { get; set; } = "Pending";

    // Baseline KPI Cards (always reflect full system totals for overview)
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

    // Charts Data (filtered according to TimeRange / ChannelFilter / CategoryId)
    public int AvailableCopiesCount { get; set; }
    public int BorrowedCopiesCount { get; set; }
    public int ReservedCopiesCount { get; set; }
    public int DamagedOrLostCopiesCount { get; set; }

    // Governance & Logs
    public List<S22CategoryOptionDto> Categories { get; set; } = new();
    public List<S22AdminUserItemDto> Users { get; set; } = new();
    public List<CuratorUpgradeRequestItemDto> PendingRoleUpgrades { get; set; } = new();
    public List<S22SystemAuditLogDto> AuditLogs { get; set; } = new();
    public List<S22BackgroundJobInfoDto> BackgroundJobs { get; set; } = new();
}

public class S22AdminUserItemDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
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

public class S22ManageSampleDataDto
{
    public string ActionMode { get; set; } = "SeedSampleData";
}

public class S22TriggerJobRequestDto
{
    public string JobKey { get; set; } = "AllDailyJobs"; // DueReminder / OverdueFine / ExpireCleanup / AllDailyJobs
}
