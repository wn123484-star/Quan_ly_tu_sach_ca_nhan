namespace BookShare.Web.Services.Interfaces;

public interface IAntiSpamRateLimiterService
{
    /// <summary>
    /// Kiểm tra và ghi nhận lượt gọi trong cửa sổ trượt (Sliding Window).
    /// Trả về Allowed = false cùng số giây cần chờ nếu vượt ngưỡng chống spam.
    /// </summary>
    (bool Allowed, int RetryAfterSeconds, string Message) TryConsume(
        string scope,
        string actorKey,
        int maxRequests,
        TimeSpan window,
        string? customMessage = null);

    /// <summary>
    /// Ghi nhận 1 lần thử thất bại (ví dụ nhập sai mã OTP hoặc sai mật khẩu).
    /// </summary>
    (int FailedCount, bool IsLockedOut, int RetryAfterSeconds) RecordFailure(
        string scope,
        string actorKey,
        int maxFailures,
        TimeSpan window);

    /// <summary>
    /// Kiểm tra xem một khóa (email / IP / tài khoản) có đang bị khóa tạm do nhập sai liên tiếp không.
    /// </summary>
    bool IsLockedOut(
        string scope,
        string actorKey,
        int maxFailures,
        TimeSpan window,
        out int retryAfterSeconds);

    /// <summary>
    /// Xóa bộ đếm lỗi sau khi người dùng xác thực thành công.
    /// </summary>
    void ClearFailures(string scope, string actorKey);
}

public sealed class RateLimitExceededException : Exception
{
    public int RetryAfterSeconds { get; }

    public RateLimitExceededException(string message, int retryAfterSeconds = 60)
        : base(message)
    {
        RetryAfterSeconds = Math.Max(1, retryAfterSeconds);
    }
}
