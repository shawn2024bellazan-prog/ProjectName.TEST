# ProjectName Aspire Development Integrations — 2026-10-29

Scope: G:\ProjectName only. Development infrastructure, no production activation.
This is a real tested AppHost integration, not an application database migration or authorized MAF inference route.

## Topology

- Aspire.AppHost.Sdk 13.6.4 supervises the existing Orchestration API, Agent Gateway, five role services, Web and MAF DevUI.
- In Development only, AppHost adds PostgreSQL server `postgres`, database `projectname`, Redis `redis`, and Ollama server `ollama`.
- Named persistent Docker volumes: `projectname-postgres-data`, `projectname-redis-data`, `projectname-ollama-data`.
- Orchestration API is given Aspire references to the PostgreSQL database and Redis, waits for healthy resources, and conditionally registers `Aspire.Npgsql` and `Aspire.StackExchange.Redis` clients only when their connection strings are present.
- Agent Gateway references the named `model-orchestrator` Ollama model and waits for the Ollama server. Its current native protocol routes still explicitly deny all unadmitted execution.
- In the 2026-10-29 development increment the AppHost explicitly declares one model resource (`model-orchestrator`, model `qwen3:4b`), downloading into its existing named Docker volume; it never invokes generation. The Windows Ollama application and its model directory remain separate and untouched.
- No database schema, EF migration, checkpoint backend switch, queue worker, model call, external credential, authority grant or production state is configured or invoked. The Agent Gateway now has a development-only keyed `IChatClient` DI registration (`ollama`) when Aspire injects the model connection; no route uses it.
- PostgreSQL authentication is managed by the Aspire hosting integration; never copy connection strings, generated passwords or dashboard login tokens into evidence.

## Pinned package versions

- Aspire.Hosting.PostgreSQL 13.6.4
- Aspire.Hosting.Redis 13.6.4
- CommunityToolkit.Aspire.Hosting.Ollama 13.6.1
- CommunityToolkit.Aspire.OllamaSharp 13.6.1 (Gateway development-only `IChatClient`)
- Aspire.Npgsql 13.6.4
- Aspire.StackExchange.Redis 13.6.4

Central NuGet versions and the AppHost/API package references were updated. New lock files were generated with a normal (unlocked) restore once; subsequent locked restore passed.

## Verified on Windows host

- .NET SDK 10.0.403, Docker Engine 29.8.2.
- Locked restore PASS; full Release build PASS, 0 warnings and 0 errors.
- Governance tests PASS, 31/31. Receipt: `evidence/builds/core-20261029-004228.json`.
- Aspire AppHost was stopped without `--force` or `--volumes` and relaunched in detached development mode.
- All 13 resource rows were observed as `Running/Healthy`: agent-gateway, checker, devui, maker, ollama, orchestration-api, orchestrator, planner, postgres, projectname, redis, reflector, web.
- HTTP responses: Orchestration API /healthz 200, Scalar /scalar/v1 200, MAF DevUI /devui/ 200, Ollama /api/tags 200, denied agent-gateway unknown path 403.
- PostgreSQL `pg_isready` returned `accepting connections`.
- Redis initially waited while the redis:8.6 image was downloaded, then logged `Ready to accept connections` and was reported `Healthy` by Aspire. A direct unconfigured redis-cli probe did not succeed, so no independent PONG claim is made.
- The three new Docker volumes were confirmed by `docker volume ls`.
- The local Windows Ollama installation reported 0.40.2 with no downloaded models listed. Aspire Ollama is an independent container and model store.

## Running and inspecting

Open PowerShell on the Windows device:

```powershell
Set-Location G:\ProjectName
dotnet aspire ps
dotnet aspire describe --apphost .\src\Hosting\ProjectName.AppHost\ProjectName.AppHost.csproj --format Table --non-interactive
```

After source changes, rebuild before starting. To stop without deleting volumes:

```powershell
dotnet aspire stop --apphost .\src\Hosting\ProjectName.AppHost\ProjectName.AppHost.csproj --non-interactive
```

To start:

```powershell
dotnet aspire run --detach --apphost .\src\Hosting\ProjectName.AppHost\ProjectName.AppHost.csproj --launch-profile development --non-interactive
```

Aspire dashboard login links and infrastructure ports may change after restart. Use `dotnet aspire ps` instead of relying on saved URLs.

## Nvidia GPU acceleration — verified 2026-10-29

- AppHost's existing Aspire-managed `ollama` resource now includes `.WithGPUSupport(OllamaGpuVendor.Nvidia)`. It retains the exact persistent volume `projectname-ollama-data`.
- Windows host: NVIDIA GeForce RTX 4070 Laptop GPU, 8188 MiB, driver 616.92.
- Docker runtime inventory exposed `nvidia`; the restarted Ollama container's device-request record contains `Capabilities: [[gpu]]` and Count=-1.
- `docker exec ... nvidia-smi` in the Ollama container listed the RTX 4070 with 8188 MiB.
- Ollama startup logs detected `library=CUDA`, compute capability 8.9, 8.0 GiB total and 6.9 GiB available.
- Restarted AppHost: all thirteen resources were `Running/Healthy`, including GPU-enabled Ollama, PostgreSQL and Redis.
- Prior GPU-only increment: Ollama `/api/tags` returned 200 and `ollama list` had no models. This establishes CUDA passthrough, not successful inference or a benchmark. The subsequent model-setup increment is tracked below.
- Gateway unknown route still returned 403. No model was downloaded, mapped into MAF, or authorized to execute.
- Historical GitHub Aspire config documents possible qwen3:8b + large-context VRAM/SIGSEGV problems on this GPU. Do not copy a 16K context or flash-attention override without fresh bounded reproducibility checks. The existing default context is left unchanged.
- Engineering receipt: `evidence/builds/core-20261029-005041.json`. See `evidence/builds/ollama-gpu-20261029.json` for bounded runtime observations.

