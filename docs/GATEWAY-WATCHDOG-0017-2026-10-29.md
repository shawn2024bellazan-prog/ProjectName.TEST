# Gateway Watchdog — Development Cycle 0017

Date: 2026-10-29. Canonical root: G:\ProjectName.
Purpose: preserve a parent-owned, bounded result when a standalone
AgentGateway smoke child exits abruptly without its own JSON receipt.

## Implementation
- eng/watch-agent-gateway.ps1 is a separate PowerShell parent with
  ValidateSet mode and finite caps: 1–3 iterations and 2–90s per child.
- It runs only the named G:\ProjectName gateway smoke or explicit synthetic
  fixtures, not arbitrary tools, workers, models or full Aspire AppHost.
- Its receipt records the child PID, signed and hexadecimal exit code,
  elapsed milliseconds, timeout/failure status, bounded stdout/stderr paths
  and whether loopback port 58440 was released.
- It stops on the first failure or occupied gateway test port and never
  kills a process it did not start. A timed-out child is stopped by exact PID.
- Receipts use randomized filenames and FileMode.CreateNew. Child logs are
  development-only synthetic traffic and contain no request credentials.
- It does not collect process memory, dumps, secrets or raw Windows event text.

## Test vectors
- FixturePass (2 iterations): PASS, both child exit 0.
  evidence/builds/gateway-watchdog-20261029-151313-5182805-731e4e9e.json
- FixtureFail (3-iteration cap): LOOP after one child exit 23.
  evidence/builds/gateway-watchdog-20261029-151321-6056899-3d4d9f17.json
- FixtureAbruptExit: LOOP after synthetic exit -1073740791 (0xC0000409).
  evidence/builds/gateway-watchdog-20261029-151322-3269995-4d091183.json
- FixtureTimeout (2 seconds): LOOP, child stopped by exact PID.
  evidence/builds/gateway-watchdog-20261029-151323-0306968-15ec13f0.json
- Three real isolated gateway smokes: PASS, 0 exit codes, port released
  each time. evidence/builds/gateway-watchdog-20261029-151343-4493450-d4bcc154.json

## Redacted CoreCLR event inspector checks
- Historical match query: 2 events, evidence/builds/
  coreclr-events-20261029-151429-6185131-df830153.json.
- Later empty query: 0 matching events, evidence/builds/
  coreclr-events-20261029-151430-0497201-985a7cd7.json.
- Invalid 48h query: rejected before event access.
- Simulated event-log permission failure: rejected, no mock authority
  receipt; tests/fixtures/inspect-coreclr-unavailable.ps1.
- A later narrow query found no new matching CoreCLR 1000/1023 events.

## Remaining constraints
These results prove the watchdog's reporting behavior, not a native
runtime root cause. The earlier CoreCLR 0xc0000005 and later unobserved
0xC0000409 runner exit remain separate until corroborated.
No production authority, signed independent Checker, KMS or private ACL
was established. Production worker admission remains DENY.
