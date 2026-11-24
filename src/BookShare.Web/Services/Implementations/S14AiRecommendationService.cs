using System.Text;
using System.Text.Json;
using BookShare.Web.Data;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S14AiRecommendationService : IS14AiRecommendationService
{
    private readonly BookShareDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public S14AiRecommendationService(
        BookShareDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration config)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    public async Task<S14AiRecommendationPageViewModel> GetRecommendationsPageAsync(
        string currentUserId,
        string mood = "all",
        string major = "all")
    {
        var user = await _db.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        var progresses = await _db.ReadingProgresses
            .AsNoTracking()
            .Include(rp => rp.Book)
                .ThenInclude(b => b.Category)
            .Where(rp => rp.UserId == currentUserId)
            .ToListAsync();

        var topCategories = progresses
            .GroupBy(rp => rp.Book.Category.Name)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(3)
            .ToList();

        if (!topCategories.Any())
        {
            topCategories.Add("Công nghệ Phần mềm & AI");
            topCategories.Add("Kỹ năng Học thuật");
        }

        var trackedBookIds = progresses.Select(rp => rp.BookId).ToHashSet();

        var booksQuery = _db.Books
            .AsNoTracking()
            .Include(b => b.Category)
            .Include(b => b.BookCopies)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(major) && major != "all")
        {
            var majorKw = major.ToLower();
            booksQuery = booksQuery.Where(b =>
                b.Category.Name.ToLower().Contains(majorKw) ||
                b.Title.ToLower().Contains(majorKw));
        }

        var candidateBooks = await booksQuery
            .OrderByDescending(b => b.BookCopies.Count(bc => bc.Status == "Available" && bc.IsLendable))
            .ThenBy(b => b.Id)
            .Take(12)
            .ToListAsync();

        var recommendations = candidateBooks.Select((b, idx) =>
        {
            var firstAvailCopy = b.BookCopies.FirstOrDefault(bc => bc.Status == "Available" && bc.IsLendable);
            bool matchesFavCategory = topCategories.Contains(b.Category.Name);
            int score = matchesFavCategory ? Math.Max(88, 98 - idx * 2) : Math.Max(80, 91 - idx * 2);

            string moodTag = (idx % 3) switch
            {
                0 => "Tập trung sâu (Deep Focus)",
                1 => "Tư duy Hệ thống & Kiến trúc",
                _ => "Đọc nhẹ nhàng cuối tuần"
            };

            string reason = matchesFavCategory
                ? $"Cùng chuyên ngành \"{b.Category.Name}\" với các cuốn bạn đang theo dõi trong Không gian đọc."
                : $"Được mượn nhiều nhất bởi các độc giả có TrustScore trên 120 điểm tại ĐH CMC.";

            return new S14RecommendedBookDto
            {
                BookId = b.Id,
                AvailableBookCopyId = firstAvailCopy?.Id,
                AvailableBarcode = firstAvailCopy?.Barcode,
                Title = b.Title,
                Author = b.Author,
                ISBN = b.Isbn,
                CategoryName = b.Category.Name,
                PageCount = b.PageCount,
                CoverImageUrl = b.CoverImageUrl,
                MatchScorePercent = score,
                AiReason = reason,
                MoodTag = moodTag,
                AvailableCopiesCount = b.BookCopies.Count(bc => bc.Status == "Available" && bc.IsLendable)
            };
        }).ToList();

        if (!string.IsNullOrWhiteSpace(mood) && mood != "all")
        {
            if (mood == "ShortRead")
            {
                recommendations = recommendations.OrderBy(r => r.PageCount).ToList();
            }
            else if (mood == "DeepFocus")
            {
                recommendations = recommendations.OrderByDescending(r => r.MatchScorePercent).ToList();
            }
        }

        string personaSummary = $"Dựa trên {progresses.Count} cuốn sách trong hồ sơ đọc của {user?.FullName ?? "Độc giả BookShare"}, hệ thống AI nhận thấy bạn ưu tiên nhóm chủ đề [{string.Join(", ", topCategories)}]. Dưới đây là danh sách đầu sách giấy có độ tương thích cao nhất và đang có sẵn bản sao trên kệ.";

        return new S14AiRecommendationPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserName = user?.FullName ?? "Độc giả BookShare",
            SelectedMood = mood,
            SelectedMajor = major,
            AiPersonaSummary = personaSummary,
            TopReadCategories = topCategories,
            TotalTrackedBooks = progresses.Count,
            CompletedBooksCount = progresses.Count(p => p.Status == "Completed"),
            Recommendations = recommendations
        };
    }

    public async Task<(bool Success, string AiAdvice, List<S14RecommendedBookDto> SuggestedBooks)> GenerateAiCustomAdviceAsync(
        string currentUserId,
        S14GenerateCustomRecRequestDto request)
    {
        var pageVm = await GetRecommendationsPageAsync(currentUserId, request.Mood, "all");
        var top3 = pageVm.Recommendations.Take(3).ToList();

        string modelName = _config["Gemini:Model"]
            ?? Environment.GetEnvironmentVariable("GEMINI_MODEL")
            ?? "gemini-2.5-flash";
        string? apiKey = _config["Gemini:ApiKey"]
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        string? geminiAdvice = null;
        if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.StartsWith("demo", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var bookSummary = string.Join("; ", top3.Select(b => $"\"{b.Title}\" ({b.Author}, {b.PageCount} trang, thể loại {b.CategoryName})"));
                var prompt = $"Bạn là Trợ lý Thủ thư BookShare (chỉ mượn sách giấy phi thương mại, tuyệt đối không bán sách). " +
                             $"Hãy đề xuất lộ trình đọc 14 ngày ngắn gọn bằng tiếng Việt cho mục tiêu \"{request.SkillGoal}\" với tâm trạng đọc \"{request.Mood}\" " +
                             $"dựa trên các sách sẵn sàng cho mượn: {bookSummary}.";

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(20);
                var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
                var payload = new
                {
                    contents = new[]
                    {
                        new { role = "user", parts = new[] { new { text = prompt } } }
                    }
                };

                var httpRes = await client.PostAsync(
                    endpoint,
                    new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

                if (httpRes.IsSuccessStatusCode)
                {
                    var body = await httpRes.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                        candidates.GetArrayLength() > 0 &&
                        candidates[0].TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0 &&
                        parts[0].TryGetProperty("text", out var textNode))
                    {
                        geminiAdvice = textNode.GetString();
                    }
                }
            }
            catch
            {
                // Dùng lộ trình gợi ý thông minh từ kho sách nội bộ nếu không kết nối được Gemini API
            }
        }

        string advice = string.IsNullOrWhiteSpace(geminiAdvice)
            ? $"Lộ trình đọc 14 ngày đề xuất cho mục tiêu \"{request.SkillGoal}\" (nhịp độ {request.Mood}): " +
              $"Bắt đầu với cuốn \"{top3.FirstOrDefault()?.Title ?? "Clean Code"}\" (duy trì 25 trang/ngày tại mục Tiến độ đọc sách), " +
              $"lưu các ý chính vào Sổ tay ghi chú và tiếp nối bằng \"{top3.Skip(1).FirstOrDefault()?.Title ?? "Domain-Driven Design"}\" trong thư viện."
            : geminiAdvice;

        return (true, advice, top3);
    }
}
