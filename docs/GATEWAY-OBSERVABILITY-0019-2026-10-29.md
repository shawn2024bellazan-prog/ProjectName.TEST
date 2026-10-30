# Cycle 0019: isolated gateway observability target and Git provenance plan

Date: 2026-10-29
Scope: G:\ProjectName only. This is development self-verification, not an independent Checker receipt.

## New bounded verification

- `eng/verify-gateway-observability.ps1` defaults to synthetic-only tests: two passing child exits; distinct failed, abrupt (synthetic 0xC0000409), synthetic access-violation (synthetic 0xC0000005), and timeout children; occupied-port refusal before child start; no live gateway child.
- Opt-in `-RunLiveGateway -LiveGatewayIterations 1` performs one isolated gateway denial smoke after the fixture suite. Hard cap: three live iterations. Neither mode starts Aspire AppHost, role workers, local models, native A2A or AG-UI routes.
- Uses exclusive loopback bind/release on 127.0.0.1:58440; an occupied port must halt before child start. No reliance on Get-NetTCPConnection failure suppression and no killing other listeners. On its own child timeout, the watchdog terminates only its captured PID.
- `eng/correlate-gateway-exits.ps1 -SelfTestSchemaRejection` checks the same schema validator with an in-memory malformed document, without creating forged evidence files. Traversal and duplicate receipt inputs are rejected. Matrix validator accepts 0–3 explicitly specified live runs and validates SHA-256 input digests and no authority/causality claims.
- Source script SHA-256 and per-input receipt SHA-256 are bound into unique parent-owned `gateway-observability` JSON receipts via FileMode.CreateNew. Historical redacted Windows CoreCLR receipt is used as the fixed, bounded baseline; it is not PID-bound to current children.

## Verified checks

- Default synthetic-only: PASS; 0 live runs, `evidence/builds/gateway-observability-20261029-174242-0057689-929f1480.json`.
- Explicit isolated live smoke: PASS; 1/1 gateway run, `evidence/builds/gateway-observability-20261029-174259-8309543-81447772.json`.
- Existing frozen Cycle 0018 ten-row matrix revalidated successfully with expected 3 live exits.
- SDK 10.0.403, locked restore, .NET 10 Release build: PASS, 0 warnings/errors. Governance tests 102/102 PASS, `evidence/builds/core-20261029-174320.json`.
- Standalone DevUI OpenAPI/Scalar/MCP and Production denial smoke PASS, `evidence/builds/dev-surfaces-20261029-174338.json`. Full Aspire AppHost was not started.
- Disposable PostgreSQL 18.3 with loopback-only random port and tmpfs: 102/102 PASS, `evidence/builds/checkpoint-cycle-0019-20261029.trx`. `public.pmcro_checkpoint_candidate_v1` row security true / FORCE true. Fixture `pmcro-candidate-pg-0019` stopped and removed; no persistent data or real checkpoints changed.
- Authority read-only preflight: HALT, `evidence/builds/authority-preflight-20261029-174447.json`. Local authenticated Windows SID observed, but signed issuer, independent signed Checker, grants, revocation, protected KMS and private directory ACL not verified.
- Historical CoreCLR Event 1000 0xc0000005 remains a distinct real observation with unproven cause. A synthetic child returning 0xC0000005 is not evidence of a native crash root cause. No dump, process memory, ETW payload, secrets or private configs collected.

## Git provenance: read-only findings and recovery proposal

- Current branch name: main. `git rev-parse --verify HEAD` fails: unborn HEAD.
- `git status --short` reports top-level directories and files as untracked. No remote name was returned by `git remote`.
- Source SHA-256 snapshots are local integrity observations, not Git history or authenticated provenance. No branch, commit, merge, push, remote registration or publish occurred.

Proposed lawful recovery, not executed:

1. Preserve a timestamped SHA-256 inventory of this development tree and existing immutable evidence; independently review untracked content, large files and secrets safely without printing secrets.
2. Have the accountable user choose and authorize the exact reachable repository and principal. Verify Git remote, protections and credential scope read-only.
3. Obtain an independent authenticated Checker receipt covering exact candidate digests, license/source provenance, tests, deny-all policies and history preservation. Separate Checker must not be the Maker.
4. Only after a new scoped grant, prepare the initial commit on a development branch with approved exclusions; never rewrite historical G:\workspace trails. Validate CI before proposing promotion.
5. Keep production DENY until issuer, effective grants, revocation, protected KMS provider, private directory ACL and independently signed digest-binding are verified.

Engineering status: LOOP (observability checks pass; native crash cause and repository provenance remain unresolved).
Production governance: HALT. Checker PASS: false. One unexecuted NextSeed is staged.
