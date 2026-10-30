# Cycle 0023: fixed-clock mock rotation replay

Canonical root G:\ProjectName. Test-only development, not signed or authoritative.

Before edits, 724/724 source hashes in `evidence/builds/sha256-20261029-182403.csv` matched. The active allowlist digest is B0C99A92CCE29CC30C6432351484B61CC773702CF9E1E01BA868E8AA22FB3D95; the UNEXECUTED proposed revision-2 manifest digest is 0BE47DB57AA18C721AE907AAEAA74FABBB5249AF3560B1091DD7B3D8CC67261F; the frozen new redacted event receipt digest is 4E15D1038865CDF9596054A2F393A34BCD754F13B677087B98FC40E9FED1DA5A; Cycle0022 evidence receipt SHA is 852FBE633528E1EE22D53ECA0AB0AB50768C7C105670CC0BEA0435EE71115D5F. No Git commits or remote existed.

`eng/coreclr-rotation-mock-replay.ps1` contains an in-memory deterministic mock CAS state machine, explicit test-fixture identities and revocation/expiry checks. It neither verifies real cryptographic signatures nor touches files, grants, storage, services, or active allowlist. `Assert-NoHostPromotionRoute` always refuses any host activation. A caller-provided fixture seal is not identity authentication. This is a logic regression test, not a trusted signer integration or multi-host database transaction.

`tests/fixtures/test-coreclr-rotation-replay.ps1` passes 25 assertions: two prepared candidates competing on revision-1; winner and stale loser; approval replay; reused receipt digest on later revision; interruption before in-memory publication and safe retry; spoofed issuer/Checker/actor/mock seal; unknown and revoked mock grant; revocation after prepare; expired or future-dated mock approval; bound digest mismatch; rollback; stale/changed head; duplicate publication; and unconditional real promotion DENY. Fixed clock 2026-10-29T23:30:00Z. All mock claims explicitly labeled not authoritative.

Application checks were rerun:
- SDK 10.0.403 locked Release restore/build, 0 warnings/errors; governance 102/102 PASS: `evidence/builds/core-20261029-182846.json`.
- DevUI/Scalar/MCP development, production routes denied: `evidence/builds/dev-surfaces-20261029-182901.json`, PASS.
- Gateway synthetic fixtures only, 0 live gateway runs: `evidence/builds/gateway-observability-20261029-182908-6557558-61361c50.json`, PASS.
- Read-only authority preflight: `evidence/builds/authority-preflight-20261029-182906.json`, HALT; issuer, separate signed Checker, effective grants/revocation, protected KMS and ACL not verified.
- PostgreSQL was not rerun because Cycle0023 changed only PowerShell mock contracts; Cycle0022's disposable FORCE-RLS 102/102 receipt remains historical evidence, not a newly claimed test result.

Engineering LOOP: simulated CAS and denials pass, production gating remains HALT. No native crash causality established. No real keys, grants, workers, models, full Aspire, prod route, Git push/commit, checkpoint migration, garbage collection, or modification of G:\workspace historical Trails, Seed0033 HALT or Seed0034 UNEXECUTED. Exactly one new NextSeed remains UNEXECUTED.
