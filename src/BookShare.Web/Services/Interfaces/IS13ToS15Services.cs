using BookShare.Web.Models.ViewModels;

namespace BookShare.Web.Services.Interfaces;

public interface IS13GeminiAiService
{
    Task<S13GeminiAssistantPageViewModel> GetAssistantPageAsync(string currentUserId, int? sessionId = null);

    Task<(bool Success, string ReplyText, int SessionId, int PromptTokens, string SourceMode)> SendMessageWithRagAsync(
        string currentUserId,
        int? sessionId,
        string userMessage);

    // [BS-AGENT-AI-COPILOT] Trợ lý AI Copilot Hành Động trả về danh sách actions[] thực thi trực tiếp
    Task<(bool Success, string ReplyText, int SessionId, int PromptTokens, string SourceMode, List<AiCopilotActionDto> Actions, string? EmailSubject)> SendCopilotMessageAsync(
        string currentUserId,
        int? sessionId,
        string userMessage);

        Task<(bool Success, string Message, int SessionId)> CreateSessionAsync(
            string currentUserId,
            string title);

        Task<(bool Success, string Message)> DeleteSessionAsync(
            string currentUserId,
            int sessionId);
    }

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
