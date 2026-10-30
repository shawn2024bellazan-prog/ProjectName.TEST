# Development-only authority verification and transactional checkpoint candidate
Date: 2026-10-29
Canonical root: G:\ProjectName
Verdict: engineering candidate; production admission DENY.

## Authority preflight contract

`eng/verify-authority-prerequisites.ps1` is a read-only Windows evidence
probe. It now discovers the current Windows SID (reports only a SHA-256
fingerprint internally), hashes the exact candidate source, inspects host
certificate signatures, tests signed issuer/grant/revocation/Checker statements
and checks directory ACLs. The host evidence root is separate from this repo:
`C:\ProgramData\ProjectName\authority`. This directory must already exist,
have an owner of SYSTEM/Administrators, and grant no broad write access.
No directories, identities, trust anchors, grants or keys are created.

Optional host evidence files (not created here) are `issuer.json`,
`grant.json`, `revocations.json`, `checker.json` and `kms.json`.
Each envelope has `purpose`, `payloadBase64`, `signatureBase64`,
`certificateSha256Hex`. Verification requires an unexpired ECDSA signer
present in the LocalMachine TrustedPeople certificate store, SHA-256
certificate equality, digitalSignature usage, and a purpose-prefixed signed
payload (`purpose\n` + UTF-8 JSON). The issuer proof must match the current
Windows SID hash and exact source digest; the grant must bind actor, digest,
expiry and grant ID; the revocation snapshot must come from the same issuer
and exclude the grant; the Checker must have a distinct signer, digest,
identity and evidence reference. All checks fail closed when evidence is
missing or malformed. A Windows login alone is not proof of authority.

The KMS probe checks only a signed, external, non-secret configuration
reference; it expressly does NOT verify an actual HSM, key provider access
or a protected key. Consequently `protectedKeySourceVerified` and
`productionActivationAllowed` remain false. The real environment requires
a protected KMS/provider attestation and separately authenticated Checker
review before activation. Never treat a test fixture or a signed config
as host authorization.

`ReadOnlyAuthorityPreflight` in ProjectName.Governance provides testable,
read-only interfaces for issuer assertion, registration, effective grant,
revocation, signed Checker receipt, protected provider and ACL inspection.
Its verdict always sets ProductionActivationAllowed=false. Test doubles
exercise acceptance/rejection logic only, not authenticated production PASS.

## PostgreSQL candidate

`PostgresTransactionalCheckpointCandidate` is an unregistered ADO.NET
`DbDataSource` adapter in ProjectName.Persistence. It is not a live native
`ICheckpointStore<JsonElement>`; it is not registered in Aspire, service DI,
or the AgentGateway and cannot encrypt, decrypt, provision keys or admit
workers. Callers supply opaque *already encrypted* envelopes, never raw
checkpoint JSON. Tests use visibly fake UTF-8 ciphertext placeholders.

Integration fixture DDL is confined to
`pmcro_checkpoint_candidate_v1` inside a database explicitly named
`pmcro_checkpoint_candidate_tests` on `127.0.0.1`. The runtime adapter
never creates or drops schema. All queries bind exact issuer, tenant,
actor, workflow SHA-256, session and checkpoint keys. The PostgreSQL
composite PRIMARY KEY prevents duplicate checkpoints. Serializable
transactions atomically publish envelope and pointer metadata; UPDATE
performs revision/digest/key-version CAS and denies revoked rows.
At most three bounded conflict attempts are allowed. Reads verify digest,
and revoked reads expose no envelope bytes.

The integration test uses two independent Npgsql data sources against one
isolated temporary PostgreSQL instance. It covers duplicate admission,
issuer/tenant/actor/workflow separation, concurrent CAS (one winner),
stale writer rejection, monotonic versions, cancellation, bounded retries,
revocation, and forcibly terminating an actual PostgreSQL backend AFTER
UPDATE but BEFORE COMMIT. A fresh connection sees the previous committed
row and can later commit a valid version. This is a database-backend crash
simulation, NOT a physical power-loss or geographically separated multi-host
test. Durable backups, real envelope encryption, key rotation, KMS,
host principal binding, tenant RLS, actual multi-host failover and
independently signed Checker PASS remain unverified.

## Safety and promotion

- No existing Aspire PostgreSQL named volume is mounted by the test.
- The ephemeral test instance uses local loopback dynamic port, a throwaway
  username/password, no host volume and `--rm` with a tmpfs data directory.
- No real checkpoint data is migrated, cleaned up or garbage-collected.
- No production workers, models, MCP tools or protocol routes are activated.
- Preserve G:\workspace historical Trails, Seed 0033 HALT and Seed 0034
  UNEXECUTED. Source hashes and build receipts are engineering evidence,
  not an independent authenticated Checker signature.
