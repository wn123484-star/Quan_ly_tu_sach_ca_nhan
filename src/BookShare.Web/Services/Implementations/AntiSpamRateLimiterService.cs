using BookShare.Web.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace BookShare.Web.Services.Implementations;

public class AntiSpamRateLimiterService : IAntiSpamRateLimiterService
{
    private readonly IMemoryCache _cache;

    public AntiSpamRateLimiterService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public (bool Allowed, int RetryAfterSeconds, string Message) TryConsume(
        string scope,
        string actorKey,
        int maxRequests,
        TimeSpan window,
        string? customMessage = null)
    {
        if (string.IsNullOrWhiteSpace(actorKey))
        {
            actorKey = "anonymous";
        }

        string cacheKey = $"bs:rl:{scope.Trim().ToLowerInvariant()}:{actorKey.Trim().ToLowerInvariant()}";
        var now = DateTime.UtcNow;
        var cutoff = now - window;

        var bucket = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.SlidingExpiration = window + TimeSpan.FromMinutes(1);
            return new List<DateTime>();
        }) ?? new List<DateTime>();

        lock (bucket)
        {
            bucket.RemoveAll(t => t <= cutoff);
            if (bucket.Count >= maxRequests)
            {
                var oldest = bucket[0];
                int retryAfter = Math.Max(1, (int)Math.Ceiling((oldest + window - now).TotalSeconds));
                string msg = string.IsNullOrWhiteSpace(customMessage)
                    ? $"Bạn thao tác quá nhanh (giới hạn {maxRequests} lần). Vui lòng thử lại sau {retryAfter} giây để chống spam hệ thống."
                    : customMessage;
                return (false, retryAfter, msg);
            }

            bucket.Add(now);
            return (true, 0, string.Empty);
        }
    }

    public (int FailedCount, bool IsLockedOut, int RetryAfterSeconds) RecordFailure(
        string scope,
        string actorKey,
        int maxFailures,
        TimeSpan window)
    {
        string cacheKey = $"bs:fail:{scope.Trim().ToLowerInvariant()}:{(actorKey ?? "anonymous").Trim().ToLowerInvariant()}";
        var now = DateTime.UtcNow;
        var cutoff = now - window;

        var bucket = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.SlidingExpiration = window + TimeSpan.FromMinutes(2);
            return new List<DateTime>();
        }) ?? new List<DateTime>();

        lock (bucket)
        {
            bucket.RemoveAll(t => t <= cutoff);
            bucket.Add(now);
            bool locked = bucket.Count >= maxFailures;
            int retryAfter = locked && bucket.Count > 0
                ? Math.Max(1, (int)Math.Ceiling((bucket[0] + window - now).TotalSeconds))
                : 0;
            return (bucket.Count, locked, retryAfter);
        }
    }

    public bool IsLockedOut(
        string scope,
        string actorKey,
        int maxFailures,
        TimeSpan window,
        out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        string cacheKey = $"bs:fail:{scope.Trim().ToLowerInvariant()}:{(actorKey ?? "anonymous").Trim().ToLowerInvariant()}";
        if (!_cache.TryGetValue(cacheKey, out List<DateTime>? bucket) || bucket == null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var cutoff = now - window;
        lock (bucket)
        {
            bucket.RemoveAll(t => t <= cutoff);
            if (bucket.Count >= maxFailures)
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((bucket[0] + window - now).TotalSeconds));
                return true;
            }
            return false;
        }
    }

    public void ClearFailures(string scope, string actorKey)
    {
        string cacheKey = $"bs:fail:{scope.Trim().ToLowerInvariant()}:{(actorKey ?? "anonymous").Trim().ToLowerInvariant()}";
        _cache.Remove(cacheKey);
    }
}
