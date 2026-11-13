namespace BookShare.Web.Models.ViewModels;

/* =========================================================================
   S13: GEMINI AI ASSISTANT & LIGHTWEIGHT RAG VIEWMODELS
   ========================================================================= */
public class S13GeminiAssistantPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public string ActiveModelName { get; set; } = "gemini-2.5-flash";
    public bool HasConfiguredApiKey { get; set; }
    public int RagAvailableCopiesCount { get; set; }
    public int UserActiveLoansCount { get; set; }
    public int UserReadingBooksCount { get; set; }
    public int SelectedSessionId { get; set; }
    public List<S13ChatSessionDto> Sessions { get; set; } = new();
    public List<S13ChatMessageDto> Messages { get; set; } = new();
    public List<S13RagContextBookDto> RagBooksPreview { get; set; } = new();
}

public class S13ChatSessionDto
{
    public int SessionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ModelName { get; set; } = "gemini-2.5-flash";
    public int MessageCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class S13ChatMessageDto
{
    public int MessageId { get; set; }
    public string Sender { get; set; } = "User"; // User / Gemini
    public string MessageText { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public string? ReferencedBookIdsJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class S13RagContextBookDto
{
    public int BookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int AvailableCopies { get; set; }
}

public class SendAiChatRequestDto
{
    public int? SessionId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CreateAiSessionRequestDto
{
    public string Title { get; set; } = string.Empty;
}

/* =========================================================================
   S14: AI PERSONALIZED BOOK RECOMMENDATION VIEWMODELS
   ========================================================================= */
public class S14AiRecommendationPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public string SelectedMood { get; set; } = "all";
    public string SelectedMajor { get; set; } = "all";
    public string AiPersonaSummary { get; set; } = string.Empty;
    public List<string> TopReadCategories { get; set; } = new();
    public int TotalTrackedBooks { get; set; }
    public int CompletedBooksCount { get; set; }
    public List<S14RecommendedBookDto> Recommendations { get; set; } = new();
}

public class S14RecommendedBookDto
{
    public int BookId { get; set; }
    public int? AvailableBookCopyId { get; set; }
    public string? AvailableBarcode { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public string? CoverImageUrl { get; set; }
    public int MatchScorePercent { get; set; } = 95;
    public string AiReason { get; set; } = string.Empty;
    public string MoodTag { get; set; } = string.Empty;
    public int AvailableCopiesCount { get; set; }
}

public class S14GenerateCustomRecRequestDto
{
    public string Mood { get; set; } = "DeepFocus";
    public string SkillGoal { get; set; } = "Kiến trúc Phần mềm & .NET 10";
}

/* =========================================================================
   S15: TWO-WAY TRUSTSCORE & BOOK REVIEW VIEWMODELS
   ========================================================================= */
public class S15TrustScoreReviewPageViewModel
{
    public string CurrentUserId { get; set; } = string.Empty;
    public string CurrentUserName { get; set; } = string.Empty;
    public int CurrentUserTrustScore { get; set; } = 128;
    public int CurrentUserLendQuotaBonus { get; set; }
    public List<CatalogBookOptionDto> BooksToReview { get; set; } = new();
    public List<S15UserOptionDto> UsersToReview { get; set; } = new();
    public List<S15LeaderboardUserDto> Leaderboard { get; set; } = new();
    public List<S15ReviewCardDto> Reviews { get; set; } = new();
}

public class S15UserOptionDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string RoleName { get; set; } = "User";
    public int TrustScore { get; set; }
}

public class S15LeaderboardUserDto
{
    public int Rank { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = "User";
    public int TrustScore { get; set; }
    public int LendQuotaBonus { get; set; }
    public int TotalReviewsReceived { get; set; }
}

public class S15ReviewCardDto
{
    public int ReviewId { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
    public string TargetType { get; set; } = "Book"; // Book / User
    public string TargetId { get; set; } = string.Empty;
    public string TargetDisplayName { get; set; } = string.Empty;
    public int? LoanId { get; set; }
    public int Rating { get; set; } = 5;
    public string? Comment { get; set; }
    public int TrustScoreDelta { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SubmitReviewRequestDto
{
    public string TargetType { get; set; } = "User"; // Book / User
    public string TargetId { get; set; } = string.Empty;
    public int? LoanId { get; set; }
    public int Rating { get; set; } = 5;
    public string Comment { get; set; } = string.Empty;
}
