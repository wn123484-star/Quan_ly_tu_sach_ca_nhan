using BookShare.Web.Models.ViewModels;
using Microsoft.AspNetCore.Http;

namespace BookShare.Web.Services.Interfaces;

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
}

public interface IS09P2PLoanService
{
    Task<S09P2PLoanPageViewModel> GetP2PLoanFlowAsync(string currentUserId);

    Task<(bool Success, string Message)> CreateP2PLoanRequestAsync(
        string currentUserId,
        CreateP2PLoanRequestDto request);

    Task<(bool Success, string Message)> ApproveOrRejectP2PLoanAsync(
        string currentUserId,
        ApproveP2PLoanDto request);

    Task<(bool Success, string Message)> AdvanceP2PStageAsync(
        string currentUserId,
        P2PStageTransitionDto request);
}

public interface IS10ConditionReportService
{
    Task<S10ConditionReportPageViewModel> GetConditionReportsPageAsync(string currentUserId, int? selectedLoanId = null);

    Task<(bool Success, string Message, string ImageUrl)> SaveConditionReportAsync(
        string currentUserId,
        int loanId,
        string reportType,
        string conditionGrade,
        string? note,
        IFormFile? uploadedFile,
        string? fallbackImageUrl);
}

public interface IS11HotBookQueueService
{
    Task<S11HotBookQueuePageViewModel> GetHotBookQueuePageAsync(string currentUserId);

    Task<(bool Success, string Message, int QueuePosition)> EnqueueReservationAsync(
        string currentUserId,
        int bookId);

    Task<(bool Success, string Message)> NotifyFirstInQueueAsync(int bookId);

    Task<(bool Success, string Message)> CancelReservationAsync(
        string currentUserId,
        int reservationId);
}

public interface IS12LoanRenewalFineService
{
    Task<S12LoanRenewalFinePageViewModel> GetLoanRenewalFinePageAsync(string currentUserId);

    Task<(bool Success, string Message, DateTime? NewDueDate, int NewRenewCount)> RenewLoanAsync(
        string currentUserId,
        int loanId);

    Task<(bool Success, string Message, decimal FineAmount, int TransactionId)> AssessOverdueFineAsync(
        string currentUserId,
        AssessOverdueFineRequestDto request);
}
