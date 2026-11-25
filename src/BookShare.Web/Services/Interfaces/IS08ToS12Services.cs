using BookShare.Web.Models.ViewModels;
using Microsoft.AspNetCore.Http;

namespace BookShare.Web.Services.Interfaces;

/// <summary>
/// [S08] Giao diện dịch vụ Luồng Mượn – Trả sách Kho chung Thư viện.
/// </summary>
/// <remarks>Triển khai bởi: S08LibraryLoanService.</remarks>
public interface IS08LibraryLoanService
{
    Task<S08LibraryLoanPageViewModel> GetLibraryLoanFlowAsync(string currentUserId, string currentUserRole);

    Task<(bool Success, string Message)> CreateLibraryLoanRequestAsync(
        string currentUserId,
        CreateLibraryLoanRequestDto request);

    Task<(bool Success, string Message)> ApproveOrRejectLibraryLoanAsync(
        string currentUserId,
        ApproveRejectLibraryLoanDto request);

    Task<(bool Success, string Message)> ConfirmHandoverLibraryLoanAsync(
        string currentUserId,
        HandoverLibraryLoanDto request);

        Task<(bool Success, string Message)> ConfirmReturnLibraryLoanAsync(
            string currentUserId,
            ReturnLibraryLoanDto request);

        Task<(bool Success, string Message)> CancelLibraryLoanRequestAsync(
            string currentUserId,
            string currentUserRole,
            int loanRequestId);
    }

/// <summary>
/// [S09] Giao diện dịch vụ Luồng Mượn – Trả sách Cộng đồng ngang hàng P2P.
/// </summary>
/// <remarks>Triển khai bởi: S09P2PLoanService.</remarks>
public interface IS09P2PLoanService
{
    Task<S09P2PLoanPageViewModel> GetP2PLoanFlowAsync(string currentUserId);

    Task<(bool Success, string Message)> CreateP2PLoanRequestAsync(
        string currentUserId,
        CreateP2PLoanRequestDto request);

    Task<(bool Success, string Message)> ApproveOrRejectP2PLoanAsync(
        string currentUserId,
        string currentUserRole,
        ApproveP2PLoanDto request);

    Task<(bool Success, string Message)> AdvanceP2PStageAsync(
        string currentUserId,
        string currentUserRole,
        P2PStageTransitionDto request);

    Task<(bool Success, string Message)> CancelP2PLoanRequestAsync(
        string currentUserId,
        string currentUserRole,
        int loanRequestId);
}

/// <summary>
/// [S10] Giao diện dịch vụ Hộ chiếu Kiểm định Tình trạng Sách (Ảnh giao & Ảnh trả).
/// </summary>
/// <remarks>Triển khai bởi: S10ConditionReportService.</remarks>
public interface IS10ConditionReportService
{
    Task<S10ConditionReportPageViewModel> GetConditionReportsPageAsync(string currentUserId, int? selectedLoanId = null, string currentUserRole = "User");

    Task<(bool Success, string Message, string ImageUrl)> SaveConditionReportAsync(
        string currentUserId,
        string currentUserRole,
        int loanId,
        string reportType,
        string conditionGrade,
        string? note,
        IFormFile? uploadedFile,
        string? fallbackImageUrl);

    Task<(bool Success, string Message)> UpdateConditionReportAsync(
        string currentUserId,
        string currentUserRole,
        UpdateConditionReportRequestDto request);

    Task<(bool Success, string Message)> DeleteConditionReportAsync(
        string currentUserId,
        string currentUserRole,
        int reportId);
}

/// <summary>
/// [S11] Giao diện dịch vụ Hàng chờ đặt trước Sách Hot.
/// </summary>
/// <remarks>Triển khai bởi: S11HotBookQueueService.</remarks>
public interface IS11HotBookQueueService
{
    Task<S11HotBookQueuePageViewModel> GetHotBookQueuePageAsync(string currentUserId);

    Task<(bool Success, string Message, int QueuePosition)> EnqueueReservationAsync(
        string currentUserId,
        int bookId);

    Task<(bool Success, string Message)> NotifyFirstInQueueAsync(int bookId);

    Task<(bool Success, string Message)> CancelReservationAsync(
        string currentUserId,
        string currentUserRole,
        int reservationId);
}

/// <summary>
/// [S12] Giao diện dịch vụ Gia hạn hợp đồng mượn và Tính phí trễ hạn.
/// </summary>
/// <remarks>Triển khai bởi: S12LoanRenewalFineService.</remarks>
public interface IS12LoanRenewalFineService
{
    Task<S12LoanRenewalFinePageViewModel> GetLoanRenewalFinePageAsync(string currentUserId, string currentUserRole = "User");

    Task<(bool Success, string Message, DateTime? NewDueDate, int NewRenewCount)> RenewLoanAsync(
        string currentUserId,
        string currentUserRole,
        int loanId);

    Task<(bool Success, string Message, decimal FineAmount, int TransactionId)> AssessOverdueFineAsync(
        string currentUserId,
        AssessOverdueFineRequestDto request);
}
