using System.Collections.Concurrent;
using InstaRAG.Api.Configuration;
using Microsoft.Extensions.Options;

namespace InstaRAG.Api.Services;

/// <summary>
/// In-memory sliding window rate limiter using ConcurrentDictionary.
/// Tracks request timestamps per user and enforces max requests within a time window.
/// </summary>
public class RateLimiterService : IRateLimiterService, IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _userRequests = new();
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly ILogger<RateLimiterService> _logger;
    private readonly Timer _cleanupTimer;

    public RateLimiterService(IOptions<RateLimitSettings> settings, ILogger<RateLimiterService> logger)
    {
        _maxRequests = settings.Value.MaxRequests;
        _window = TimeSpan.FromSeconds(settings.Value.WindowSeconds);
        _logger = logger;

        // Clean up stale entries every 5 minutes
        _cleanupTimer = new Timer(CleanupStaleEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <inheritdoc />
    public bool IsAllowed(string userId)
    {
        var now = DateTime.UtcNow;
        var queue = _userRequests.GetOrAdd(userId, _ => new ConcurrentQueue<DateTime>());

        // Remove expired timestamps from the front of the queue
        while (queue.TryPeek(out var oldest) && (now - oldest) > _window)
        {
            queue.TryDequeue(out _);
        }

        if (queue.Count >= _maxRequests)
        {
            _logger.LogWarning("Rate limit exceeded for user {UserId}. Count: {Count}/{Max}",
                userId, queue.Count, _maxRequests);
            return false;
        }

        queue.Enqueue(now);
        _logger.LogDebug("Request allowed for user {UserId}. Count: {Count}/{Max}",
            userId, queue.Count, _maxRequests);
        return true;
    }

    private void CleanupStaleEntries(object? state)
    {
        var now = DateTime.UtcNow;
        var staleKeys = new List<string>();

        foreach (var kvp in _userRequests)
        {
            // Remove expired entries
            while (kvp.Value.TryPeek(out var oldest) && (now - oldest) > _window)
            {
                kvp.Value.TryDequeue(out _);
            }

            // Mark empty queues for removal
            if (kvp.Value.IsEmpty)
            {
                staleKeys.Add(kvp.Key);
            }
        }

        foreach (var key in staleKeys)
        {
            _userRequests.TryRemove(key, out _);
        }

        if (staleKeys.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} stale rate limiter entries", staleKeys.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
