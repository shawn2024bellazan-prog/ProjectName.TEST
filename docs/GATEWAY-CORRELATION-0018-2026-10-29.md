# Gateway Exit/Event Correlation — PMCR-O Cycle 0018

Date: 2026-10-29 (host local). Canonical root: G:\ProjectName.
Scope: isolated development-only process metadata and redacted Windows events.

## Correction discovered during testing
The original gateway watchdog used Get-NetTCPConnection -ErrorAction
SilentlyContinue as its only occupied-port probe. A controlled local
listener test found this was insufficient: the watchdog launched a child
despite the test port being occupied, and the child timed out. The failed
test was preserved at evidence/builds/
gateway-watchdog-20261029-173004-0535554-1c4e0eca.json.
The watchdog now uses an exclusive IPv4 loopback TCP bind/release
probe and fails closed on unexpected socket errors. It refuses an
occupied port before launching any child, and verifies port release
after each real gateway run. The corrected isolated reservation fixture
passed at evidence/builds/
gateway-watchdog-20261029-173539-5093413-0ccb0a06.json.
This is an engineering guard test, not a production security grant.

## Correlation contract
- eng/correlate-gateway-exits.ps1 reads at most 10 distinct named
  watchdog receipts from the bounded evidence/builds directory.
- It verifies schema, development-only flags, exact basename patterns,
  input sizes, SHA-256 digests and no asserted Checker authority.
- It classifies clean exits, timeouts, synthetic status-code exits,
  preexisting occupied-port refusal, and orphan-listener HALT.
- Redacted CoreCLR Event 1000/1023 data may provide historical context
  or temporal overlap; absent process identity binding it cannot prove
  the native crash belonged to the smoke child.
- Synthetic 0xC0000409 and synthetic 0xC0000005 exit codes are NOT
  real Windows crash events and cannot establish a common root cause.
- It does not collect process memory, dumps, traces, secrets, raw event
  messages, independent Checker credentials or grant material.

Evidence matrix: evidence/builds/
gateway-correlation-20261029-173604-7288625-4021d93c.json
10 rows covering historical fixture receipts, corrected port refusal,
synthetic 0xC0000005, and three live Development/Production-denied
gateway smokes. Corroborated child identity=false; rootCauseProven=false;
checkerPass=false; productionActivationAllowed=false.

The stored matrix validator passed, and invalid path traversal input
was rejected. The redacted event query from 17:25 returned no new
matching CoreCLR 1000/1023 events; this is not proof of no other
runtime failures.

## Development verification
- .NET 10 SDK 10.0.403 locked Release build: zero warnings/errors,
  102/102 governance tests PASS.
- Standalone DevUI, Scalar, MCP and gateway denial smoke PASS.
- Explicit disposable PostgreSQL 18.3 forced-RLS/CAS tests:
  102/102 PASS; tmpfs fixture stopped and auto-removed.
- Read-only authority preflight HALT: no verified issuer, signed
  Checker, effective grants, revocation, protected KMS or private ACL.
- Full Aspire AppHost and production workers were not started.
- AppHost, AgentGateway and persistence application source unchanged.
- Existing Agent Creator/C-suite drafts and historical G:\workspace
  Trails/Seed 0033 HALT/Seed 0034 UNEXECUTED preserved.

Status: development LOOP (correlation integrity strengthened; historical
runtime root cause remains unresolved), production HALT.
