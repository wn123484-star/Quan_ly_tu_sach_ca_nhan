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
                ReviewerId = r.ReviewerId,
                IsOwner = r.ReviewerId == currentUserId,
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
            CurrentUserName = currentUser?.FullName ?? "Độc giả BookShare",
            CurrentUserTrustScore = currentUser?.TrustScore ?? 100,
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
        string cleanTargetId = request.TargetId.Trim();
        int cleanRating = Math.Clamp(request.Rating, 1, 5);

        if (cleanTargetType == "User" && cleanTargetId == reviewerUserId)
        {
            return (false, "Bạn không thể tự đánh giá chính tài khoản của mình.", 0);
        }

        if (request.LoanId.HasValue)
        {
            bool loanExists = await _db.Loans.AnyAsync(l => l.Id == request.LoanId.Value);
            if (!loanExists)
            {
                return (false, "Không tìm thấy hợp đồng mượn sách hợp lệ.", 0);
            }

            bool duplicateLoanReview = await _db.Reviews.AnyAsync(r =>
                r.ReviewerId == reviewerUserId &&
                r.LoanId == request.LoanId.Value &&
                r.TargetType == cleanTargetType);
            if (duplicateLoanReview)
            {
                return (false, "Bạn đã gửi đánh giá cho lượt mượn này trước đó.", 0);
            }
        }
        else if (cleanTargetType == "Book")
        {
            bool duplicateBookReview = await _db.Reviews.AnyAsync(r =>
                r.ReviewerId == reviewerUserId &&
                r.TargetType == "Book" &&
                r.TargetId == cleanTargetId);
            if (duplicateBookReview)
            {
                return (false, "Bạn đã đánh giá đầu sách này trước đó. Bạn có thể chỉnh sửa đánh giá hiện có.", 0);
            }
        }

        var newReview = new Review
        {
            ReviewerId = reviewerUserId,
            TargetType = cleanTargetType,
            TargetId = cleanTargetId,
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
            var targetUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == cleanTargetId);
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

            targetUser.TrustScore = Math.Clamp(targetUser.TrustScore + delta, 0, 200);
            updatedTrustScore = targetUser.TrustScore;
            string sign = delta >= 0 ? $"+{delta}" : delta.ToString();
            deltaMessage = $"Đã ghi nhận đánh giá {cleanRating} sao cho {targetUser.FullName}! Điểm uy tín thay đổi ({sign} điểm) -> Hiện tại: {updatedTrustScore} điểm.";
        }
        else
        {
            if (!int.TryParse(cleanTargetId, out int bookId) || !await _db.Books.AnyAsync(b => b.Id == bookId))
            {
                return (false, "Không tìm thấy đầu sách cần đánh giá.", 0);
            }

            var reviewer = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == reviewerUserId);
            if (reviewer != null)
            {
                reviewer.TrustScore = Math.Clamp(reviewer.TrustScore + 1, 0, 200);
                updatedTrustScore = reviewer.TrustScore;
            }
            deltaMessage = "Đã lưu đánh giá đầu sách thành công! Bạn được cộng thưởng +1 điểm uy tín vì đóng góp nhận xét hữu ích.";
        }

        await _db.SaveChangesAsync();
        return (true, deltaMessage, updatedTrustScore);
    }

    public async Task<(bool Success, string Message)> UpdateReviewAsync(
        string currentUserId,
        string currentUserRole,
        UpdateReviewRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Comment))
        {
            return (false, "Nội dung nhận xét không được để trống.");
        }

        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == request.ReviewId);
        if (review == null)
        {
            return (false, "Không tìm thấy đánh giá cần chỉnh sửa.");
        }

        if (review.ReviewerId != currentUserId && currentUserRole != "Admin")
        {
            throw new UnauthorizedAccessException("Bạn không có quyền chỉnh sửa nhận xét của thành viên khác.");
        }

        int oldRating = review.Rating;
        int newRating = Math.Clamp(request.Rating, 1, 5);

        if (review.TargetType == "User" && oldRating != newRating)
        {
            var targetUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == review.TargetId);
            if (targetUser != null)
            {
                int oldDelta = oldRating switch { 5 => 5, 4 => 3, 3 => 1, 2 => -3, _ => -5 };
                int newDelta = newRating switch { 5 => 5, 4 => 3, 3 => 1, 2 => -3, _ => -5 };
                targetUser.TrustScore = Math.Clamp(targetUser.TrustScore - oldDelta + newDelta, 0, 200);
            }
        }

        review.Rating = newRating;
        review.Comment = request.Comment.Trim();
        await _db.SaveChangesAsync();

        return (true, $"Đã cập nhật đánh giá #{review.Id} ({newRating} sao) và đồng bộ điểm uy tín!");
    }

    public async Task<(bool Success, string Message)> DeleteReviewAsync(
        string currentUserId,
        string currentUserRole,
        int reviewId)
    {
        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review == null)
        {
            return (false, "Không tìm thấy đánh giá cần xóa.");
        }

        if (review.ReviewerId != currentUserId && currentUserRole != "Admin")
        {
            throw new UnauthorizedAccessException("Chỉ người viết đánh giá hoặc Quản trị viên mới được quyền xóa đánh giá này.");
        }

        if (review.TargetType == "User")
        {
            var targetUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == review.TargetId);
            if (targetUser != null)
            {
                int oldDelta = review.Rating switch { 5 => 5, 4 => 3, 3 => 1, 2 => -3, _ => -5 };
                targetUser.TrustScore = Math.Clamp(targetUser.TrustScore - oldDelta, 0, 200);
            }
        }
        else if (review.TargetType == "Book")
        {
            var reviewer = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == review.ReviewerId);
            if (reviewer != null)
            {
                reviewer.TrustScore = Math.Clamp(reviewer.TrustScore - 1, 0, 200);
            }
        }

        _db.Reviews.Remove(review);
        await _db.SaveChangesAsync();

        return (true, $"Đã xóa đánh giá #{reviewId} và hoàn tác biến động điểm uy tín!");
    }
        }
