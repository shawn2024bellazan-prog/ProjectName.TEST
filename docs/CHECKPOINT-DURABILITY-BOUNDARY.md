# Encrypted MAF checkpoint durability boundary — development candidate

Canonical root: G:\ProjectName
Status: engineering candidate; NOT an activated production checkpoint backend.

## Implemented local contract

- EncryptedTenantStateStore uses host-supplied AES-256-GCM key material and an
  existing absolute directory. Associated data binds verified issuer, tenant,
  actor, agent and state ID; HMAC-derived paths contain no raw identities.
- SaveAsync writes an authenticated ciphertext envelope to a new random
  temporary file, flushes the staged file with Flush(flushToDisk: true), closes
  it, then performs File.Move(overwrite: true) for atomic visibility on a
  compatible local filesystem. Cancellation before rename does not replace
  previously committed content. This does NOT guarantee directory-entry
  durability against sudden power loss, and is not a transaction across files.
- EncryptedMafCheckpointStore takes a bounded 10-second exclusive OS file
  lease before reading, appending, and saving the encrypted checkpoint index.
  Payload is saved before its indexed reference. A failed commit can leave a
  private, unreferenced encrypted payload, but should not publish a reference
  to an incompletely staged payload under the local rename contract.
- Windows tests prove an independent process cannot open the held lease, and
  terminating a lease-holding process releases it for reacquisition. Another
  test launches two independent .NET test-only processes through a start gate;
  each commits 12 checkpoints to the same encrypted session. A fresh store
  retrieves 25/25 unique entries and their full encrypted payloads, including
  the initial seed checkpoint. This is local host/process, NOT multi-host.
- Tests also prove an abandoned random *.tmp and a cancelled replacement do
  not replace previously committed authenticated encrypted bytes. A synthetic
  interruption between payload and index leaves an unreadable-by-index orphan
  and does not prevent subsequent well-formed commits. A second test installs
  a friend-assembly-only fault seam, then kills a real test child immediately
  after the encrypted payload save and before index publication. After restart,
  the previous index remains intact, the orphan is inaccessible via checkpoint
  lookup, and a later commit succeeds. This is process termination, NOT sudden
  power loss; encrypted orphan garbage collection is not implemented.
- No plaintext FileSystemJsonCheckpointStore is enabled by this adapter.

## Open risks and required production design

1. Multi-host and network shares: FileShare.None and DeleteOnClose are local
   filesystem synchronization tools, NOT a distributed lease or transaction.
   Reject deployment on a shared volume or replicated storage until a real
   transactional implementation and fault tests exist.
2. Cross-file crash recovery: payload and index are separate encrypted files.
   Abrupt power loss during rename, directory metadata persistence, orphan
   encrypted payload collection, and partial index cleanup remain unverified.
   There is no recovery job, repair policy, or retention schedule.
3. Key rotation: the same external key protects ciphertext and HMAC file
   naming, so changing it directly makes old state unfindable. Never silently
   replace it or try a caller-chosen key. Design a host-approved, versioned
   envelope/key identifier, authenticated keyset manifest, bounded dual-read
   migration, atomic proof of rewrite, rollback and revocation. No real key,
   key provider, grant or credential has been created. The bounded candidate
   protocol is documented in docs/CHECKPOINT-KEY-ROTATION-DESIGN.md.
4. Multi-host candidate: implement a separate transactional store with
   tenant/issuer/actor/workflow-digest/session/checkpoint composite keys,
   unique index, compare-and-swap version, and atomic checkpoint payload plus
   index commit inside one database transaction. Retry serialization conflicts
   with finite caps; use per-tenant RBAC, host-provided encryption/key version,
   explicit revocation, crash-injection and distributed race tests. Reuse the
   pinned native MAF ICheckpointStore<JsonElement> contract. Do not migrate
   current *.enc files without an authenticated, reversible migration plan.
5. Deployment: authenticate an external issuer, effective grant registry,
   independent signed Checker bound to exact digests, secure KMS source,
   protected ACLs, storage retention, privacy-safe instrumentation and
   operational backups. Current default remains deny all; native sessions,
   agent routes, workers and model calls stay disabled.

Engineering validation is not independent Checker approval.
G:\workspace historical Trails, Seed 0033 HALT and Seed 0034 UNEXECUTED
are outside this candidate's write scope.
