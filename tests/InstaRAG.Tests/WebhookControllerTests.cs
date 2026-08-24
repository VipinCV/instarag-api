using InstaRAG.Api.Configuration;
using InstaRAG.Api.Controllers;
using InstaRAG.Api.Models;
using InstaRAG.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace InstaRAG.Tests;

public class WebhookControllerTests
{
    private readonly Mock<IRagService> _ragServiceMock;
    private readonly Mock<IInstagramService> _instagramServiceMock;
    private readonly Mock<IRateLimiterService> _rateLimiterMock;
    private readonly Mock<ILogger<WebhookController>> _loggerMock;
    private readonly WebhookController _controller;

    public WebhookControllerTests()
    {
        var metaSettings = Options.Create(new MetaSettings
        {
            VerifyToken = "test_token_123",
            PageId = "123456",
            PageAccessToken = "fake_token",
            ApiVersion = "v21.0"
        });

        _ragServiceMock = new Mock<IRagService>();
        _instagramServiceMock = new Mock<IInstagramService>();
        _rateLimiterMock = new Mock<IRateLimiterService>();
        _loggerMock = new Mock<ILogger<WebhookController>>();

        _controller = new WebhookController(
            metaSettings,
            _ragServiceMock.Object,
            _instagramServiceMock.Object,
            _rateLimiterMock.Object,
            _loggerMock.Object);
    }

    // ─── Webhook Verification Tests ─────────────────────────────────────

    [Fact]
    public void Verify_WithValidToken_ReturnsChallengeResponse()
    {
        // Arrange
        var mode = "subscribe";
        var verifyToken = "test_token_123";
        var challenge = "challenge_abc_123";

        // Act
        var result = _controller.Verify(mode, verifyToken, challenge);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(challenge, okResult.Value);
    }

    [Fact]
    public void Verify_WithInvalidToken_ReturnsForbid()
    {
        // Act
        var result = _controller.Verify("subscribe", "wrong_token", "challenge");

        // Assert
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public void Verify_WithInvalidMode_ReturnsForbid()
    {
        // Act
        var result = _controller.Verify("unsubscribe", "test_token_123", "challenge");

        // Assert
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public void Verify_WithNullParameters_ReturnsForbid()
    {
        // Act
        var result = _controller.Verify(null, null, null);

        // Assert
        Assert.IsType<ForbidResult>(result);
    }

    // ─── Message Receiving Tests ────────────────────────────────────────

    [Fact]
    public void ReceiveMessage_WithInstagramPayload_ReturnsOkEventReceived()
    {
        // Arrange
        var payload = CreateTestPayload("Hello, do you have sneakers?");

        // Act
        var result = _controller.ReceiveMessage(payload);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("EVENT_RECEIVED", okResult.Value);
    }

    [Fact]
    public void ReceiveMessage_WithNonInstagramPayload_ReturnsOkEventReceived()
    {
        // Arrange
        var payload = new WebhookPayload { Object = "page" };

        // Act
        var result = _controller.ReceiveMessage(payload);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("EVENT_RECEIVED", okResult.Value);
    }

    [Fact]
    public void ReceiveMessage_WithEmptyPayload_ReturnsOk()
    {
        // Arrange
        var payload = new WebhookPayload
        {
            Object = "instagram",
            Entry = new List<WebhookEntry>()
        };

        // Act
        var result = _controller.ReceiveMessage(payload);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static WebhookPayload CreateTestPayload(string messageText)
    {
        return new WebhookPayload
        {
            Object = "instagram",
            Entry = new List<WebhookEntry>
            {
                new()
                {
                    Id = "entry_1",
                    Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Messaging = new List<MessagingEvent>
                    {
                        new()
                        {
                            Sender = new ParticipantId { Id = "user_123" },
                            Recipient = new ParticipantId { Id = "page_456" },
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            Message = new IncomingMessage
                            {
                                Mid = "mid_abc_123",
                                Text = messageText
                            }
                        }
                    }
                }
            }
        };
    }
}
