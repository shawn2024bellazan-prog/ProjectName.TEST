# PMCR-O Reflection — Seed 0013 Read-Only Authority and PostgreSQL Candidate
Date: 2026-10-29
Canonical root: G:\ProjectName
I AM: PMCR-O Orchestrator coordinating bounded development-only evidence.
Development engineering verdict: PASS for implemented bounded tests.
Production governance verdict: HALT. Independent authenticated Checker PASS: false.

## ORCHESTRATE
Original user seed preserved verbatim at .pmcro/seeds/seed-0013-authority-postgres-development.txt.
Previous unexecuted NextSeed moved unchanged to .pmcro/seeds/staged-nextseed-after-seed-0012.txt.
Scope: G:\ProjectName only; no alteration to historical G:\workspace Trails, Seed 0033 HALT or Seed 0034 UNEXECUTED.
No worker admission, real keys, grants, models, promotion, published branch, or runtime activation.

## PLAN
Reuse existing ProjectName.Governance interfaces and signed ECDSA Checker verifier.
Add read-only evidence evaluator and host Windows preflight; never equate Windows login,
mock assertions or signed configuration to production authority.
Implement separate, unregistered ADO.NET PostgreSQL checkpoint candidate with immutable
composite issuer/tenant/actor/workflow/session/checkpoint scope, serializable transactions,
revision+digest CAS, key-version monotonicity, bounded retry and explicit revocation.
Test only against a disposable PostgreSQL container, not Aspire's persistent volume.

## MAKE
Added src/Governance/ProjectName.Governance/ReadOnlyAuthorityPreflight.cs.
Added tests/ProjectName.Governance.Tests/ReadOnlyAuthorityPreflightTests.cs (six tests).
Replaced static authority placeholders with read-only host discovery and signed-evidence
checks in eng/verify-authority-prerequisites.ps1; protected KMS configuration alone is
not marked as a live provider attestation.
Added src/BuildingBlocks/ProjectName.Persistence/PostgresTransactionalCheckpointCandidate.cs
and tests/ProjectName.Governance.Tests/PostgresTransactionalCheckpointCandidateTests.cs.
Added exact Npgsql 10.0.3 central version and test dependency to existing solution.
Added docs/AUTHORITY-AND-POSTGRES-CANDIDATE-2026-10-29.md.
No AppHost, active service DI, native MAF, model route or runtime worker changes.

## CHECK
Host SDK 10.0.403; locked-mode dotnet restore ProjectName.slnx PASS.
Release build PASS: 0 warnings, 0 errors.
Release tests with explicit isolated PostgreSQL connection: 102/102 PASS,
0 failed, 0 skipped; TRX: evidence/builds/checkpoint-candidate-20261029.trx.
Test fixture used independent Npgsql data sources; database row count verified as 2.
Concurrent compare-and-swap admits one winner and rejects stale revision; tenant/issuer/
actor/workflow scope checks, rollback, cancellation, revocation/read denial PASS.
A test-only friend-assembly hook terminated a real PostgreSQL backend after UPDATE
before COMMIT; fresh connection retained previous committed row, later commit succeeded.
Not a full physical power-loss or separate-host network-partition test.
Disposable postgres:18.3 container pmcro-candidate-pg-20261029-1404
used no named/persistent volume, stopped and auto-removed after testing.
Development Scalar/MCP/simulation/production denial:
  evidence/builds/dev-surfaces-20261029-140828.json — PASS.
Gateway protocol denial and throttling:
  evidence/builds/gateway-20261029-140839.json — PASS.
Aspire AppHost DevUI/MCP resource discovery:
  evidence/builds/aspire-mcp-20261029-140849.json — PASS; AppHost stopped.
Authority preflight:
  evidence/builds/authority-preflight-20261029-140700.json — HALT.
Windows identity observed only; issuer, signed Checker, grant, revocation,
live KMS provider and directory ACL not verified; zero local artifacts.
No authenticated independent Checker invoked and no fabricated PASS.
Interactive DevUI model route smoke intentionally not invoked because
Responses/Conversations agent routes remain unmounted and no model/worker
execution is permitted.
Source SHA-256 inventory and exact changed-file hashes are frozen in
a separate engineering receipt under evidence/checks.

## REFLECT
Development candidate implemented and tested but unactivated. The backing store
is not registered in Aspire or wired to EncryptedMafSessionStore.
Remaining: real host-owned trust config/KMS attestation and signed independent
Checker; stronger tenant DB RLS and real separate-host failover/physical crash tests.
ProductionAdmission=DENY; CheckerPass=false; ProductionActivationAllowed=false.
One next seed remains in .pmcro/next-seed.txt with state UNEXECUTED/UNAUTHORIZED.
