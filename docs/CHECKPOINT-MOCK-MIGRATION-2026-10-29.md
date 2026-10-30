# Mock key rotation and checkpoint migration — development-only

Root: G:\ProjectName
State: simulation candidate; no KMS, production admission or native session mount.

## What is implemented

`MockCheckpointKeyRing` stores identifiers, versions, expiry and state
(Active, ReadOnly, Revoked) only. It contains NO key bytes or crypto handles.
Its key scope is the exact issuer, tenant, actor and SHA-256 workflow digest.
Version and epoch updates are monotonic and compare-and-swap checked. Rotated
keys become read-only, revoked keys cannot be used or silently reactivated.

`MockCheckpointMigrationManifest` stores in-memory ciphertext digests
and authoritative pointers, not payloads. The pointer is scoped to issuer,
tenant, actor, workflow digest, session and checkpoint state ID. A migration
requires the previous revision and previous ciphertext digest, an active
higher-version target key, and a bounded receipt identifier. Both pointer
and revision change in one in-memory lock. Conflict retries cap at three
and observe cancellation. This is a concurrency *model*, not persistent
cross-process or multi-host durability.

`MockCheckpointOrphanPolicy` is an inventory decision function only:
referenced or leased objects stay KEEP; recent objects stay KEEP; older
unreferenced objects with fewer than two stable observations are quarantined;
older stable candidates require MANUAL_REVIEW. There is no delete method.
No garbage collection is performed.

## Crash simulation model

- Before stage: existing v1 pointer remains authoritative.
- Stage v2 then crash before index CAS: v1 pointer remains authoritative,
  staged v2 object is merely an orphan candidate and cannot be retrieved
  through the checkpoint index.
- After successful CAS: v2 pointer is authoritative; any attempt to
  republish v1 or reuse a stale revision is rejected.
- Revocation takes precedence over a readable old key; do not fall back.
- These simulations do not reproduce sudden power loss, actual KMS failure,
  Windows ACL changes, or process-shared database transactions.

## Future unactivated multi-host candidate

Use an entirely separate PostgreSQL transaction-backed
`ICheckpointStore<JsonElement>` implementation. Proposed unique key:
(issuer_id, tenant_id, actor_id, workflow_digest, session_id, checkpoint_id).
Persist ciphertext and index visibility in ONE database transaction.
Store key version, envelope version, state digest, revision, migration receipt,
and revocation epoch. Conditional UPDATE ... WHERE revision = expected guards
against lost updates; a unique index prevents duplicate commits. Use
serializable isolation or documented concurrency strategy and retry at most
three conflicts. Require per-tenant RBAC and protected KMS access. Do not
register this candidate in Aspire, migrate existing *.enc data, or alter the
current Postgres volume without separate authority and Checker approval.

## Verification and constraints

Local test-only checks exercise key version/expiry/revocation, issuer/tenant/
workflow isolation, concurrent CAS, rollback, bounded attempts, interruption
visibility, and non-deleting orphan review. A reflection test confirms the
existing checkpoint fault seam is internal; it remains friend-visible only
to ProjectName.CheckpointProbe. The native five-phase Checker privacy boundary
and production deny-all service configuration are unchanged.

Not implemented: real key provision, KMS connection, signed grant,
authenticated Checker PASS, directory ACL verification, migration worker,
garbage collector, multi-host storage, activated agent, model call,
OpenAI Responses/Conversations, A2A, AG-UI, or production promotion.
