namespace BookShare.Web.Models.ViewModels;

/* =========================================================================
   [BS-WALLET-PAY] VIEWMODELS & DTOS CHO VÍ BẢO LÃNH BOOKSHARE PAY
   ========================================================================= */
public class WalletSummaryDto
{
    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string UserRole { get; set; } = "User";
    public decimal Balance { get; set; }
    public decimal EscrowHeldTotal { get; set; }
    public decimal LenderRevenueTotal { get; set; }
    public string MaskedCardNumber { get; set; } = "4826 •••• •••• 2026";
    public bool HasCustomPin { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<WalletTransactionDto> RecentTransactions { get; set; } = new();
}

public class WalletTransactionDto
{
    public int TransactionId { get; set; }
    public int? LoanId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentGateway { get; set; } = "BookSharePay";
    public string PaymentStatus { get; set; } = "Completed";
    public string? HmacSignature { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WalletTopUpRequestDto
{
    public decimal Amount { get; set; } = 200000m;
    public string? Note { get; set; }
}

public class WalletPayQrRequestDto
{
    public decimal Amount { get; set; }
    public string Pin { get; set; } = string.Empty;
    public string PaymentRef { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? LoanId { get; set; }
}

public class WalletChangePinRequestDto
{
    public string CurrentPin { get; set; } = string.Empty;
    public string NewPin { get; set; } = string.Empty;
}

/* =========================================================================
   [BS-QRLOGIN-BRIDGE] DTOS CHO QR CROSS-LOGIN (MOBILE -> WEB PC)
   ========================================================================= */
public class QrLoginInitResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string QrPayload { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int ExpiresInSeconds { get; set; } = 180;
}

public class QrLoginStatusResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? ApprovedUserName { get; set; }
    public int ExpiresInSeconds { get; set; }
}

public class QrLoginTokenRequestDto
{
    public string Token { get; set; } = string.Empty;
}
