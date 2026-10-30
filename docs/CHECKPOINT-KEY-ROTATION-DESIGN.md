# Encrypted checkpoint key rotation — candidate design, not deployed

Scope: G:\ProjectName / native MAF JSON checkpoints. No KMS connection, key
provisioning, external principal, worker registration or production route.

## Existing constraint
EncryptedTenantStateStore derives both the AES-GCM ciphertext protection and
the HMAC filename from the same externally supplied 256-bit key. Swapping the
key changes the filename as well as the decryption authority. A direct key
swap therefore makes existing data invisible; it is NOT safe key rotation.

## Proposed bounded authority-aware migration

1. The host validates issuer/tenant/actor and an effective grant for each
   state scope; a separately authenticated Checker binds exact implementation
   digests before any migration worker is admitted. No client header can
   select a key or widen the state scope.
2. A future host-owned KMS abstraction returns immutable KeyId, purpose,
   status (active/read-only/revoked), expiry and cryptographic key handle.
   The repository must not contain key bytes, fallback default keys, or
   a caller-controlled key ID. Separate data-encryption and path-derivation
   keys in the new envelope format; do not reinterpret the existing v1 format.
3. Read old v1 state only when the verified identity, exact workflow digest,
   old-key ID and migration grant match. Authenticate with old associated data,
   reject tampered data without trying weaker or alternative fallback keys.
4. Stage a v2 envelope under the active key with purpose/tenant/workflow-bound
   associated data and an authenticated versioned envelope header. Reopen and
   verify the new ciphertext before recording any authoritative switch.
5. Commit a monotonic, tenant-scoped migration pointer in a durable
   transactional manifest that binds state ID, old/new KeyIds, expected old
   digest, new ciphertext digest, transaction version and migration receipt.
   Compare-and-swap prevents older writers from reverting a newer pointer.
6. On a crash before manifest commit, v1 remains authoritative and the
   unverifiable v2 staged object is quarantined. On a crash after commit, v2
   remains authoritative. Never silently fall back to v1 after the switch.
7. Retain old ciphertext and approved read-only key for a bounded reversible
   period, then delete by independently checked retention policy. Revoked or
   compromised keys fail closed even if migration is incomplete.
8. A distinct Checker verifies source hashes, signed migration receipts,
   sample round trips, tenant separation, race/crash tests and rollback
   before any release or production activation authority acts.

## Required tests before implementation is called complete

- Cross-process and multi-host compare-and-swap races, duplicate migrations,
  old-key writer against new-key manifest, and mixed-version MAF resume.
- Interrupt before v2 stage, before manifest commit and after manifest commit.
- Wrong key, wrong tenant, wrong issuer, wrong digest and revoked key denial.
- Unauthorized key selection, tampered envelope/manifest and replay downgrade.
- Finite retry, cancellation, log redaction, no plaintext snapshot or trace,
  secure backups and bounded retention/garbage collection.

The local file lease is not a substitute for the transactional manifest.
This is a design artifact only. The currently executable gateway and worker
admission remain deny-all; independent Checker PASS is absent.
