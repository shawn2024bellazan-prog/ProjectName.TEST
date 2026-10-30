# Cycle 0022: Unexecuted CoreCLR observation allowlist rotation proposal

Scope: G:\ProjectName development only; local self-validation, not authenticated authority. Date: 2026-10-29.

## Design and execution boundary

The active `eng/coreclr-receipts.development.json` manifest was not changed. Its SHA-256 remains `B0C99A92CCE29CC30C6432351484B61CC773702CF9E1E01BA868E8AA22FB3D95`. The proposed file `eng/coreclr-receipts.rotation-0022.proposed.json` is a separately staged, non-effective candidate. Its previous revision 1 and proposed revision 2 plus exact previous manifest/receipt digests form local compare-and-swap anti-rollback preconditions. No authority or approval is inferred from these local fields.

A fresh bounded, read-only Windows Application event inspection produced `evidence/builds/coreclr-events-20261029-181618-6338113-6b4f6a42.json`, SHA-256 `4E15D1038865CDF9596054A2F393A34BCD754F13B677087B98FC40E9FED1DA5A`, 2 matching events. This captures no raw event messages, memory or dumps. The proposed expiry is 2026-10-30T23:06:18Z, within 24 hours of observation and later than the previous allowlist expiry. The first candidate was correctly rejected because its expiry regressed; the proposed file was adjusted before any activation.

The pure `eng/coreclr-rotation-contract.ps1` verifies version monotonicity, exact preceding manifest SHA, previous pinned source, candidate SHA, development DENY, strictly UNEXECUTED state, no approval record, and observation freshness against a supplied clock. The read-only `eng/verify-coreclr-rotation-proposal.ps1` requires explicit sanitized proposal name, new observation name, and SHA arguments; it verifies without writing to the active manifest. Promotion is deliberately not implemented; even a caller-supplied `approved=true` flag is refused. No host-authorized signer, grant, activation route or independent Checker exists.

## Local verification

- Baseline 716/716 SHA-256 source files from Cycle0021 matched before changes; Git main has no commits and no configured remote.
- `tests/fixtures/test-coreclr-rotation.ps1`: 21 assertions PASS, including repeat deterministic fixed-clock replay, stale/skip/rollback revision, competing head, manifest SHA substitution, duplicate source, digest mismatch, expiry/future/stale, forged approval and missing mandatory CLI inputs.
- Proposed read-only verification: `ROTATION_PROPOSAL=PASS_UNEXECUTED`, proposed SHA-256 `0BE47DB57AA18C721AE907AAEAA74FABBB5249AF3560B1091DD7B3D8CC67261F`.
- Locked SDK 10.0.403 Release restore/build 0 warnings/errors, 102/102 governance tests PASS: `evidence/builds/core-20261029-182105.json`.
- DevUI/Scalar/MCP development and production route denial PASS: `evidence/builds/dev-surfaces-20261029-182129.json`.
- Gateway synthetic-only pinned prior receipt PASS, LIVE_GATEWAY_RUNS=0: `evidence/builds/gateway-observability-20261029-182246-8324105-1fccf0f6.json`.
- Disposable PostgreSQL18.3 FORCE-RLS candidate: 102/102 PASS, `evidence/builds/checkpoint-cycle-0022-20261029.trx`; direct catalog `public|pmcro_checkpoint_candidate_v1|t|t`; disposable container removed.
- Read-only issuer/grant/revocation/KMS/Checker/ACL preflight HALT: `evidence/builds/authority-preflight-20261029-182244.json`.

No full Aspire, native model worker, production admission, encrypted checkpoint migration, keys, grants, Git commit/push, or historical G:\workspace Trail change. Original historical CoreCLR native crash causality remains unproven; redacted two-event metadata is not authenticated process identity or a Checker verdict.

EngineeringVerdict=LOOP; ProductionGovernanceVerdict=HALT. Exactly one UNEXECUTED NextSeed follows.
