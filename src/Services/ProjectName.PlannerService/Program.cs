using ProjectName.Grpc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
var app = builder.Build();
app.MapGrpcService<DenyAllPhaseWorker>();
app.MapGet("/healthz", () => Results.Ok(new {
    service = "ProjectName.PlannerService", ready = false, admission = "deny-all"
}));
app.MapGet("/", () => "Use authenticated gRPC; no worker admission configured.");
app.Run();
