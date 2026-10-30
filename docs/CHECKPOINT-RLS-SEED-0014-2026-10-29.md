# Seed 0014: tenant-RLS checkpoint development candidate and failover test design
Date: 2026-10-29
Canonical root: G:\ProjectName
Scope: DEVELOPMENT ONLY; production worker admission DENY.

## What is implemented
- The isolated `PostgresTransactionalCheckpointCandidate` stays unregistered in service DI, Aspire, AgentGateway and all native agent execution routes.
- Every read, initialize, compare-and-swap commit, and revoke first requires PostgreSQL `current_user` to exactly equal the tenant in `CandidateCheckpointScope`, AND `row_security_active('pmcro_checkpoint_candidate_v1')` to be true. Database superusers and bypass-RLS connections therefore fail the candidate check; the adapter does not create roles or DDL.
- The disposable integration fixture creates separate non-superuser `pmcro_tenant_a` and `pmcro_tenant_b` logins, grants only SELECT/INSERT/UPDATE on the disposable candidate table, and enables + FORCEs tenant-scoped row-level security with `USING` and `WITH CHECK` predicates.
- Integration checks: mismatched tenant login denies read/write; correct login accesses only its tenant; direct SQL tenant A sees zero tenant B rows, while tenant B sees its own row; privileged fixture login is rejected by the adapter; accidental DISABLE ROW LEVEL SECURITY forces denial; concurrent CAS, revision+key-version rollback prevention, bounded retries, cancel, explicit revocation, and `pg_terminate_backend` after UPDATE but before COMMIT preserve prior committed state.
- The table and throwaway tenant credentials live only in an isolated PostgreSQL 18.3 container with loopback host mapping and tmpfs. Neither the fixture roles nor its credentials are production grants.

## Separate-host partition/failover protocol (NOT EXECUTED)
1. Create three isolated disposable processes/namespaces on distinct test VMs: PostgreSQL primary A, transaction client B, and independent observer C. Use synthetic ciphertext bytes and unique, nonproduction issuer/tenant/workflow IDs. No Aspire DB volumes or real credentials.
2. Before each write, observer C pins the exact role, signed-test-identity digest, tenant RLS policy and `pg_is_in_recovery()`/primary term. Reject superusers, BYPASSRLS and cross-tenant queries.
3. Commit revision N from client B; record SHA-256 of envelope, row and attempt receipt. Place a controlled network partition between B and primary A at the following fault points, separately: before BEGIN, after UPDATE before COMMIT, after COMMIT before ACK, and while a competing CAS writer is active.
4. For each fault, use an independent observer C after reconnect: count committed revisions, verify no duplicate accepted revision, monotonic key version, tenant/issuer/workflow isolation and explicit revocation. After COMMIT-before-ACK, client must reconcile by read, not infer failure means no commit.
5. For actual primary failover add a disposable replicated PostgreSQL topology with a documented fenced-leader epoch; show the old primary is fenced before acknowledging a replacement writer. Test stale-primary rejoin and replica lag; if single-leader fencing cannot be proved, mark HALT rather than calling it linearizable.
6. Record bounded retries, failure injection timestamps, per-host clocks, row hashes, WAL/LSN boundaries, DB logs, network rules, exact binaries and teardown receipts. Never interpret passing mocks as a production failover guarantee.

## Authority limitations
The read-only host authority preflight discovered a Windows identity but could not verify an issuer-bound identity assertion, independent signed Checker receipt, effective grant, revocation snapshot, protected KMS provider or protected storage ACL. This remains PRODUCTION HALT. The signed-file verification design is only candidate code; no production keys, grants, approvals or host trust anchors were provisioned.
The tenant role match is a narrow database defense, not proof that an application actor is authenticated or authorized; any future admission needs host-pinned issuer and scoped grant verification outside this candidate.
No test-generated checkpoint is a native EncryptedMafSessionStore snapshot; no OpenAI Responses/Conversations, A2A, AG-UI, or production execution route is mounted.

## Preservation
Preserve G:\workspace historical Trails, Seed 0033 HALT, Seed 0034 UNEXECUTED, exact existing Aspire resource topology, previously captured receipts and source evidence. No destructive checkpoint cleanup or garbage collection is authorized. This candidate does not independently pass Checker review and must not be promoted.
