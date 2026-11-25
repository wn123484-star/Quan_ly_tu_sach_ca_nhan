namespace BookShare.Web.Services.Interfaces;

/// <summary>
/// [CORE-ANTISPAM] Giao diện dịch vụ Giới hạn tần suất thao tác (Sliding-Window Rate Limiter) và Khóa chống dò mã OTP/Mật khẩu.
/// </summary>
/// <remarks>Triển khai bởi: AntiSpamRateLimiterService.</remarks>
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

/// <summary>
/// [CORE-ANTISPAM] Ngoại lệ nghiệp vụ ném ra khi người dùng/IP vượt ngưỡng giới hạn chống spam; Middleware toàn cục tại Program.cs bắt lại và trả JSON HTTP 429 kèm tiêu đề Retry-After.
/// </summary>
/// <remarks>Dùng thống nhất trên các dịch vụ S01, S10, S13, S15, S17, S18, S19, S20, QR Auth.</remarks>
public sealed class RateLimitExceededException : Exception
{
    public int RetryAfterSeconds { get; }

    public RateLimitExceededException(string message, int retryAfterSeconds = 60)
        : base(message)
    {
        RetryAfterSeconds = Math.Max(1, retryAfterSeconds);
    }
}
