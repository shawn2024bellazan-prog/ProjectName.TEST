# ProjectName MCP port: approval proposal (2026-10-29)

Status: DRAFT_UNAPPROVED / NOT_REGISTERED / NOT_ACTIVE
Canonical root: G:\ProjectName
Source reviewed: ShawnDelaineBellazanLoop/PMCR-O-Marketplace, main tree a25f7f014bd899a5e3554b6ddbe71c430ef575b4
Target: .NET 10 development-only isolated MCP services. Preserve existing AppHost roles, Postgres, Redis, GPU Ollama, DevUI, Scalar.
This document is a proposal, NOT tools/allowlist.json, a host grant, activation evidence, or independent Checker approval.

## Proposed allowlist policy (not effective)

Global: default-deny; exact server ID + exact tool name + principal + scope + digest + expiry and revocation check required. No wildcard tool grants. Any side-effecting tool requires authenticated human approval at its execution boundary; a comment, prompt, or MCP tool description is not enforcement.

- mcp-filesystem: candidate INSPECT_ONLY tools desktop-commander__read_file, desktop-commander__list_directory, desktop-commander__get_file_info, desktop-commander__start_search, GrepContent, ListSkills, LoadSkill. Bound all reads to an explicitly approved non-secret path subtree. desktop-commander__write_file remains DENY.
- mcp-playwright: candidate INSPECT_ONLY tools GetSessionStatus, GetPageTitle, GetPageContent, GetPageSnapshot. Navigation, screenshots to disk, click, fill, submit and Execute* remain DENY pending origin allowlist and per-action authorization. Browser must not inherit operator cookies or secret-bearing sessions.
- mcp-terminal: candidate INSPECT_ONLY tools desktop-commander__list_sessions and Which. GetEnvironment, ExecuteRunCommand, ExecuteRunScript, ExecuteKillProcess, process-kill, and any other direct execution tools remain DENY. Request-only tool names do not imply authority to use Execute* counterparts.
- orchestration-api: existing get_admission_status is read-only and was discovered by Aspire MCP. No wider runtime rights inferred.
- postgres/redis/ollama: Aspire resources and health dependencies are not automatically MCP tool grants. WithPostgresMcp is a separate experimental capability and remains disabled pending scoped, read-only database credential and review.

## Security and compatibility findings

1. Marketplace TerminalTools.cs decorates ExecuteRunCommand, ExecuteRunScript, ExecuteKillProcess with McpServerTool. Do not assume method names such as 'Orchestrator-only' enforce caller identity.
2. Marketplace GetEnvironment accepts arbitrary requested environment variable names. Do not expose it.
3. Marketplace FilesystemTools contains write_file. Sandbox paths alone are not an authorization policy.
4. Marketplace Playwright project uses Patchright and browser-actuation tools. Prefer reviewed browser automation with explicit origin and execution controls.
5. Marketplace projects target net11.0 and reference a different ServiceDefaults layout. Port selectively to net10.0 and current hosting abstractions, not by copying the old AppHost.
6. Aspire resource MCP annotations can proxy tools into agent client lists. Do not add WithMcpServer for unapproved candidates, and do not enable WithPostgresMcp merely because a database exists.

## Admission prerequisites and acceptance tests

Required before mounting any candidate in the AppHost:
- Host-authenticated principal, effective grant, revocation policy, exact tool allowlist (tools/allowlist.json) and independent Checker evidence bound to source digests.
- HIL enforcement in server code for writes, browser actuation and command execution, not only orchestration prompts.
- Loopback-only or authenticated HTTP exposure in Development. Zero production endpoints.
- Build locked .NET 10 Release; run tests for DENY-by-default and malicious direct MCP calls; assert no wildcard exposures.
- Test Aspire discovery, three candidate health statuses, DevUI 15 simulation agents, Postgres/Redis/Ollama health and unchanged production worker DENY.
- Preserve immutable historical Trails and leave the NextSeed unexecuted.

Observed preflight: tools/allowlist.json absent; MCP approval and grants absent; three candidate directories absent. No candidate tools activated in this phase.
