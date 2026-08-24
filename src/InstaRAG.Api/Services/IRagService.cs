namespace InstaRAG.Api.Services;

/// <summary>
/// RAG (Retrieval-Augmented Generation) service interface.
/// Handles context retrieval from Vertex AI RAG Engine and answer generation via Gemini.
/// </summary>
public interface IRagService
{
    /// <summary>
    /// Retrieves relevant context from the RAG corpus for the given query.
    /// </summary>
    Task<string> RetrieveContextAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a natural language answer using Gemini 2.5 Flash,
    /// combining the system prompt, retrieved context, and user query.
    /// </summary>
    Task<string> GenerateAnswerAsync(string query, string context, CancellationToken cancellationToken = default);

    /// <summary>
    /// End-to-end: retrieves context and generates an answer for the user's question.
    /// </summary>
    Task<string> AnswerQuestionAsync(string query, CancellationToken cancellationToken = default);
}
