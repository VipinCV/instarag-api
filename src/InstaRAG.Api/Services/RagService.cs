using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth.OAuth2;
using Google.GenAI;
using InstaRAG.Api.Configuration;
using InstaRAG.Api.Prompts;
using Microsoft.Extensions.Options;

namespace InstaRAG.Api.Services;

/// <summary>
/// Implements the RAG pipeline:
/// 1. Retrieve relevant context from Vertex AI RAG Engine via REST API
/// 2. Generate a response using Gemini 2.5 Flash via Google.GenAI SDK
/// </summary>
public class RagService : IRagService
{
    private readonly HttpClient _httpClient;
    private readonly GoogleCloudSettings _gcpSettings;
    private readonly ILogger<RagService> _logger;
    private readonly Client _geminiClient;
    private const string GeminiModel = "gemini-2.5-flash";

    public RagService(
        IHttpClientFactory httpClientFactory,
        IOptions<GoogleCloudSettings> gcpSettings,
        ILogger<RagService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("VertexAI");
        _gcpSettings = gcpSettings.Value;
        _logger = logger;

        try
        {
            var jsonText = _gcpSettings.GetParsedServiceAccountJson();
            var googleCredential = GoogleCredential.FromJson(jsonText);
            
            // Initialize the Gemini client for Vertex AI (Enterprise / GEAP)
            _geminiClient = new Client(
                project: _gcpSettings.ProjectId,
                location: _gcpSettings.Location,
                enterprise: true,
                credential: googleCredential
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Google GenAI Client with ServiceAccountJson. RAG operations will fail.");
            _geminiClient = null!;
        }
    }

    /// <inheritdoc />
    public async Task<string> RetrieveContextAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving context for query: {Query}", query);

        try
        {
            var jsonText = _gcpSettings.GetParsedServiceAccountJson();
            // Parse credential directly from config string or file
            var credential = GoogleCredential.FromJson(jsonText)
                .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
                
            var accessToken = await credential
                .UnderlyingCredential
                .GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);

            // Build the retrieval request
            var requestBody = new
            {
                vertex_rag_store = new
                {
                    rag_resources = new[]
                    {
                        new { rag_corpus = _gcpSettings.RagCorpusResourceName }
                    }
                },
                query = new { text = query },
                rag_retrieval_config = new
                {
                    top_k = 5,
                    filter = new { vector_distance_threshold = 0.5 }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.PostAsync(
                _gcpSettings.RetrieveContextsUrl,
                content,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("RAG retrieval failed. Status: {Status}, Body: {Body}",
                    response.StatusCode, responseBody);
                return string.Empty;
            }

            // Parse the retrieval response and extract context chunks
            var retrievalResponse = JsonSerializer.Deserialize<RetrievalResponse>(responseBody);
            if (retrievalResponse?.Contexts?.Contexts == null || retrievalResponse.Contexts.Contexts.Count == 0)
            {
                _logger.LogWarning("No relevant context found for query: {Query}", query);
                return string.Empty;
            }

            var contextChunks = retrievalResponse.Contexts.Contexts
                .Where(c => !string.IsNullOrWhiteSpace(c.Text))
                .Select(c => c.Text!.Trim());

            var combinedContext = string.Join("\n\n---\n\n", contextChunks);

            _logger.LogInformation("Retrieved {Count} context chunks for query",
                retrievalResponse.Contexts.Contexts.Count);

            return combinedContext;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving context from RAG Engine");
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateAnswerAsync(string query, string context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating answer for query: {Query}", query);

        try
        {
            // Build the prompt from the template
            var prompt = SystemPrompts.ProductAssistant
                .Replace("{context}", string.IsNullOrWhiteSpace(context)
                    ? "No relevant product information was found in the knowledge base."
                    : context)
                .Replace("{query}", query);

            var response = await _geminiClient.Models.GenerateContentAsync(
                model: GeminiModel,
                contents: prompt
            );

            var answer = response?.Candidates?.FirstOrDefault()
                ?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(answer))
            {
                _logger.LogWarning("Gemini returned empty response for query: {Query}", query);
                return "I'm sorry, I couldn't generate a response right now. Please try again later or contact our support team! 😊";
            }

            _logger.LogInformation("Generated answer successfully. Length: {Length} chars", answer.Length);
            return answer.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating answer with Gemini");
            return "I'm sorry, something went wrong on my end! 😅 Please try again in a moment, or reach out to our support team for help.";
        }
    }

    /// <inheritdoc />
    public async Task<string> AnswerQuestionAsync(string query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing question: {Query}", query);

        // Step 1: Retrieve relevant context
        var context = await RetrieveContextAsync(query, cancellationToken);

        // Step 2: Generate answer using context + query
        var answer = await GenerateAnswerAsync(query, context, cancellationToken);

        return answer;
    }
}

#region Vertex AI RAG Response Models

/// <summary>
/// Models for deserializing the Vertex AI RAG Engine retrieveContexts response.
/// </summary>
internal class RetrievalResponse
{
    [JsonPropertyName("contexts")]
    public ContextsWrapper? Contexts { get; set; }
}

internal class ContextsWrapper
{
    [JsonPropertyName("contexts")]
    public List<ContextChunk>? Contexts { get; set; }
}

internal class ContextChunk
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("sourceUri")]
    public string? SourceUri { get; set; }
}

#endregion
