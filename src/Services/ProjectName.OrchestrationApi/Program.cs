using ProjectName.Contracts;
using ProjectName.ControlPlane;
using ProjectName.Governance;
using ProjectName.Runtime;
using Microsoft.Agents.AI.Workflows;
using System.Net;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Authentication.Negotiate;
using ProjectName.OrchestrationApi;

var builder = WebApplication.CreateBuilder(args);
// Aspire supplies these only for development AppHost runs. Direct API runs
// remain possible without database/cache infrastructure or automatic writes.
if (builder.Configuration.GetConnectionString("projectname") is not null)
    builder.AddNpgsqlDataSource("projectname");
if (builder.Configuration.GetConnectionString("redis") is not null)
    builder.AddRedisClient("redis");

builder.Services.AddSingleton<IAuthorityRepository, DenyAllAuthorityRepository>();
builder.Services.AddSingleton<IIndependentReceiptVerifier, DenyAllReceiptVerifier>();
builder.Services.AddSingleton<AdmissionGate>();
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "ProjectName Orchestration API";
        document.Info.Version = "v1";
        document.Info.Description = "PMCR-O orchestration control plane. Worker admission is DENY by default.";
        return Task.CompletedTask;
    });
});
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// An unauthorizing identity inspection handler, not an admission or grant provider.
if (builder.Environment.IsDevelopment() && OperatingSystem.IsWindows())
    builder.Services.AddAuthentication().AddNegotiate();

var app = builder.Build();

// Developer-facing documentation is deliberately unavailable outside Development.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().ExcludeFromDescription();
    app.MapMcp("/mcp").ExcludeFromDescription();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("ProjectName | PMCR-O API");
    });

    if (WindowsDevelopmentIdentityEndpoint.MayMap(app.Environment))
        app.MapGet(WindowsDevelopmentIdentityEndpoint.Route,
            (Delegate)(Func<HttpContext, Task<IResult>>)WindowsDevelopmentIdentityEndpoint.ProbeAsync)
            .ExcludeFromDescription();

    app.MapPost("/v1/workflows/simulate", async (HttpContext context, PmcroDryRunInput input) =>
    {
        if (context.Connection.RemoteIpAddress is not IPAddress remote || !IPAddress.IsLoopback(remote))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (string.IsNullOrWhiteSpace(input.TrailId) || input.TrailId.Length > 128 ||
            string.IsNullOrWhiteSpace(input.RawIntent) || input.RawIntent.Length > 4096)
            return Results.BadRequest(new { error = "INVALID_SIMULATION_INPUT" });

        // Pure MAF graph run: no model, worker, grants, or production side effects.
        var checkpoints = CheckpointManager.CreateInMemory();
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            PmcroDryRunWorkflow.Create(), input, checkpoints);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent output && output.Data is PmcroDryRunOutcome result)
                return Results.Ok(result);
        }
        return Results.Problem("Simulation yielded no reflection output.");
    }).WithName("SimulatePmcroWorkflow").WithTags("Development Simulation");
}

app.MapGet("/healthz", () => Results.Ok(new {
    service = "ProjectName.OrchestrationApi", alive = true
})).WithName("GetHealth").WithTags("Operations");

app.MapGet("/v1/capabilities", () => Results.Ok(new {
    registration = "not activated", workerAdmission = "deny-all"
})).WithName("GetCapabilities").WithTags("Governance");

app.MapPost("/v1/cycles", (CycleSubmission submission, AdmissionGate gate) =>
{
    AdmissionResult decision = gate.Evaluate(submission, null, DateTimeOffset.UtcNow);
    return Results.Json(decision, statusCode: StatusCodes.Status403Forbidden);
}).WithName("SubmitCycle").WithTags("Governance");

app.Run();

public partial class Program { }
