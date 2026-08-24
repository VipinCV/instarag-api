using System.Text.Json.Serialization;

namespace InstaRAG.Api.Models;

/// <summary>
/// Root webhook payload sent by Meta/Instagram.
/// </summary>
public class WebhookPayload
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    [JsonPropertyName("entry")]
    public List<WebhookEntry> Entry { get; set; } = new();
}

public class WebhookEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("messaging")]
    public List<MessagingEvent> Messaging { get; set; } = new();
}

public class MessagingEvent
{
    [JsonPropertyName("sender")]
    public ParticipantId Sender { get; set; } = new();

    [JsonPropertyName("recipient")]
    public ParticipantId Recipient { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("message")]
    public IncomingMessage? Message { get; set; }
}

public class ParticipantId
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public class IncomingMessage
{
    [JsonPropertyName("mid")]
    public string Mid { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("attachments")]
    public List<Attachment>? Attachments { get; set; }
}

public class Attachment
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public AttachmentPayload? Payload { get; set; }
}

public class AttachmentPayload
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

/// <summary>
/// Request body for sending a message via the Instagram Graph API.
/// </summary>
public class SendMessageRequest
{
    [JsonPropertyName("recipient")]
    public ParticipantId Recipient { get; set; } = new();

    [JsonPropertyName("message")]
    public OutgoingMessage Message { get; set; } = new();
}

public class OutgoingMessage
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Response from the Instagram Graph API send endpoint.
/// </summary>
public class SendMessageResponse
{
    [JsonPropertyName("recipient_id")]
    public string? RecipientId { get; set; }

    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("error")]
    public GraphApiError? Error { get; set; }
}

public class GraphApiError
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public int Code { get; set; }
}
