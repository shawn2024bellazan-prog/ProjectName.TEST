# ProjectName developer surfaces

This document covers local development only. It does not register, grant, activate, or execute an AI agent.

## Aspire and MCP

Aspire 13.6.4 is installed as a local dotnet tool (see dotnet-tools.json).

From G:\ProjectName:

```powershell
dotnet tool restore
dotnet aspire start --apphost src/Hosting/ProjectName.AppHost/ProjectName.AppHost.csproj --launch-profile development
dotnet aspire describe --format Json
dotnet aspire mcp tools --format Json
dotnet aspire stop --apphost src/Hosting/ProjectName.AppHost/ProjectName.AppHost.csproj
```

VS Code .vscode/mcp.json runs `dotnet aspire agent mcp --non-interactive` from the workspace directory. Only use MCP commands that are admitted by PMCR-O policy. Starting an Aspire AppHost never admits a worker task.

The `orchestration-api` AppHost resource is annotated `WithMcpServer("/mcp")` only in Development. The ASP.NET API implements this endpoint with the official ModelContextProtocol.AspNetCore 2.3.0 SDK. `GetAdmissionStatus` is read-only and reports deny-all. The endpoint is not present in Production.

## Web developer surfaces

In Development, the API exposes:

- `/healthz`: liveness (also present in Production).
- `/v1/capabilities`: non-activated capability projection (also present in Production).
- `/openapi/v1.json`: ASP.NET Core OpenAPI document.
- `/scalar/v1`: Scalar interactive API reference.
- `/mcp`: MCP streamable HTTP, read-only inspection tool.
- `POST /v1/workflows/simulate`: loopback-only MAF five-phase dry run with typed Checker evidence and HALT reflection (in-memory checkpoints, never a production execution).

`POST /v1/cycles` remains denied with HTTP 403 until authenticated admission and independently verified Checker evidence exist. Scalar must not be treated as permission to run cycles.

The AppHost also publishes a Microsoft Agent Framework DevUI resource in Development. The DevUI aggregator intentionally declares **zero agents**. The Microsoft.DevUI and Aspire.Hosting.AgentFramework.DevUI prerelease packages are development samples, not a production authentication boundary. Real `WithAgentService(...)` entries require registered MAF `AddAIAgent` agents, OpenAI Responses/Conversations backends, authenticated caller isolation, trusted model/tool grants, and independent Checker PASS.

MAF native `AIAgent` and workflow infrastructure exist in ProjectName.Runtime. The custom privacy-projected five-phase workflow has been executed in tests, with JSON checkpoint files and resume verified. These development-only deterministic executors do not invoke agents, and the evidence projection cannot be treated as an independent Checker verdict. Full protocol hosting (OpenAI Responses, A2A, AG-UI), encrypted tenant-isolated sessions, authenticated telemetry, and a real Checker implementation remain separate bounded increments. Do not use the generic sequential factory for a production Checker: it passes shared conversation history.

## Reproducible verification

```powershell
powershell -ExecutionPolicy Bypass -File eng/restore-locks.ps1
powershell -ExecutionPolicy Bypass -File eng/verify-core.ps1
powershell -ExecutionPolicy Bypass -File eng/smoke-dev-surfaces.ps1
powershell -ExecutionPolicy Bypass -File eng/smoke-aspire-mcp.ps1
powershell -ExecutionPolicy Bypass -File eng/snapshot-sources.ps1
```

The first smoke suite verifies the developer OpenAPI, Scalar and MCP endpoints, the native simulation HALT, HTTP 403 for unadmitted cycles, and Production-mode 404 for dev routes. The Aspire smoke suite starts then stops only this AppHost, checks DevUI discovery, and lists resource MCP tools. Neither smoke script invokes an agent or grants a capability.

Dependency pins include MessagePack 3.1.12 to avoid GHSA-qhrr-8q5h-9q3h from a DevUI transitive dependency. Exact validation receipts live in evidence/builds.
