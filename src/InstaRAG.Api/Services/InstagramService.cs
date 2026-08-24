using System.Text;
using System.Text.Json;
using InstaRAG.Api.Configuration;
using InstaRAG.Api.Models;
using Microsoft.Extensions.Options;

namespace InstaRAG.Api.Services;

/// <summary>
/// Sends messages to Instagram users via the Meta Graph API.
/// Features: retry with exponential backoff, long message splitting, structured error logging.
/// </summary>
public class InstagramService : IInstagramService
{
    private const int MaxMessageLength = 1000;
    private const int MaxRetries = 3;

    private readonly HttpClient _httpClient;
    private readonly MetaSettings _metaSettings;
    private readonly ILogger<InstagramService> _logger;

    public InstagramService(
        IHttpClientFactory httpClientFactory,
        IOptions<MetaSettings> metaSettings,
        ILogger<InstagramService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Instagram");
        _metaSettings = metaSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> SendMessageAsync(string recipientId, string text, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending message to recipient {RecipientId}. Length: {Length} chars",
            recipientId, text.Length);

        // Split into multiple messages if text exceeds Instagram's limit
        var messageParts = SplitMessage(text);
        var allSuccess = true;

        for (int i = 0; i < messageParts.Count; i++)
        {
            var success = await SendSingleMessageWithRetryAsync(recipientId, messageParts[i], i + 1, messageParts.Count, cancellationToken);
            if (!success)
            {
                allSuccess = false;
                _logger.LogError("Failed to send message part {Part}/{Total} to {RecipientId}",
                    i + 1, messageParts.Count, recipientId);
                break; // Don't send subsequent parts if one fails
            }

            // Small delay between parts to avoid rate limiting
            if (i < messageParts.Count - 1)
            {
                await Task.Delay(500, cancellationToken);
            }
        }

        return allSuccess;
    }

    private async Task<bool> SendSingleMessageWithRetryAsync(
        string recipientId, string text, int partNumber, int totalParts,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var request = new SendMessageRequest
                {
                    Recipient = new ParticipantId { Id = recipientId },
                    Message = new OutgoingMessage { Text = text }
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"{_metaSettings.SendMessageUrl}?access_token={_metaSettings.PageAccessToken}";
                var response = await _httpClient.PostAsync(url, content, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<SendMessageResponse>(responseBody);
                    _logger.LogInformation(
                        "Message sent successfully. Part: {Part}/{Total}, Recipient: {RecipientId}, MessageId: {MessageId}",
                        partNumber, totalParts, recipientId, result?.MessageId);
                    return true;
                }

                _logger.LogWarning(
                    "Send attempt {Attempt}/{MaxRetries} failed. Status: {Status}, Body: {Body}, Recipient: {RecipientId}",
                    attempt, MaxRetries, response.StatusCode, responseBody, recipientId);

                // Don't retry on 4xx client errors (except 429 rate limit)
                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500 && response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogError("Client error sending message — not retrying. Status: {Status}", response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception on attempt {Attempt}/{MaxRetries} sending message to {RecipientId}",
                    attempt, MaxRetries, recipientId);
            }

            // Exponential backoff before retry
            if (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogDebug("Retrying in {Delay}s...", delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        _logger.LogError("All {MaxRetries} attempts failed for recipient {RecipientId}", MaxRetries, recipientId);
        return false;
    }

    /// <summary>
    /// Splits a long message into parts that fit within Instagram's character limit.
    /// Splits at sentence boundaries when possible.
    /// </summary>
    private static List<string> SplitMessage(string text)
    {
        if (text.Length <= MaxMessageLength)
            return new List<string> { text };

        var parts = new List<string>();
        var remaining = text;

        while (remaining.Length > 0)
        {
            if (remaining.Length <= MaxMessageLength)
            {
                parts.Add(remaining);
                break;
            }

            // Try to split at the last sentence boundary within the limit
            var chunk = remaining[..MaxMessageLength];
            var lastPeriod = chunk.LastIndexOf(". ", StringComparison.Ordinal);
            var lastNewline = chunk.LastIndexOf('\n');
            var splitAt = Math.Max(lastPeriod, lastNewline);

            if (splitAt <= MaxMessageLength / 2)
            {
                // No good split point found — split at last space
                splitAt = chunk.LastIndexOf(' ');
            }

            if (splitAt <= 0)
            {
                // No space found — hard split
                splitAt = MaxMessageLength;
            }
            else
            {
                splitAt++; // Include the delimiter
            }

            parts.Add(remaining[..splitAt].TrimEnd());
            remaining = remaining[splitAt..].TrimStart();
        }

        return parts;
    }
}
