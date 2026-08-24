using InstaRAG.Api.Configuration;
using InstaRAG.Api.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InstaRAG.Tests;

public class RateLimiterServiceTests
{
    private RateLimiterService CreateService(int maxRequests = 3, int windowSeconds = 60)
    {
        var settings = Options.Create(new RateLimitSettings
        {
            MaxRequests = maxRequests,
            WindowSeconds = windowSeconds
        });
        var logger = new Mock<ILogger<RateLimiterService>>();
        return new RateLimiterService(settings, logger.Object);
    }

    [Fact]
    public void IsAllowed_FirstRequest_ReturnsTrue()
    {
        // Arrange
        using var service = CreateService();

        // Act
        var result = service.IsAllowed("user_1");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsAllowed_WithinLimit_ReturnsTrue()
    {
        // Arrange
        using var service = CreateService(maxRequests: 5);

        // Act & Assert
        for (int i = 0; i < 5; i++)
        {
            Assert.True(service.IsAllowed("user_1"), $"Request {i + 1} should be allowed");
        }
    }

    [Fact]
    public void IsAllowed_ExceedsLimit_ReturnsFalse()
    {
        // Arrange
        using var service = CreateService(maxRequests: 3);

        // Act - exhaust the limit
        service.IsAllowed("user_1");
        service.IsAllowed("user_1");
        service.IsAllowed("user_1");

        // Assert - 4th request should be denied
        Assert.False(service.IsAllowed("user_1"));
    }

    [Fact]
    public void IsAllowed_DifferentUsers_AreIndependent()
    {
        // Arrange
        using var service = CreateService(maxRequests: 2);

        // Exhaust user_1's limit
        service.IsAllowed("user_1");
        service.IsAllowed("user_1");
        Assert.False(service.IsAllowed("user_1"));

        // user_2 should still be allowed
        Assert.True(service.IsAllowed("user_2"));
        Assert.True(service.IsAllowed("user_2"));
        Assert.False(service.IsAllowed("user_2"));
    }

    [Fact]
    public async Task IsAllowed_AfterWindowExpires_AllowsAgain()
    {
        // Arrange - use a very short window (1 second)
        using var service = CreateService(maxRequests: 1, windowSeconds: 1);

        // Act - exhaust the limit
        Assert.True(service.IsAllowed("user_1"));
        Assert.False(service.IsAllowed("user_1"));

        // Wait for the window to expire
        await Task.Delay(1100);

        // Assert - should be allowed again
        Assert.True(service.IsAllowed("user_1"));
    }

    [Fact]
    public void IsAllowed_ExactlyAtLimit_LastRequestIsAllowed()
    {
        // Arrange
        using var service = CreateService(maxRequests: 3);

        // Act & Assert
        Assert.True(service.IsAllowed("user_1")); // 1
        Assert.True(service.IsAllowed("user_1")); // 2
        Assert.True(service.IsAllowed("user_1")); // 3 (exactly at limit)
        Assert.False(service.IsAllowed("user_1")); // 4 (exceeds limit)
    }

    [Fact]
    public void IsAllowed_SingleRequestLimit_Works()
    {
        // Arrange
        using var service = CreateService(maxRequests: 1);

        // Act & Assert
        Assert.True(service.IsAllowed("user_1"));
        Assert.False(service.IsAllowed("user_1"));
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        var exception = Record.Exception(() => service.Dispose());
        Assert.Null(exception);
    }
}
