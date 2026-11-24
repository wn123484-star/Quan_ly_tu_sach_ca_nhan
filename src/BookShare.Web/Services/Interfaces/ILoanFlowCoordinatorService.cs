using BookShare.Web.Models.Entities;

namespace BookShare.Web.Services.Interfaces;

public interface ILoanFlowCoordinatorService
{
    Task<(bool IsEligible, string Reason)> ValidateBorrowEligibilityAsync(string borrowerUserId, BookCopy copy);

    Task<bool> FulfillNotifiedReservationAsync(string borrowerUserId, int bookId);

    Task<(bool CanApprove, string Reason)> ValidateCopyApprovalAsync(LoanRequest loanRequest);

    Task<(bool NotifiedNext, string? NotifiedUserName)> HandleCopyReturnedAndNotifyQueueAsync(
        BookCopy copy,
        DateTime nowUtc);
}