## Explicit local model setup — 2026-10-29

- Development-only `model-orchestrator` uses `qwen3:4b` in the Aspire GPU-backed Ollama container, with default context and flash-attention settings unchanged. The resource starts an automatic pull; wait for `Running / Healthy` before claiming model ready.
- The AppHost explicitly sets `OLLAMA_NO_CLOUD=1`; a restarted Ollama container logged `Ollama cloud disabled: true`. This keeps remote inference and Ollama cloud web search disabled while preserving local model downloads.
- A version-pinned `CommunityToolkit.Aspire.OllamaSharp` registers a keyed `Microsoft.Extensions.AI.IChatClient` inside `ProjectName.AgentGateway` only when Aspire injects the development model connection string. No endpoint, worker or MAF workflow calls this client.
- Repeatable non-secret validation: `powershell.exe -NoProfile -File G:\ProjectName\eng\verify-aspire-dev.ps1`. It checks all fourteen resource health states, `/healthz`, Scalar, DevUI, Ollama `/api/tags` model presence, and gateway's HTTP 403 denial. It saves a sanitized receipt under `evidence\builds\aspire-dev-*.json` and never stores the raw Aspire environment containing OTLP credentials.
- Source and model downloads do not create registration, grant, activation, an independently signed Checker receipt, or authority to invoke a model. The model is a local development asset only.
- Final post-download verification: all **14/14** resources `Running / Healthy`; all six HTTP probes passed (five HTTP 200s and gateway denial HTTP 403); installed model `qwen3:4b` digest `359d7dd4bcdab3d86b87d73ac27966f4dbb9f5efdfcc75d34a8764a09474fae7`; `OLLAMA_NO_CLOUD=1`; no inference invoked. Sanitized receipt `evidence/builds/aspire-dev-20261029-011035.json` reports `ENGINEERING_PASS`, `checkerPass=false`, `productionEnabled=false`.
- The final Release verification after the local-only setting passed a locked restore, zero-warning build and all 31 governance tests: `evidence/builds/core-20261029-010243.json`.

## Native MAF agent previews in DevUI — 2026-10-29

- The Development-only Agent Gateway now registers 15 native MAF `ChatClientAgent` definitions: the five core `pmcro-*` roles plus the ten draft `csuite-*` executives from `marketplaces/csuite-marketplace/marketplace.json`. Aspire `AddDevUI(...).WithAgentService(gateway, agents: ...)` advertises exactly those identities; DevUI discovery is **not** PMCR-O registration or grant.
- Every preview agent uses `SimulationOnlyChatClient`, a deterministic, no-network `Microsoft.Extensions.AI.IChatClient`. It never reads prompt contents, invokes Ollama, calls tools, starts a role worker, or produces an authenticated Checker verdict. Chat output begins `SIMULATION_ONLY`. The real local-only `qwen3:4b` client remains DI-registered but is **not** used by these agents.
- Native `AddAIAgent` registrations, `MapOpenAIResponses` and `MapOpenAIConversations` apply only in Development. The simulation routes require loopback client and Host plus same-origin checks; cross-site browser requests are denied. Gateway rate limiting and the default 403 catchall remain active. OTEL GenAI message capture is disabled for this gateway.
- First five-role verification passed: `evidence/builds/devui-agents-20261029-012414.json`. Final full-catalog verification passed **15/15** DevUI discovery, exact parity with both DRAFT_UNREGISTERED marketplace manifests, PMCR-O and CEO Responses simulation, read-only deny-all catalog, and cross-site HTTP 403: `evidence/builds/devui-agents-20261029-012959.json`.
- Locked .NET 10 Release validation: 0 warnings/0 errors and **48/48** governance tests PASS (`evidence/builds/core-20261029-012632.json`). Final post-restart infrastructure check: 14/14 resources Healthy and all six HTTP checks PASS (`evidence/builds/aspire-dev-20261029-012743.json`).
- Repeatable validation: `powershell.exe -NoProfile -File G:\ProjectName\eng\verify-devui-agents.ps1`. Verification scripts retain only sanitized, simulation-specific status; raw Aspire environment and authenticated dashboard tokens are never written to evidence.
- Open the current DevUI URL through `dotnet aspire describe --format Table` after each restart; the port changes. Live Ollama reasoning, tool calling, workflow execution, host grants and executive activation are still prohibited pending verified authority.

## Governance boundary

- ProjectName.Governance and ProjectName.ControlPlane still use DenyAll* admission and verifier implementations.
- Runtime containers are not host identities, registrations, effective grants, activated workers, authenticated Checker verdicts, or permission to make model calls.
- No historical `G:\workspace` PMCR-O evidence was touched.
- This engineering validation is not independent Checker PASS and does not lift production HALT.

## Future bounded work

Before enabling a database-backed multi-host checkpoint path: implement tenant-isolated schema/migrations as an unactivated candidate; transactional/anti-rollback tests; protect keys with authenticated host issuer/KMS and ACL evidence; add independent Checker receipt. Before any Ollama **inference**: verify an approved exact model digest and resource policy, obtain effective authority and independent Checker PASS, and build a governance-gated call path. Merely pulling `qwen3:4b` or registering an inert `IChatClient` does not grant execution. Add RabbitMQ/Seq/management UIs only when required, with bound resources and an independent review.
