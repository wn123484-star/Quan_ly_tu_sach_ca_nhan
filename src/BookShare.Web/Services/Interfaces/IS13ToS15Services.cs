using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

/// <summary>
/// [S13] Giao diện dịch vụ Trợ lý Đọc sách AI Gemini và điều phối lệnh giao diện/Email.
/// </summary>
/// <remarks>Triển khai bởi: S13GeminiAiService.</remarks>
public interface IS13GeminiAiService
{
    Task<S13GeminiAssistantPageViewModel> GetAssistantPageAsync(string currentUserId, int? sessionId = null);

    Task<(bool Success, string ReplyText, int SessionId, int PromptTokens, string SourceMode)> SendMessageWithRagAsync(
        string currentUserId,
        int? sessionId,
        string userMessage);

    Task<CopilotChatResultDto> SendCopilotMessageAsync(
        string currentUserId,
        string currentUserRole,
        int? sessionId,
        string userMessage);

    Task<(bool Success, string Message)> ExecuteCopilotActionAsync(
        string currentUserId,
        string currentUserRole,
        ExecuteCopilotActionRequestDto request);

        Task<(bool Success, string Message, int SessionId)> CreateSessionAsync(
            string currentUserId,
            string title);

        Task<(bool Success, string Message)> DeleteSessionAsync(
            string currentUserId,
            int sessionId);

        Task<AiQuickHistoryResultDto> GetQuickWidgetHistoryAsync(
            string currentUserId,
            string currentUserRole);

        Task<(bool Success, string Message, int SessionId, S13ChatMessageDto? Greeting)> ResetChatSessionAsync(
            string currentUserId,
            string currentUserRole,
            int? sessionId = null);

        Task<(bool Success, string Message, bool RequiresEmailSetup)> SendChatSummaryEmailAsync(
            string currentUserId,
            int? sessionId = null,
            string? customNote = null);
    }

/// <summary>
/// [S14] Giao diện dịch vụ Gợi ý sách cá nhân hóa và Lộ trình đọc theo mục tiêu.
/// </summary>
/// <remarks>Triển khai bởi: S14AiRecommendationService.</remarks>
public interface IS14AiRecommendationService
{
    Task<S14AiRecommendationPageViewModel> GetRecommendationsPageAsync(
        string currentUserId,
        string mood = "all",
        string major = "all");

    Task<(bool Success, string AiAdvice, List<S14RecommendedBookDto> SuggestedBooks)> GenerateAiCustomAdviceAsync(
        string currentUserId,
        S14GenerateCustomRecRequestDto request);
}

/// <summary>
/// [S15] Giao diện dịch vụ Điểm uy tín và Đánh giá cộng đồng hai chiều.
/// </summary>
/// <remarks>Triển khai bởi: S15TrustScoreReviewService.</remarks>
public interface IS15TrustScoreReviewService
{
    Task<S15TrustScoreReviewPageViewModel> GetTrustScoreReviewPageAsync(string currentUserId);

        Task<(bool Success, string Message, int NewTargetTrustScore)> SubmitTwoWayReviewAsync(
            string reviewerUserId,
            SubmitReviewRequestDto request);

        Task<(bool Success, string Message)> UpdateReviewAsync(
            string currentUserId,
            string currentUserRole,
            UpdateReviewRequestDto request);

        Task<(bool Success, string Message)> DeleteReviewAsync(
            string currentUserId,
            string currentUserRole,
            int reviewId);
    }
