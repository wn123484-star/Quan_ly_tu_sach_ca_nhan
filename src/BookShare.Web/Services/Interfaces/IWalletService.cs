using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

// [BS-WALLET-PAY] Dịch vụ Ví Bảo Lãnh BookShare Pay, Escrow Thuê Sách & Thanh Toán 2 Bước PIN + HMAC-SHA512
public interface IWalletService
{
    Task<UserWallet> EnsureWalletAsync(string userId);
    Task<WalletSummaryDto> GetWalletSummaryAsync(string userId);
    Task<(bool Success, string Message, decimal NewBalance, int TransactionId, string HmacSignature)> TopUpDemoAsync(
        string userId,
        WalletTopUpRequestDto request);
    Task<(bool Success, string Message, decimal NewBalance, int TransactionId, string HmacSignature)> PayQrWithPinAsync(
        string userId,
        WalletPayQrRequestDto request);
    Task<(bool Success, string Message)> ChangePinAsync(
        string userId,
        WalletChangePinRequestDto request);
    Task<(bool Success, string Message)> HoldEscrowForLoanAsync(
        string borrowerId,
        Loan loan,
        decimal rentalFeeTotal,
        decimal depositAmount);
    Task<(bool Success, string Message)> SettleEscrowOnReturnAsync(
        Loan loan,
        string? lenderUserId,
        bool isDamaged);
    Task<(bool Success, string Message)> ChargeRenewalFeeAsync(
        string borrowerId,
        Loan loan,
        decimal renewalFee);
}
