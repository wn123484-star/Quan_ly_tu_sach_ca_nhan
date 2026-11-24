using System.Text.Json;

namespace BookShare.Web.Models.ViewModels;

// =========================================================================
// SPRINT 3.1: CURATOR ROLE UPGRADE WORKFLOW (S01 -> S22)
// =========================================================================
public class SubmitCuratorRequestDto
{
    public string OrganizationName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class CuratorUpgradeRequestItemDto
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string RequestedRole { get; set; } = "Curator";
    public string OrganizationName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ReviewRoleUpgradeRequestDto
{
    public int RequestId { get; set; }
    public bool Approve { get; set; } = true;
    public string? ReviewNote { get; set; }
}

// =========================================================================
// SPRINT 3.2: DISPUTE RESOLUTION -> S17 PENDING COMPENSATION INVOICE
// =========================================================================
public class S19ResolveDisputeOutcomeDto
{
    public int? PendingTransactionId { get; set; }
    public string? RawPayload { get; set; }
    public string? ExpectedHmac { get; set; }
    public string? PaymentUrl { get; set; }
    public bool LoanUnlocked { get; set; }
}

// =========================================================================
// SPRINT 3.3: PHYSICAL BOOK HANDOVER & RETURN QR (/api/handover-qr)
// =========================================================================
public class CreateHandoverQrRequestDto
{
    public int LoanRequestId { get; set; }
    public string HandoverType { get; set; } = "Handover"; // Handover / Return
}

public class CreateHandoverQrResultDto
{
    public int QrId { get; set; }
    public int LoanRequestId { get; set; }
    public string HandoverType { get; set; } = "Handover";
    public string Token { get; set; } = string.Empty;
    public string QrPayload { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; } = 600;
    public DateTime ExpiresAt { get; set; }
}

public class ConfirmHandoverQrRequestDto
{
    public string? QrPayload { get; set; }
    public string? Token { get; set; }
    public string? ShortCode { get; set; }
    public int? LoanRequestId { get; set; }
    public string ReturnCondition { get; set; } = "Good";
}

// =========================================================================
// SPRINT 3.4: 2-WAY QR LOGIN (/api/qr-login)
// =========================================================================
public class CreateQrLoginResultDto
{
    public int SessionId { get; set; }
    public string FlowType { get; set; } = "WebLoginByMobile";
    public string Token { get; set; } = string.Empty;
    public string QrPayload { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class QrLoginStatusDto
{
    public int SessionId { get; set; }
    public string Status { get; set; } = "Expired";
    public int RemainingSeconds { get; set; }
}

public class PreviewQrLoginRequestDto
{
    public string? QrPayload { get; set; }
    public string? Token { get; set; }
}

public class PreviewQrLoginResultDto
{
    public int SessionId { get; set; }
    public string DeviceInfo { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int RemainingSeconds { get; set; }
}

public class ApproveQrLoginRequestDto
{
    public int SessionId { get; set; }
    public bool Approve { get; set; } = true;
}

public class ClaimQrLoginRequestDto
{
    public int SessionId { get; set; }
    public string Token { get; set; } = string.Empty;
}

public class MobileClaimQrLoginRequestDto
{
    public string? QrPayload { get; set; }
    public string? Token { get; set; }
}

// =========================================================================
// SPRINT 3.5: AI COPILOT 2-TIER FUNCTION CALLING (S13 / QUICK-ASK)
// =========================================================================
public class AiClientCommandDto
{
    public string Command { get; set; } = string.Empty; // set_theme / navigate_to / search_catalog
    public string Label { get; set; } = string.Empty;
    public Dictionary<string, string> Args { get; set; } = new();
}

public class AiActionCardDto
{
    public string ActionKey { get; set; } = string.Empty; // renew_loan / update_reading_progress / send_my_reminder_email / send_due_reminders_batch
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ConfirmLabel { get; set; } = "Xác nhận thực hiện";
    public string? RequiresRole { get; set; }
    public Dictionary<string, JsonElement> Parameters { get; set; } = new();
}

public class ExecuteCopilotActionRequestDto
{
    public string ActionKey { get; set; } = string.Empty;
    public Dictionary<string, JsonElement>? Parameters { get; set; }
}

public class CopilotChatResultDto
{
    public bool Success { get; set; }
    public string ReplyText { get; set; } = string.Empty;
    public int SessionId { get; set; }
    public int PromptTokens { get; set; }
    public string SourceMode { get; set; } = string.Empty;
    public List<AiClientCommandDto> ClientCommands { get; set; } = new();
    public List<AiActionCardDto> ActionCards { get; set; } = new();
}

// =========================================================================
// SPRINT 3.6: MOBILE COMPANION API (/api/mobile)
// =========================================================================
public class MobileNotificationItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "System";
    public string TargetUrl { get; set; } = "/S18NotificationHub";
    public DateTime CreatedAt { get; set; }
}

public class MobileUnreadNotificationsResponseDto
{
    public int UnreadCount { get; set; }
    public List<MobileNotificationItemDto> Notifications { get; set; } = new();
}

public class MobileSessionStateDto
{
    public bool Authenticated { get; set; }
    public string? UserId { get; set; }
    public string? FullName { get; set; }
    public string? Role { get; set; }
    public int UnreadCount { get; set; }
    public string ServerVersion { get; set; } = "BookShare-Sprint3";
}
