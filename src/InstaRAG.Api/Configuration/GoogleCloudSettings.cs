namespace InstaRAG.Api.Configuration;

/// <summary>
/// Strongly-typed configuration for Google Cloud / Vertex AI settings.
/// Bound from the "GoogleCloud" section in appsettings.json.
/// </summary>
public class GoogleCloudSettings
{
    public const string SectionName = "GoogleCloud";

    /// <summary>GCP Project ID.</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>GCP region for Vertex AI (e.g., "asia-south1").</summary>
    public string Location { get; set; } = "asia-south1";

    /// <summary>
    /// Full resource name of the RAG corpus.
    /// Format: projects/{project}/locations/{location}/ragCorpora/{corpus_id}
    /// </summary>
    public string RagCorpusResourceName { get; set; } = string.Empty;

    /// <summary>Human-readable display name for the RAG corpus.</summary>
    public string RagCorpusDisplayName { get; set; } = "my-product-catalog";

    /// <summary>GCS bucket name for storing product documents.</summary>
    public string GcsBucketName { get; set; } = string.Empty;

    /// <summary>
    /// The full JSON content of the Google Cloud Service Account key, or a file path to it.
    /// This avoids needing the GOOGLE_APPLICATION_CREDENTIALS environment variable.
    /// </summary>
    public string ServiceAccountJson { get; set; } = string.Empty;

    /// <summary>
    /// Gets the raw Service Account JSON text. 
    /// Unescapes newlines to correctly parse JSON injected via environment variables in platforms like Render.
    /// </summary>
    public string GetParsedServiceAccountJson()
    {
        if (string.IsNullOrWhiteSpace(ServiceAccountJson))
        {
            return string.Empty;
        }

        // If the value looks like a file path, read the file content
        if (System.IO.File.Exists(ServiceAccountJson))
        {
            try
            {
                return System.IO.File.ReadAllText(ServiceAccountJson);
            }
            catch (Exception)
            {
                // If we can’t read the file, fall back to empty string (RagService will log a warning)
                return string.Empty;
            }
        }

        // Return the injected JSON, unescaping newlines for cloud environments (e.g., Render, Docker)
        return ServiceAccountJson.Trim().Replace("\\n", "\n");
    }

    /// <summary>Base URL for Vertex AI API in the configured region.</summary>
    public string VertexAiBaseUrl =>
        $"https://{Location}-aiplatform.googleapis.com/v1";

    /// <summary>Full endpoint URL for retrieving contexts from the RAG corpus.</summary>
    public string RetrieveContextsUrl =>
        $"{VertexAiBaseUrl}/projects/{ProjectId}/locations/{Location}:retrieveContexts";

    /// <summary>Full endpoint URL for creating a new RAG corpus.</summary>
    public string CreateCorpusUrl =>
        $"{VertexAiBaseUrl}/projects/{ProjectId}/locations/{Location}/ragCorpora";

    /// <summary>Builds the import files URL for a given corpus resource name.</summary>
    public string GetImportFilesUrl(string corpusResourceName) =>
        $"{VertexAiBaseUrl}/{corpusResourceName}:importRagFiles";
}
