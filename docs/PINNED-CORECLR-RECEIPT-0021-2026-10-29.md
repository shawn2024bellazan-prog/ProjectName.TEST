# Cycle 0021 — explicit SHA-256-pinned redacted CoreCLR input

Date: 2026-10-29. Canonical root: G:\ProjectName. Local dev self-validation only.

## Runtime contract

`eng/verify-gateway-observability.ps1` no longer defaults to a hardcoded historical event receipt. Caller must explicitly supply both `-CoreClrReceiptName` and `-CoreClrReceiptSha256`. The filename is restricted to a redacted basename, not an arbitrary path. `eng/coreclr-receipts.development.json` is an allowlist of one SHA-256-pinned, locally observed redacted receipt and a bounded expiration. The manifest is NOT a signed grant, verifier or production authority.

Example opt-in synthetic-only verification (the allowlisted entry expires 2026-10-30T22:55:39Z):
```powershell
pwsh -NoProfile -File G:\ProjectName\eng\verify-gateway-observability.ps1 -CoreClrReceiptName coreclr-events-20261029-175538-8528699-65d45e4a.json -CoreClrReceiptSha256 62A723AD1223DCF48F90158B0B66AA1D3342372341BAE5E11712DBA5775D6F81
```
Use `-RunLiveGateway -LiveGatewayIterations 1` only for one isolated denial smoke; full Aspire/AppHost, models and workers are never started.

`Assert-PinnedCoreClrObservation` checks manifest schema, development DENY flags, exact basename uniqueness, supplied SHA, actual file SHA, observation schema, timestamp freshness and 24-hour maximum allowlist validity. A missing, substituted, stale, expired, unsigned/unallowlisted or future-dated observation HALTs before synthetic or gateway child creation. Synthetic fixture child exits remain uncorrelated with the historical Windows crash cause.

`Get-ExplicitFaultingPid` is a pure parser for the XML EventData field with exactly one attribute `Name="FaultingProcessId"`. It rejects unnamed Data, provider System.Execution ProcessID, duplicates, zero, malformed and out-of-range values. No raw event XML, message, process memory, dump, secrets or ETW payload is recorded. A PID/time metadata candidate does not authenticate process identity or prove the native 0xc0000005 root cause.

## Evidence from the bounded cycle

- Pre-edit read-only source drift: Cycle0020 snapshot had 710 files; 710/710 matched exactly. Git main remains unborn, no configured remote; no commit/push.
- Mock-only pinned SHA/allowlist/expiry and named EventData tests: 17 assertions PASS.
- Existing gateway contract 15 assertions PASS; two simultaneous occupied-port denials PASS.
- Pinned synthetic-by-default target 0 live PASS: `evidence/builds/gateway-observability-20261029-180901-5367841-66b7d26b.json`.
- Substituted digest HALT with zero watchdog children: `evidence/builds/gateway-observability-20261029-180926-0636769-7c4ae5ab.json`.
- Missing receipt HALT with zero watchdog children: `evidence/builds/gateway-observability-20261029-180931-6874603-625a66a2.json`.
- Explicit pinned isolated gateway 1/1 PASS: `evidence/builds/gateway-observability-20261029-180943-6443966-5bea66fe.json`.
- Locked SDK10.0.403 restore/Release build PASS 0 warnings/errors, 102/102 tests PASS: `evidence/builds/core-20261029-181011.json`.
- Standalone Development DevUI/Scalar/MCP and Production denial PASS: `evidence/builds/dev-surfaces-20261029-181035.json`.
- Disposable PostgreSQL18.3 test storage: 102/102 PASS, `evidence/builds/checkpoint-cycle-0021-20261029.trx`. `public.pmcro_checkpoint_candidate_v1` RLS true and FORCE true; dedicated loopback tmpfs container `pmcro-candidate-pg-0021` stopped and auto removed.
- Read-only authority preflight HALT: `evidence/builds/authority-preflight-20261029-181151.json`. Host Windows identity observed; issuer, separate signed Checker, effective grants, revocation, protected KMS and private ACL remain unverified.
- Exact source SHA-256 snapshot and one unexecuted NextSeed frozen in `.pmcro`; no historical G:\workspace state changed.

## Provenance recovery (proposal, not executed)

1. Keep exact SHA-256 inventory of untracked local source. Review approved exclusions and any secrets without exposing them.
2. Have an accountable principal identify exact target Git origin and authorization; verify remote and branch protection read-only.
3. Request a distinct host-authenticated Checker tied to exact artifacts and effective policy evidence.
4. Only after an explicit separately scoped grant, consider a new development branch, initial commit, and CI; do not rewrite older Trails or promote production.

Engineering verdict LOOP (checks pass, old crash causality unproven and source Git provenance absent). Production HALT; admission DENY.
