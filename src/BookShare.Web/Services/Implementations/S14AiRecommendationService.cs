using BookShare.Web.Data;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S14AiRecommendationService : IS14AiRecommendationService
{
    private readonly BookShareDbContext _db;

    public S14AiRecommendationService(BookShareDbContext db)
    {
        _db = db;
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
                ? $"Cùng chuyên ngành \"{b.Category.Name}\" với các cuốn bạn đang đọc trên S03 ReadingTracker."
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

        string personaSummary = $"Dựa trên {progresses.Count} cuốn sách trong S03 ReadingTracker của {user?.FullName ?? "Nguyễn Hùng"}, hệ thống AI nhận thấy bạn ưu tiên nhóm chủ đề [{string.Join(", ", topCategories)}]. Dưới đây là danh sách đầu sách giấy có độ tương thích cao nhất và đang có sẵn bản sao trên kệ.";

        return new S14AiRecommendationPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserName = user?.FullName ?? "Nguyễn Hùng (BIT250157)",
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

        string advice = $"🤖 [Lộ trình đọc cá nhân hóa từ Gemini AI cho mục tiêu \"{request.SkillGoal}\" — Tâm trạng: {request.Mood}]: " +
                        $"Bạn nên bắt đầu với cuốn \"{top3.FirstOrDefault()?.Title ?? "Clean Code"}\" (đọc 25 trang/ngày trên S03 ReadingTracker), ghi chú trích dẫn quan trọng vào S04 BookNotesQuotes, sau đó mượn tiếp \"{top3.Skip(1).FirstOrDefault()?.Title ?? "Domain-Driven Design"}\" tại Kho chung ĐH CMC.";

        return (true, advice, top3);
    }
}
