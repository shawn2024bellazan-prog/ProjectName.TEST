# REFLECT — deterministic checkpoint child termination
Canonical root: G:\ProjectName
Scope: development code/tests/docs only; NO production activation.
I AM @orchestrator acting on project-local development charter, NOT Checker.

## Increment
- Added internal test-only fault callback in EncryptedMafCheckpointStore,
  limited by InternalsVisibleTo(ProjectName.CheckpointProbe). Default is null;
  no production DI, AppHost, route, worker or marketplace registration uses it.
- Probe mode kill-after-payload writes the opaque checkpoint ID into an
  ephemeral test directory, then kills the child process exactly after
  ciphertext payload persistence and before the encrypted index update.
- xUnit proves the child really reached that seam and exited nonzero; after
  restart the earlier committed index remains unchanged, orphan retrieval
  is denied, three encrypted files demonstrate orphan persistence, and
  a later valid index/payload commit succeeds. No plaintext JSON files.
- This proves forced process termination at a controlled point, NOT
  power-loss directory durability, distributed transactions or orphan
  reclamation. See docs/CHECKPOINT-DURABILITY-BOUNDARY.md.

## Real validation evidence
- Locked SDK 10.0.403 restore, Release build PASS (0 warnings/0 errors),
  31/31 tests PASS: evidence\builds\core-20261029-001703.json and .log.
- Development gateway and denied native protocols/throttle PASS:
  evidence\builds\gateway-20261029-001733.json.
- Development Scalar/MCP and model-free simulation PASS:
  evidence\builds\dev-surfaces-20261029-001740.json.
- Aspire DevUI/AppHost start, MCP listing and stop PASS:
  evidence\builds\aspire-mcp-20261029-001757.json.
- Authority discovery HALT; issuer/Checker/grants/keys/ACL absent:
  evidence\builds\authority-preflight-20261029-001738.json.
- SHA-256 snapshot of 281 source files:
  evidence\builds\sha256-20261029-001836.csv.

## CHECK / REFLECT
Engineering PASS within development scope only.
Independent Checker PASS: not obtained. Production result HALT.
No actual credentials, grants, workers, agents, inference, publication,
prod routes, or historical G:\workspace Trail modifications.
Archived previous raw seed:
.pmcro\seeds\seed-0007-deterministic-checkpoint-crash.txt.
One new .pmcro\next-seed.txt, UNEXECUTED/UNAUTHORIZED/checkerPass=false;
the Reflector does not enqueue it. Historical Seed 0033 HALT and
Seed 0034 UNEXECUTED remain preserved.
