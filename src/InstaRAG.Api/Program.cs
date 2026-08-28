using InstaRAG.Api.Configuration;
using InstaRAG.Api.Middleware;
using InstaRAG.Api.Services;
using Polly;
using Polly.Extensions.Http;
using Microsoft.AspNetCore.Authentication;
using System.Text.Encodings.Web;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ─── Simple No‑Auth scheme (required because we call Forbid() on webhook verification failures) ───
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "NoAuth";
    options.DefaultChallengeScheme = "NoAuth";
    options.DefaultForbidScheme = "NoAuth";
})
.AddScheme<AuthenticationSchemeOptions, NoAuthHandler>("NoAuth", _ => { });

// ─── Configuration Binding ───────────────────────────────────────────
builder.Services.Configure<MetaSettings>(
    builder.Configuration.GetSection(MetaSettings.SectionName));
builder.Services.Configure<GoogleCloudSettings>(
    builder.Configuration.GetSection(GoogleCloudSettings.SectionName));
builder.Services.Configure<RateLimitSettings>(
    builder.Configuration.GetSection(RateLimitSettings.SectionName));

// ─── HTTP Clients ────────────────────────────────────────────────────
// Instagram Graph API client with retry policy
builder.Services.AddHttpClient("Instagram", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddPolicyHandler(GetRetryPolicy());

// Vertex AI RAG Engine client
builder.Services.AddHttpClient("VertexAI", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(60);
});

// ─── Services ────────────────────────────────────────────────────────
builder.Services.AddSingleton<IRateLimiterService, RateLimiterService>();
builder.Services.AddScoped<IRagService, RagService>();
builder.Services.AddScoped<IInstagramService, InstagramService>();

// ─── Controllers & Swagger ───────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "InstaRAG API",
        Version = "v1",
        Description = "Instagram DM RAG-powered product assistant API. " +
                      "Automatically answers product questions via Instagram Direct Messages " +
                      "using Google Vertex AI RAG Engine and Gemini 2.5 Flash."
    });
});

// ─── CORS ────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ─── Logging ─────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// ─── Middleware Pipeline ─────────────────────────────────────────────
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "InstaRAG API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors();
app.MapControllers();

// ─── Startup Banner ──────────────────────────────────────────────────
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("═══════════════════════════════════════════════════");
logger.LogInformation("  InstaRAG API v1.0.0 — Instagram DM RAG Assistant");
logger.LogInformation("  Environment: {Env}", app.Environment.EnvironmentName);
logger.LogInformation("  Swagger UI:  /swagger");
logger.LogInformation("  Health:      /api/health");
logger.LogInformation("  Webhook:     /api/webhook/instagram");
logger.LogInformation("═══════════════════════════════════════════════════");

app.Run();

// ─── Polly Retry Policy ─────────────────────────────────────────────
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

// Make Program class accessible for integration tests
public partial class Program { }

// Simple authentication handler that always succeeds
public class NoAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public NoAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
                         ILoggerFactory logger,
                         UrlEncoder encoder,
                         ISystemClock clock)
        : base(options, logger, encoder, clock) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(), Scheme.Name)));
}
