namespace InstaRAG.Api.Configuration;

/// <summary>
/// Strongly-typed configuration for Meta/Instagram API credentials.
/// Bound from the "Meta" section in appsettings.json.
/// </summary>
public class MetaSettings
{
    public const string SectionName = "Meta";

    /// <summary>Meta App ID from the developer dashboard.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Meta App Secret for webhook signature verification.</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Page Access Token with instagram_manage_messages permission.</summary>
    public string PageAccessToken { get; set; } = string.Empty;

    /// <summary>Custom verify token for webhook subscription handshake.</summary>
    public string VerifyToken { get; set; } = string.Empty;

    /// <summary>Facebook Page ID linked to the Instagram Professional account.</summary>
    public string PageId { get; set; } = string.Empty;

    /// <summary>Graph API version (e.g., "v21.0").</summary>
    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>Builds the full send-message endpoint URL.</summary>
    public string SendMessageUrl =>
        $"https://graph.facebook.com/{ApiVersion}/{PageId}/messages";
}
