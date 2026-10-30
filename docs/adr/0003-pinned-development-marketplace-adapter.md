# ADR-0003: Pinned Development marketplace admission preflight

Date: 2026-10-29
Status: Proposed Development candidate; no activation authority

## Context
I AM the Orchestrator. I resume ProjectName with an existing control-plane admission gate and an adjacent, separately versioned marketplace factory. A matching catalog name, JSON grant field, command message or role self-description is not host authority. The imported factory at commit `2b4608416f9e4cf8742788154c9428d2884df5e8` has no runtime capabilities and explicitly denies Development and Production admission.

## Decision
I keep ProjectName and the marketplace as independent repositories. I pin the marketplace's 0.3.0-dev catalog bytes to SHA-256 `2d2c63cdee1aeb189db33bf68784b0a1e0f9288a82cca7b8a60efa9170650d5e` with source manifest `62f87721c72f36be64971fbb1bc9c3e49f982373783209b314704e65efab1408`.

I introduce `PinnedDevelopmentMarketplaceAdapter` as a bounded, test-only reader and `DevelopmentMarketplaceAdmission` as a composition before the existing `AdmissionGate`. I reject:
- unpinned, oversized, malformed or duplicate-key catalogs;
- invalid schema, phase order or version;
- claims of registration, effective grant, Checker approval, trust root, activation or runtime availability;
- the primordial authoring constructor as runtime execution.

No catalog input can produce ALLOW. The pinned fixture and pin manifest are explicit test artifacts, not discovered runtime configuration. No live ingress, MCP, A2A, gRPC worker, Aspire topology or AGT policy engine is mounted.

## Consequences
I can demonstrate that a caller-controlled catalog cannot bypass an otherwise permissive test double. The real host control plane retains its independent grant, digest, expiry, revocation and Checker conditions. The design accepts a future host-attested policy/AGT adapter as a separate, governed increment.

## Alternatives
I reject copying source marketplace scripts directly into the product runtime or treating JSON fields as authentication. I also reject a new policy engine built from prompts.

## Evidence and promotion gate
I require a locked .NET 10 Release build, governance tests, exact source hashes and a separate Checker. All records are development evidence only. Production stays DENY; grantsIssued=0 and workersStarted=0. No promotion occurs without a genuine host-authenticated verdict.
