using System.Diagnostics;

namespace InstaRAG.Api.Middleware;

/// <summary>
/// Middleware for structured request/response logging with timing information.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path;
        var method = context.Request.Method;

        _logger.LogInformation("→ {Method} {Path} started", method, requestPath);

        try
        {
            await _next(context);
            stopwatch.Stop();

            _logger.LogInformation("← {Method} {Path} completed with {StatusCode} in {ElapsedMs}ms",
                method, requestPath, context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "✕ {Method} {Path} failed after {ElapsedMs}ms",
                method, requestPath, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
