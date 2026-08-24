namespace InstaRAG.Api.Services;

/// <summary>
/// Service for sending messages via the Instagram Messaging API.
/// </summary>
public interface IInstagramService
{
    /// <summary>
    /// Sends a text message to a recipient via Instagram DM.
    /// Handles message splitting for long texts and retry logic.
    /// </summary>
    Task<bool> SendMessageAsync(string recipientId, string text, CancellationToken cancellationToken = default);
}
