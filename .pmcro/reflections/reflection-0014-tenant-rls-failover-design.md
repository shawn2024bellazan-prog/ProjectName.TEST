# PMCR-O Reflection — Seed 0014 Tenant RLS and Failover Protocol
Date: 2026-10-29
Canonical root: G:\ProjectName
I AM: PMCR-O Orchestrator coordinating a bounded development-only cycle.
Development verdict: LOOP (live gateway crash; Aspire AppHost intentionally stopped).
Production governance verdict: HALT. Independent signed Checker PASS: false.

## ORCHESTRATE
Fresh user intent was "continue", archived in .pmcro/seeds/seed-0014-development-continuation.txt.
Previous single UNEXECUTED NextSeed moved unchanged to .pmcro/seeds/staged-nextseed-after-seed-0013.txt.
Scope G:\ProjectName only. Historical G:\workspace Trails, Seed 0033 HALT and Seed 0034 UNEXECUTED not touched.
No real authority grant, model call, worker start, key provisioning, promotion or publication.

## PLAN
Enforce narrow tenant database principal and tenant RLS on isolated PostgreSQL checkpoint candidate;
prove no privileged connection bypass and fail-closed behavior when RLS is absent.
Design but do not execute a real physically separate-host partition/failover/fencing test.
Repeat locked .NET 10 release, production denial and read-only authority discovery.
Avoid launching full AppHost because the frozen seed prohibits starting role worker processes.

## MAKE
Updated src/BuildingBlocks/ProjectName.Persistence/PostgresTransactionalCheckpointCandidate.cs:
tenant role+RLS mandatory before initialize, commit, revoke, read; PrincipalDenied on mutation,
UnauthorizedAccessException on read. No DI/AppHost/native checkpoint wiring.
Updated tests/ProjectName.Governance.Tests/PostgresTransactionalCheckpointCandidateTests.cs:
two separate nonprivileged PostgreSQL logins, FORCE RLS policy and negative tests, unchanged
CAS/rollback/revoke/backend termination tests, and accidental disabled-RLS denial.
Added docs/CHECKPOINT-RLS-SEED-0014-2026-10-29.md with unexecuted multi-host protocol.
Updated eng/smoke-dev-surfaces.ps1 and eng/smoke-agent-gateway.ps1 to signal nonzero on failures;
gateway smoke also checks early child exit and caps each HTTP request at five seconds.
No actual production capability or new service topology introduced.

## CHECK
Evidence: evidence/checks/seed-0014-rls-development-check.txt and evidence/builds receipts.
.NET 10.0.403 locked restore, Release build (zero warnings/errors), tests 102/102 PASS.
Ephemeral PostgreSQL 18.3 role tests PASS; forced RLS verified, SQL cross-tenant read denied,
real backend terminated prior to COMMIT, later transaction resumed; isolated container removed.
Orchestration API Scalar/development MCP/simulation/denial smoke PASS.
Gateway smoke HALT: Development endpoints denied as expected, but Production child
encountered .NET fatal internal CLR error 0x80131506 in JSON serialization.
Independent rerun exited without a PASS receipt. Production smoke is NOT VERIFIED.
Aspire live check HALT: AppHost not running. No startup performed (would start role processes).
Authority HALT: no issuer assertion, signed independent Checker, grants/revocation,
protected KMS provider or private directory ACL verification. Maker cannot self-approve.
All exact changed source hashes frozen in evidence/checks receipt.

## REFLECT
Engineering RLS protections are better validated than Seed 0013, but no signed approval,
real host attestation, physical failover proof, live Aspire resource health or reproducible
gateway Production denial was established this cycle.
Candidate is UNACTIVATED; ProductionAdmission=DENY; CheckerPass=false;
ProductionActivationAllowed=false. One NextSeed staged below, NOT EXECUTED.
