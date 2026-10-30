# ProjectName enterprise architecture

Canonical product root: `G:\ProjectName`.
Marketplace factory and frozen candidate at `G:\workspace\marketplaces` are references, not writable dependencies.

## Runtime

- `ProjectName.Runtime`: official Microsoft Agent Framework `AIAgent`, `AgentResponse`, `AgentWorkflowBuilder`; models are injected, with no ambient credentials.
- `ProjectName.OrchestrationApi`: protected public ingress (development deny-all).
- `ProjectName.*Service`: independently hosted gRPC phase endpoints; no admission so all `Execute` RPCs return `PermissionDenied`.
- `ProjectName.AppHost`: Aspire 13.6.4 development topology.
- `ProjectName.Cli` (`pmcro`), `ProjectName.Sdk.DotNet`, Blazor web and .NET MAUI: client surfaces.
- Marketplace catalogs describe capabilities and C-suite agents without registering or activating them.

## Authority boundaries

`DISCOVERY != REGISTRATION != GRANT != ACTIVATION != EXECUTION`.

The control plane denies a request unless its target has a registration, unrevoked digest, matching effective grant, unexpired authorization, and an independently verified Checker receipt. The live/default repository and receipt verifier deny everything. Test doubles are not production authority.

A separate Checker process is not by itself an independently authorized Checker. PMCR-O controls every transition: ORCHESTRATE -> PLAN -> MAKE -> CHECK -> REFLECT. A tool result or inbound message cannot advance a phase. Executive roundtable consensus is advisory only.

## Current development paths

- `src/BuildingBlocks`: Domain, Contracts, Application, Infrastructure, Persistence, SharedKernel projects (many still skeletons).
- `src/Governance`: Governance, ControlPlane, Trails and Evidence projects; admission evaluator is implemented.
- `src/Runtime`: MAF agent runner and sequential workflow factory.
- `src/Services`: API and five gRPC role service projects.
- `src/Protocols`: gRPC protobuf service contract; A2A/MCP/AG-UI directories are planned integration boundaries.
- `apps`: CLI, Blazor Web, MAUI shells.
- `sdk`: .NET API client; Python and TypeScript not implemented.
- `marketplaces`: draft PMCR-O and C-suite catalogs; other marketplace categories planned.
- `templates`, `mcp`, `.agents`: future portable templates/integrations.

## Production work remaining

Cryptographically authenticated grants and Checker verification; certificate-bound service identity; persisted Trails and append-only hash chaining; secure queue-backed workers; per-tenant sessions and memory; secret manager; model provider adapters; real marketplace loading and signed packages; C-suite roundtable execution; MCP/A2A/AG-UI conformance; complete CLI/MAUI/web feature flows; supply-chain, load, chaos and end-to-end checks; signed independent review.

The development fallback is always HALT. No production activation, registration, grant, admission, or promotion is asserted.
