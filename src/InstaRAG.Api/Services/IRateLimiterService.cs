namespace InstaRAG.Api.Services;

/// <summary>
/// Per-user sliding window rate limiter.
/// </summary>
public interface IRateLimiterService
{
    /// <summary>
    /// Checks if the given user is allowed to make a request.
    /// Returns true if allowed, false if rate limit exceeded.
    /// </summary>
    bool IsAllowed(string userId);
}
