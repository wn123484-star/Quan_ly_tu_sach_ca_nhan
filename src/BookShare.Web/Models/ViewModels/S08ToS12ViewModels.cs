namespace BookShare.Web.Models.ViewModels;

/* =========================================================================
   S08: LIBRARY LOAN FLOW VIEWMODELS (MƯỢN KHO CHUNG THƯ VIỆN)
   ========================================================================= */
public class S08LibraryLoanPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public string CurrentUserRole { get; set; } = "User";
    public string MembershipTier { get; set; } = "Free";
    public int MaxConcurrentLoans { get; set; } = 2;
    public int LendQuotaBonus { get; set; }
    public int EffectiveMaxLoans => MaxConcurrentLoans + LendQuotaBonus;
    public int CurrentActiveLoansCount { get; set; }
    public List<S08AvailableLibraryCopyDto> AvailableLibraryCopies { get; set; } = new();
    public List<S08LibraryLoanItemDto> LoanItems { get; set; } = new();
}

public class S08AvailableLibraryCopyDto
{
    public int BookCopyId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string LibraryName { get; set; } = string.Empty;
    public string ShelfLocation { get; set; } = string.Empty;
    public int? LibraryId { get; set; }
}

public class S08LibraryLoanItemDto
{
    public int LoanRequestId { get; set; }
    public int? LoanId { get; set; }
    public int BookCopyId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string BookAuthor { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public string ShelfLocation { get; set; } = string.Empty;
    public string BorrowerId { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public string BorrowerTier { get; set; } = "Free";
    public int BorrowerActiveLoans { get; set; }
    public int BorrowerMaxQuota { get; set; } = 2;
    public int DesiredDays { get; set; }
    public DateTime RequestedAt { get; set; }
    public string RequestStatus { get; set; } = "Pending"; // Pending / Approved / Rejected / Cancelled
    public string CopyStatus { get; set; } = "Available";
    public string CopyCondition { get; set; } = "Good";
    public string? LoanStatus { get; set; } // Active / Returned / Overdue
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string? RequestNote { get; set; }
}

public class CreateLibraryLoanRequestDto
{
    public int BookCopyId { get; set; }
    public int DesiredDays { get; set; } = 14;
    public string? RequestNote { get; set; }
}

public class ApproveRejectLibraryLoanDto
{
    public int LoanRequestId { get; set; }
    public bool Approve { get; set; } = true;
}

public class HandoverLibraryLoanDto
{
    public int LoanRequestId { get; set; }
}

public class ReturnLibraryLoanDto
{
    public int LoanRequestId { get; set; }
    public string ReturnCondition { get; set; } = "Good"; // New / Good / Fair / Worn / Damaged
}

/* =========================================================================
   S09: P2P LOAN FLOW VIEWMODELS (MƯỢN P2P GIỮA CÁC CÁ NHÂN)
   ========================================================================= */
public class S09P2PLoanPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public int CurrentUserTrustScore { get; set; } = 100;
    public int CurrentUserLendQuotaBonus { get; set; }
    public List<LibraryOptionDto> PickupHubs { get; set; } = new();
    public List<S09AvailableP2PCopyDto> AvailableP2PCopies { get; set; } = new();
    public List<S09P2PLoanItemDto> P2PItems { get; set; } = new();
}

public class S09AvailableP2PCopyDto
{
    public int BookCopyId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public int OwnerTrustScore { get; set; }
}

public class S09P2PLoanItemDto
{
    public int LoanRequestId { get; set; }
    public int? LoanId { get; set; }
    public int BookCopyId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string BookAuthor { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public int OwnerTrustScore { get; set; }
    public int OwnerQuotaBonus { get; set; }
    public string BorrowerId { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public int BorrowerTrustScore { get; set; }
    public int? PickupHubId { get; set; }
    public string PickupHubName { get; set; } = string.Empty;
    public string PickupHubAddress { get; set; } = string.Empty;
    public int DesiredDays { get; set; }
    public DateTime RequestedAt { get; set; }
    public string RequestStatus { get; set; } = "Pending";
    public string CopyStatus { get; set; } = "Available"; // Available / Reserved / InTransit / Borrowed
    public string? LoanStatus { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string? RequestNote { get; set; }
}

public class CreateP2PLoanRequestDto
{
    public int BookCopyId { get; set; }
    public int PickupHubId { get; set; } = 3;
    public int DesiredDays { get; set; } = 14;
    public string? RequestNote { get; set; }
}

public class ApproveP2PLoanDto
{
    public int LoanRequestId { get; set; }
    public bool Approve { get; set; } = true;
    public int? PickupHubId { get; set; }
}

public class P2PStageTransitionDto
{
    public int LoanRequestId { get; set; }
    public string Stage { get; set; } = "InTransit"; // InTransit / HandoverBorrowed / Returned
}

/* =========================================================================
   S10: CONDITION REPORT VIEWMODELS (BIÊN BẢN ĐỐI CHIẾU ẢNH GIAO & TRẢ)
   ========================================================================= */
public class S10ConditionReportPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public int? SelectedLoanId { get; set; }
    public int TotalReportsCount { get; set; }
    public int DiscrepancyCount { get; set; }
    public List<S10LoanComparisonDto> LoanComparisons { get; set; } = new();
}

public class S10LoanComparisonDto
{
    public int LoanId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public string LoanType { get; set; } = "Library";
    public string LoanStatus { get; set; } = "Active";
    public S10ReportDetailDto? HandoverReport { get; set; }
    public S10ReportDetailDto? ReturnReport { get; set; }
    public bool HasDamageDetected { get; set; }
}

public class S10ReportDetailDto
{
    public int ReportId { get; set; }
    public string ReportType { get; set; } = "Handover";
    public string ConditionGrade { get; set; } = "Good";
    public string? Note { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string ReportedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/* =========================================================================
   S11: HOT BOOK QUEUE VIEWMODELS (HÀNG CHỜ ĐẶT TRƯỚC SÁCH HOT)
   ========================================================================= */
public class S11HotBookQueuePageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public string CurrentUserTier { get; set; } = "Free";
    public List<S11HotBookDto> HotBooks { get; set; } = new();
    public List<S11QueueItemDto> ActiveReservations { get; set; } = new();
}

public class S11HotBookDto
{
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public int BorrowedOrReservedCopies { get; set; }
    public int WaitingCount { get; set; }
}

public class S11QueueItemDto
{
    public int ReservationId { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string BookAuthor { get; set; } = string.Empty;
    public string? CoverImageUrl { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string UserTier { get; set; } = "Free";
    public int QueuePosition { get; set; }
    public string Status { get; set; } = "Waiting"; // Waiting / Notified / Fulfilled / Cancelled
    public DateTime CreatedAt { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsCurrentUser { get; set; }
}

public class EnqueueReservationRequestDto
{
    public int BookId { get; set; }
}

public class NotifyTopQueueRequestDto
{
    public int BookId { get; set; }
}

public class CancelReservationRequestDto
{
    public int ReservationId { get; set; }
}

/* =========================================================================
   S12: LOAN RENEWAL & FINE VIEWMODELS (GIA HẠN MƯỢN & TÍNH PHÍ TRỄ HẠN)
   ========================================================================= */
public class S12LoanRenewalFinePageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public int ActiveLoansCount { get; set; }
    public int OverdueLoansCount { get; set; }
    public decimal TotalOutstandingFines { get; set; }
    public List<S12LoanFineItemDto> Loans { get; set; } = new();
    public List<S12FineTransactionDto> FineTransactions { get; set; } = new();
}

public class S12LoanFineItemDto
{
    public int LoanId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string BorrowerId { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public string LoanType { get; set; } = "Library";
    public DateTime StartDate { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public int RenewCount { get; set; }
    public int MaxRenewals { get; set; } = 2;
    public bool HasWaitingReservations { get; set; }
    public bool CanRenew => Status == "Active" && RenewCount < MaxRenewals && !HasWaitingReservations && OverdueDays == 0;
    public int OverdueDays { get; set; }
    public decimal CalculatedFine { get; set; }
    public decimal RecordedFineAmount { get; set; }
    public string Status { get; set; } = "Active";
}

public class S12FineTransactionDto
{
    public int TransactionId { get; set; }
    public int? LoanId { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentGateway { get; set; } = "VNPaySandbox";
    public string PaymentStatus { get; set; } = "Pending";
    public string? HmacSignature { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RenewLoanRequestDto
{
    public int LoanId { get; set; }
}

public class AssessOverdueFineRequestDto
{
    public int LoanId { get; set; }
    public int SimulatedOverdueDays { get; set; } = 5;
    public bool MarkPaidImmediately { get; set; } = true;
}
