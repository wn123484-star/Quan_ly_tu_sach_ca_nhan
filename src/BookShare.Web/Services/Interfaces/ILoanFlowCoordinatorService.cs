using BookShare.Web.Models.Entities;

namespace BookShare.Web.Services.Interfaces;

/// <summary>
/// [CORE-COORDINATOR] Giao diện bộ điều phối nghiệp vụ Mượn – Trả dùng chung giữa S08, S09, S11, S20 và S21.
/// </summary>
/// <remarks>Triển khai bởi: LoanFlowCoordinatorService.</remarks>
public interface ILoanFlowCoordinatorService
{
    Task<(bool IsEligible, string Reason)> ValidateBorrowEligibilityAsync(string borrowerUserId, BookCopy copy);

    Task<bool> FulfillNotifiedReservationAsync(string borrowerUserId, int bookId);

    Task<(bool CanApprove, string Reason)> ValidateCopyApprovalAsync(LoanRequest loanRequest);

    Task<(bool NotifiedNext, string? NotifiedUserName)> HandleCopyReturnedAndNotifyQueueAsync(
        BookCopy copy,
        DateTime nowUtc);
}
