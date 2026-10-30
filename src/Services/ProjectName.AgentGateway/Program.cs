using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using OpenTelemetry.Metrics;
using ProjectName.AgentGateway;
using ProjectName.ControlPlane;
using ProjectName.Governance;

var builder = WebApplication.CreateBuilder(args);

// Development-only DI registration. Resolving a client is not a model call or
// worker activation. No runtime endpoint below exposes this client.
if (builder.Environment.IsDevelopment() &&
    !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("model-orchestrator")))
{
    builder.AddOllamaApiClient("model-orchestrator").AddKeyedChatClient("ollama");
}

// No identity provider, signed Checker receipt, or effective grant has been provisioned.
// The native agent protocols are dependencies, not active network endpoints.
builder.Services.AddSingleton<IAuthorityRepository, DenyAllAuthorityRepository>();
builder.Services.AddSingleton<IIndependentReceiptVerifier, DenyAllReceiptVerifier>();
builder.Services.AddSingleton<AdmissionGate>();
builder.Services.AddSingleton<IVerifiedIdentityResolver, DenyAllVerifiedIdentityResolver>();
builder.Services.AddSingleton<NativeProtocolAdmission>();
builder.Services.AddSingleton<PrivacySafeGatewayMetrics>();
// No exporter or sensitive request content is configured in this candidate.
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(PrivacySafeGatewayMetrics.MeterName));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.RequestServices.GetService<PrivacySafeGatewayMetrics>()?.RecordThrottled();
        return ValueTask.CompletedTask;
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unidentified",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// Native MAF agents are registered for DevUI discovery with a deterministic,
// no-network IChatClient. Real Ollama clients are never injected into these agents.
if (builder.Environment.IsDevelopment())
{
    foreach (DevelopmentAgentDescriptor role in DevelopmentAgentCatalog.Roles)
    {
        builder.AddAIAgent(role.Name,
            (_, name) => new ChatClientAgent(
                new SimulationOnlyChatClient(name),
                instructions: role.Instructions,
                name: name,
                description: "SIMULATION_ONLY: " + role.Responsibility));
    }
    builder.Services.AddOpenAIResponses();
    builder.Services.AddOpenAIConversations();
}

var app = builder.Build();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    // Restrict native simulation routes to loopback; no production mapping.
    var simulationRoutes = app.MapGroup("");
    simulationRoutes.AddEndpointFilter(async (context, next) =>
    {
        HttpContext http = context.HttpContext;
        string host = http.Request.Host.Host;
        if (http.Connection.RemoteIpAddress is not IPAddress ip || !IPAddress.IsLoopback(ip) ||
            (host != "localhost" && host != "127.0.0.1" && host != "[::1]") ||
            http.Request.Headers["Sec-Fetch-Site"] == "cross-site")
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        string origin = http.Request.Headers.Origin.ToString();
        if (!string.IsNullOrEmpty(origin) &&
            (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? parsed) || parsed.Host != host))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        return await next(context);
    });
    // Native OpenAI Responses/Conversations routes remain unmounted.
    // DevUI lists simulation-only role metadata; no agent execution endpoint.
    simulationRoutes.MapGet("/v1/agents/catalog", () => Results.Ok(new
    {
        mode = "SIMULATION_ONLY",
        executionAuthorized = false,
        modelInferenceEnabled = false,
        roles = DevelopmentAgentCatalog.Roles.Select(role => new
        {
            name = role.Name, identity = role.Identity, responsibility = role.Responsibility
        })
    }));
}

// Operational status, intentionally free of principal, session, model and Trail data.
app.MapGet("/healthz", () => Results.Ok(new { service = "ProjectName.AgentGateway", alive = true }));
app.MapGet("/v1/protocols", () => Results.Ok(new
{
    nativeAdapters = new[] { "OpenAI Responses", "OpenAI Conversations", "A2A HTTP+JSON", "AG-UI" },
    registeredAgents = 0,
    executableEndpoints = 0,
    activation = "DENIED",
    reason = "INDEPENDENT_CHECKER_AND_EFFECTIVE_GRANT_REQUIRED"
}));

// No MapOpenAIResponses, MapOpenAIConversations, MapA2AHttpJson, or MapAGUIServer
// at startup. This fallback prevents a protocol path from reaching any model or
// agent, even when a caller guesses a protocol-specific URL.
app.MapMethods("/{**unadmittedPath}", new[] { "GET", "HEAD", "POST", "PUT", "PATCH", "DELETE" },
    (PrivacySafeGatewayMetrics metrics) =>
    {
        metrics.RecordDenied();
        return Results.Json(new { error = "AGENT_ENDPOINT_NOT_ADMITTED" },
            statusCode: StatusCodes.Status403Forbidden);
    })
    .ExcludeFromDescription();

app.Run();

public partial class Program { }