using System.Text.Json;
using InstaRAG.Api.Configuration;
using InstaRAG.Api.Models;
using InstaRAG.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InstaRAG.Api.Controllers;

/// <summary>
/// Handles Instagram Webhook verification and incoming message events.
/// </summary>
[ApiController]
[Route("api/webhook/instagram")]
public class WebhookController : ControllerBase
{
    private readonly MetaSettings _metaSettings;
    private readonly IRagService _ragService;
    private readonly IInstagramService _instagramService;
    private readonly IRateLimiterService _rateLimiter;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IOptions<MetaSettings> metaSettings,
        IRagService ragService,
        IInstagramService instagramService,
        IRateLimiterService rateLimiter,
        ILogger<WebhookController> logger)
    {
        _metaSettings = metaSettings.Value;
        _ragService = ragService;
        _instagramService = instagramService;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    /// <summary>
    /// Webhook verification endpoint.
    /// Meta sends a GET request with hub.mode, hub.verify_token, and hub.challenge.
    /// We must validate the token and return the challenge to confirm subscription.
    /// </summary>
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        _logger.LogInformation("Webhook verification request. Mode: {Mode}", mode);

        if (mode == "subscribe" && verifyToken == _metaSettings.VerifyToken)
        {
            _logger.LogInformation("Webhook verified successfully");
            return Ok(challenge);
        }

        _logger.LogWarning("Webhook verification failed. Token mismatch or invalid mode");
        return Forbid();
    }

    /// <summary>
    /// Receives incoming Instagram message events.
    /// Returns 200 OK immediately (Meta requirement), then processes asynchronously.
    /// </summary>
    [HttpPost]
    public IActionResult ReceiveMessage([FromBody] WebhookPayload payload)
    {
        _logger.LogInformation("Received webhook payload. Object: {Object}, Entries: {Count}",
            payload.Object, payload.Entry.Count);

        if (payload.Object != "instagram")
        {
            _logger.LogWarning("Ignoring non-Instagram webhook. Object: {Object}", payload.Object);
            return Ok("EVENT_RECEIVED");
        }

        // Process each message asynchronously — don't block the response
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessPayloadAsync(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing webhook payload");
            }
        });

        // Meta requires a fast 200 response to avoid retries
        return Ok("EVENT_RECEIVED");
    }

    private async Task ProcessPayloadAsync(WebhookPayload payload)
    {
        foreach (var entry in payload.Entry)
        {
            foreach (var messagingEvent in entry.Messaging)
            {
                // Skip events without a text message (e.g., attachments, reactions)
                if (messagingEvent.Message?.Text == null)
                {
                    _logger.LogDebug("Skipping non-text message event from {SenderId}",
                        messagingEvent.Sender.Id);
                    continue;
                }

                var senderId = messagingEvent.Sender.Id;
                var messageText = messagingEvent.Message.Text;

                _logger.LogInformation("Processing message from {SenderId}: {Message}",
                    senderId, messageText);

                // Check rate limit
                if (!_rateLimiter.IsAllowed(senderId))
                {
                    _logger.LogWarning("Rate limit exceeded for sender {SenderId}. Message dropped.", senderId);
                    // Optionally send a rate-limit notification (once per window)
                    await _instagramService.SendMessageAsync(senderId,
                        "You're sending messages too quickly! ⏳ Please wait a moment and try again.");
                    continue;
                }

                // Generate answer via RAG pipeline
                var answer = await _ragService.AnswerQuestionAsync(messageText);

                // Send the reply
                var sent = await _instagramService.SendMessageAsync(senderId, answer);

                if (sent)
                {
                    _logger.LogInformation("Reply sent successfully to {SenderId}", senderId);
                }
                else
                {
                    _logger.LogError("Failed to send reply to {SenderId}", senderId);
                }
            }
        }
    }
}
