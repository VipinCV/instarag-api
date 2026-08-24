namespace InstaRAG.Api.Configuration;

/// <summary>
/// Configuration for the per-user rate limiter.
/// Bound from the "RateLimit" section in appsettings.json.
/// </summary>
public class RateLimitSettings
{
    public const string SectionName = "RateLimit";

    /// <summary>Maximum number of requests allowed per user within the time window.</summary>
    public int MaxRequests { get; set; } = 10;

    /// <summary>Sliding window duration in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;
}
