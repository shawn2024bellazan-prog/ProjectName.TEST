using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Development topology: role gRPC endpoints still DENY all execution.
var api = builder.AddProject<Projects.ProjectName_OrchestrationApi>("orchestration-api");
// Protocol gateway advertises adapters but exposes no admitted AIAgent or worker.
var gateway = builder.AddProject<Projects.ProjectName_AgentGateway>("agent-gateway")
    .WithReference(api)
    .WithEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "false");
builder.AddProject<Projects.ProjectName_OrchestratorService>("orchestrator").WithReference(api);
builder.AddProject<Projects.ProjectName_PlannerService>("planner").WithReference(api);
builder.AddProject<Projects.ProjectName_MakerService>("maker").WithReference(api);
builder.AddProject<Projects.ProjectName_CheckerService>("checker").WithReference(api);
builder.AddProject<Projects.ProjectName_ReflectorService>("reflector").WithReference(api);
builder.AddProject<Projects.ProjectName_Web>("web").WithReference(api);

// MAF DevUI aggregates development-only simulated AIAgents from the gateway.
// Their IChatClient never calls a model or tools and cannot admit a worker.
if (builder.Environment.IsDevelopment())
{
    // Development infrastructure only. Volumes are named and persistent; no
    // existing host database or Windows Ollama model directory is mounted.
    // Models are developer-only assets, not authorized worker admission.
    // No schema migration or model inference runs at startup.
    var postgres = builder.AddPostgres("postgres").WithDataVolume("projectname-postgres-data");
    var database = postgres.AddDatabase("projectname");
    var redis = builder.AddRedis("redis").WithDataVolume("projectname-redis-data");
    // RTX 4070 Laptop GPU is present and Docker exposes its NVIDIA runtime.
    // A small model is explicitly downloaded into the persistent development volume.
    // The gateway may resolve its keyed IChatClient but exposes no inference route.
    var ollama = builder.AddOllama("ollama")
        .WithGPUSupport(OllamaGpuVendor.Nvidia)
        .WithEnvironment("OLLAMA_NO_CLOUD", "1")
        .WithDataVolume("projectname-ollama-data");
    var developmentModel = ollama.AddModel("model-orchestrator", "qwen3:4b");

    api.WithReference(database).WaitFor(database);
    api.WithReference(redis).WaitFor(redis);
    gateway.WithReference(developmentModel).WaitFor(ollama);

#pragma warning disable ASPIREMCP001 // Aspire 13.6 MCP resource annotation remains experimental.
    api.WithMcpServer("/mcp");
#pragma warning restore ASPIREMCP001
    builder.AddDevUI("devui")
        .WithAgentService(gateway, agents:
        [
            new("pmcro-orchestrator", "SIMULATION ONLY — coordinates intent"),
            new("pmcro-planner", "SIMULATION ONLY — drafts bounded plans"),
            new("pmcro-maker", "SIMULATION ONLY — prepares candidates"),
            new("pmcro-checker", "SIMULATION ONLY — requires independent evidence"),
            new("pmcro-reflector", "SIMULATION ONLY — proposes an unexecuted NextSeed"),
            new("csuite-ceo", "DRAFT executive simulation — strategy"),
            new("csuite-cto", "DRAFT executive simulation — engineering"),
            new("csuite-coo", "DRAFT executive simulation — operations"),
            new("csuite-cfo", "DRAFT executive simulation — finance"),
            new("csuite-cpo", "DRAFT executive simulation — product"),
            new("csuite-cmo", "DRAFT executive simulation — marketing"),
            new("csuite-clo", "DRAFT executive simulation — legal"),
            new("csuite-chro", "DRAFT executive simulation — people"),
            new("csuite-cso", "DRAFT executive simulation — strategy-development"),
            new("csuite-cio", "DRAFT executive simulation — information")
        ])
        .WaitFor(gateway);
}

builder.Build().Run();
