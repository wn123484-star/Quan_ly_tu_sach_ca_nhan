using BookShare.Web.Data;
using BookShare.Web.Models.Entities;
using BookShare.Web.Models.ViewModels;
using BookShare.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BookShare.Web.Services.Implementations;

public class S15TrustScoreReviewService : IS15TrustScoreReviewService
{
    private readonly BookShareDbContext _db;

    public S15TrustScoreReviewService(BookShareDbContext db)
    {
        _db = db;
    }

    public async Task<S15TrustScoreReviewPageViewModel> GetTrustScoreReviewPageAsync(string currentUserId)
    {
        var currentUser = await _db.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        var books = await _db.Books
            .AsNoTracking()
            .OrderBy(b => b.Title)
            .Select(b => new CatalogBookOptionDto
            {
                Id = b.Id,
                Title = b.Title,
                Author = b.Author,
                ISBN = b.Isbn
            })
            .ToListAsync();

        var allUsers = await _db.AspNetUsers
            .AsNoTracking()
            .Include(u => u.Roles)
            .OrderByDescending(u => u.TrustScore)
            .ToListAsync();

        var usersToReview = allUsers
            .Select(u => new S15UserOptionDto
            {
                UserId = u.Id,
                FullName = u.FullName,
                RoleName = u.Roles.FirstOrDefault()?.Name ?? "User",
                TrustScore = u.TrustScore
            })
            .ToList();

        var reviews = await _db.Reviews
            .AsNoTracking()
            .Include(r => r.Reviewer)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var bookLookup = books.ToDictionary(b => b.Id.ToString(), b => $"{b.Title} ({b.Author})");
        var userLookup = allUsers.ToDictionary(u => u.Id, u => u.FullName);

        var reviewCards = reviews.Select(r =>
        {
            string targetName = r.TargetType == "Book"
                ? (bookLookup.TryGetValue(r.TargetId, out var bTitle) ? bTitle : $"Đầu sách #{r.TargetId}")
                : (userLookup.TryGetValue(r.TargetId, out var uName) ? uName : $"Độc giả {r.TargetId}");

            int delta = r.TargetType == "User"
                ? r.Rating switch
                {
                    5 => 5,
                    4 => 3,
                    3 => 1,
                    2 => -3,
                    _ => -5
                }
                : 1;

            return new S15ReviewCardDto
            {
                ReviewId = r.Id,
                ReviewerName = r.Reviewer?.FullName ?? "Độc giả BookShare",
                TargetType = r.TargetType,
                TargetId = r.TargetId,
                TargetDisplayName = targetName,
                LoanId = r.LoanId,
                Rating = r.Rating,
                Comment = r.Comment,
                TrustScoreDelta = delta,
                CreatedAt = r.CreatedAt
            };
        }).ToList();

        var leaderboard = allUsers
            .Select((u, idx) => new S15LeaderboardUserDto
            {
                Rank = idx + 1,
                UserId = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? string.Empty,
                RoleName = u.Roles.FirstOrDefault()?.Name ?? "User",
                TrustScore = u.TrustScore,
                LendQuotaBonus = u.LendQuotaBonus,
                TotalReviewsReceived = reviews.Count(r => r.TargetType == "User" && r.TargetId == u.Id)
            })
            .ToList();

        return new S15TrustScoreReviewPageViewModel
        {
            CurrentUserId = currentUserId,
            CurrentUserName = currentUser?.FullName ?? "Nguyễn Hùng (BIT250157)",
            CurrentUserTrustScore = currentUser?.TrustScore ?? 128,
            CurrentUserLendQuotaBonus = currentUser?.LendQuotaBonus ?? 1,
            BooksToReview = books,
            UsersToReview = usersToReview,
            Leaderboard = leaderboard,
            Reviews = reviewCards
        };
    }

    public async Task<(bool Success, string Message, int NewTargetTrustScore)> SubmitTwoWayReviewAsync(
        string reviewerUserId,
        SubmitReviewRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetId) || string.IsNullOrWhiteSpace(request.Comment))
        {
            return (false, "Vui lòng chọn đối tượng đánh giá và nhập nội dung nhận xét.", 0);
        }

        string cleanTargetType = request.TargetType == "Book" ? "Book" : "User";
        int cleanRating = Math.Clamp(request.Rating, 1, 5);

        var newReview = new Review
        {
            ReviewerId = reviewerUserId,
            TargetType = cleanTargetType,
            TargetId = request.TargetId.Trim(),
            LoanId = request.LoanId,
            Rating = cleanRating,
            Comment = request.Comment.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.Reviews.Add(newReview);

        int updatedTrustScore = 100;
        string deltaMessage;

        if (cleanTargetType == "User")
        {
            var targetUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == request.TargetId.Trim());
            if (targetUser == null)
            {
                return (false, "Không tìm thấy độc giả được đánh giá.", 0);
            }

            int delta = cleanRating switch
            {
                5 => 5,
                4 => 3,
                3 => 1,
                2 => -3,
                _ => -5
            };

            targetUser.TrustScore = Math.Max(0, targetUser.TrustScore + delta);
            updatedTrustScore = targetUser.TrustScore;
            string sign = delta >= 0 ? $"+{delta}" : delta.ToString();
            deltaMessage = $"Đã ghi nhận đánh giá {cleanRating} sao cho {targetUser.FullName}! Điểm TrustScore biến động ({sign} điểm) -> Hiện tại: {updatedTrustScore} điểm.";
        }
        else
        {
            var reviewer = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == reviewerUserId);
            if (reviewer != null)
            {
                reviewer.TrustScore += 1;
                updatedTrustScore = reviewer.TrustScore;
            }
            deltaMessage = $"Đã lưu đánh giá {cleanRating} sao cho đầu sách! Bạn được cộng thưởng +1 TrustScore vì đóng góp review học thuật.";
        }

        await _db.SaveChangesAsync();
        return (true, deltaMessage, updatedTrustScore);
    }
}
