# ProjectName

Enterprise-oriented .NET 10 LTS / Microsoft Agent Framework development platform with PMCR-O governance, five gRPC role services, Aspire hosting, .NET MAUI/Blazor clients, CLI, SDK and draft marketplaces.

Canonical generated product root: `G:\ProjectName`. Adjacent `G:\workspace` marketplace factory, frozen candidates, credentials and historic Trails were not modified.

## Build and test

From a PowerShell terminal:

```powershell
Set-Location G:\ProjectName
dotnet --version
dotnet restore ProjectName.slnx --locked-mode
dotnet build ProjectName.slnx -c Release --no-restore
dotnet test tests/ProjectName.Governance.Tests -c Release --no-build
dotnet run --project apps/ProjectName.Cli -c Release -- status
```

Alternatively run `eng/verify-core.ps1`; receipts are written to `evidence/builds/`. MAUI is a separate workload-specific project at `apps/ProjectName.Maui`, not included in the core solution.

## Run local Aspire stack

The Development AppHost runs PostgreSQL, Redis, GPU-backed Ollama (`qwen3:4b`), the API and gateway, five deny-all role services, Blazor, Scalar and MAF DevUI. DevUI lists **15 native simulation-only MAF agents**: five PMCR-O roles and ten draft C-suite executives. Their chat replies are deterministic and do **not** invoke Ollama or tools; model download takes time on first launch, and no real role inference is enabled.

```powershell
Set-Location G:\ProjectName
dotnet aspire run --detach --apphost .\src\Hosting\ProjectName.AppHost\ProjectName.AppHost.csproj --launch-profile development --non-interactive
dotnet aspire ps
dotnet aspire describe --apphost .\src\Hosting\ProjectName.AppHost\ProjectName.AppHost.csproj --format Table --non-interactive
powershell.exe -NoProfile -File .\eng\verify-aspire-dev.ps1
powershell.exe -NoProfile -File .\eng\verify-devui-agents.ps1
```

To stop without deleting persistent PostgreSQL, Redis or Ollama volumes:

```powershell
dotnet aspire stop --apphost .\src\Hosting\ProjectName.AppHost\ProjectName.AppHost.csproj --non-interactive
```

The Aspire dashboard URL and ports are dynamic; obtain them from `dotnet aspire ps`. See `docs/ASPIRE-LOCAL-INFRASTRUCTURE.md` for boundaries and evidence. Never share the authenticated dashboard login token or raw `aspire describe --format Json` output.

## Safety and scope

All live worker gRPC execution is denied by default. No agent identity, grant, Checker signature, marketplace catalog, or message can authorize execution simply by being discovered. The configured API uses deny-all authority, while test doubles are for tests only.

See `docs/architecture.md` and `docs/implementation-status.md` for exact boundaries. `.pmcro/next-seed.txt` is **unexecuted**.
