# Cycle 0020 — fail-closed gateway receipt contracts and bounded event metadata

Date: 2026-10-29
CanonicalRoot: G:\ProjectName
Authority: development-only. No independent authenticated Checker PASS.

## Implemented

- `eng/gateway-receipt-contract.ps1`: pure schema/required-field, bounded iteration, child PID, timestamp, development scope and deny-flag validation of watchdog and redacted CoreCLR receipts. Handles Windows PowerShell 5.1 JSON integer32/string timestamps and PowerShell 7 JSON integer64/date-object timestamps. Rejects unspecified or malformed timezone, reverse time, fraudulent PASS, mismatched event count and out-of-window events.
- `tests/fixtures/test-gateway-receipt-contract.ps1`: 15 in-memory assertions covering invalid schema, missing required fields, 9-iteration overrun, fraudulent PASS, reversed/malformed timestamps, event count, disputed event time, missing/mismatched/ambiguous PID and synthetic quarantine.
- `tests/fixtures/test-gateway-concurrent-port-guard.ps1`: exclusive localhost reservation at 127.0.0.1:58440, two simultaneous watchdog attempts. Both must exit 2 and report PORT_BUSY_BEFORE_START with zero gateway children; the test only owns and may stop its exact two watchdog PIDs. Port rebind after release is mandatory. PASS receipts: `evidence/builds/gateway-watchdog-20261029-175347-3588374-a3350346.json` and `evidence/builds/gateway-watchdog-20261029-175347-4448564-1307d78c.json`.
- `eng/correlate-gateway-exits.ps1` now imports shared strict validation and returns only deliberately noncausal labels: no bound event, temporal only, PID mismatch, ambiguous PID/time metadata, or PID/time metadata candidate NOT causal.
- `eng/inspect-coreclr-events.ps1` may extract faulting process ID only from an explicitly named FaultingProcessId EventData element, not from provider System.Execution.ProcessID, unlabeled payloads, untrusted event text or inferred timestamps. No raw message/XML, process memory, dumps or ETW payload is persisted; missing named field produces null.

## Verification observations

- Baseline read-only 703-file SHA-256 comparison before edits: 703/703 unchanged.
- Pure receipt contracts: 15 assertions PASS.
- Concurrent port guard: 2/2 HALT before gateway child, and rebind PASS.
- Synthetic-by-default developer target PASS, 0 live gateway runs: `evidence/builds/gateway-observability-20261029-175449-9991183-235de9cd.json`.
- Explicit one-run isolated gateway target PASS, 1/1: `evidence/builds/gateway-observability-20261029-175505-3251118-1d6b8707.json`.
- Redacted 24h Windows Application Error 1000 probe: 64 events examined, 0 explicitly named FaultingProcessId fields.
- Fresh redacted CoreCLR inspector PASS, two earlier matching events, neither a named faulting PID: `evidence/builds/coreclr-events-20261029-175538-8528699-65d45e4a.json`.
- Correlation against fresh redacted receipt PASS, 8 rows, rootCauseProven=false: `evidence/builds/gateway-correlation-20261029-175553-0185774-c592a294.json`. Validator PASS. A 0xC0000005 synthetic child exit does not prove the historical native CoreCLR fault's cause.
- Locked .NET SDK 10.0.403 Release restore/build PASS, 0 warnings/errors, 102/102 governance tests PASS: `evidence/builds/core-20261029-175608.json`.
- Standalone Development DevUI/Scalar/MCP and Production denied routes PASS: `evidence/builds/dev-surfaces-20261029-175625.json`.
- Disposable PostgreSQL 18.3 102/102 tests PASS: `evidence/builds/checkpoint-cycle-0020-20261029.trx`. `public.pmcro_checkpoint_candidate_v1` row security and FORCE RLS true. Only temporary `pmcro-candidate-pg-0020` loopback port 50640 and tmpfs, stopped and removed.
- Read-only authority preflight HALT: `evidence/builds/authority-preflight-20261029-175726.json`. Authenticated local Windows identity observed; signed issuer, independent signed Checker, effective grants, revocation, protected KMS and directory ACL still unverified.

## Provenance recovery checklist — NO GIT WRITE AUTHORIZED

- Git branch main remains unborn, `git rev-parse --verify HEAD` fails. `git remote` reports no remote. Files remain untracked. The frozen baseline from Cycle 0019 was verified before Cycle 0020 edits.
- Snapshot the new untracked source and freeze exact file SHA-256. Review the manifest under approved disclosure; do not automatically publish private or sensitive untracked content.
- Ask accountable principal to identify exact destination origin, verify permitted Git host account and branch protection, then have a distinct authenticated Checker bind receipts to exact candidate digest before committing anything.
- Later, in a separately scoped cycle, propose initial development branch and CI only after explicit authorization, without rewriting prior Trails, Seed 0033 HALT or Seed 0034 UNEXECUTED.

Engineering verdict LOOP (checks PASS, historical crash PID unavailable, local Git lacks provenance).
Production verdict HALT; worker admission DENY; independent Checker PASS false.
