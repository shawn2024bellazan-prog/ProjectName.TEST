# ProjectName MCP host-approval readiness — offline development increment

Date: 2026-10-29
Root: G:\ProjectName
State: DEVELOPMENT CANDIDATE ONLY / NO HOST AUTHORIZATION / PRODUCTION DENY

## Implemented offline

- `FileMcpApprovalUseStore`: an optional persistent, per-invocation
  human-approval consumption store. Requires a pre-existing host-managed,
  absolute directory. Each grant/invocation pair is represented by a
  deterministic SHA-256 marker created with atomic `FileMode.CreateNew`.
  Duplicates fail closed across instances and simultaneous calls.
- `PinnedMcpRevocationSnapshotVerifier`: verifies a short-lived,
  ECDSA-P256/SHA256-signed revocation JSON snapshot using the already existing
  exact certificate pin and signed revocation purpose marker. It rejects
  malformed/unknown/duplicate fields, wrong issuer/policy, expired or stale
  evidence, duplicates and wildcard revocation IDs.
- Unit tests cover ledger replay, parallel claims, expiration and separate
  approver binding, plus signed snapshot authenticity, freshness and scope.
  All positive signatures use ephemeral test-only certificates.

## Explicit non-capabilities

This code is NOT connected to an AppHost MCP endpoint. No trusted
certificate, signed allowlist, host signer, real revocation fetcher,
production ledger directory, authenticated grant issuer, effective grant,
independent Checker PASS, or active human-approval adapter is configured.

The ledger is a one-machine filesystem candidate, NOT a distributed
production ledger. Its parent directory must be protected against removal,
replacement, symlink redirection, and unapproved ACL changes by the actual
Windows host. Never reuse this storage without a separate security review.
Atomic marker creation prevents concurrent double-claims only while the
protected directory and marker files remain intact.

A fresh revocation snapshot proves only what that trusted issuer signed;
it never issues an approval. A non-revoked grant is still unapproved unless
every independent tool authorization and Checker condition passes.

## Required next external evidence

1. Host authority selects the exact tools, principals, resource scopes,
   source digests, expiration, signer certificate SHA-256 pin and revocation
   policy; sign the approval via the host, not chat.
2. Host wires the trusted certificate and signed evidence parser into a
   real `IHostMcpApprovalSource`, with fail-closed freshness checks and an
   authenticated human approval broker.
3. Host provisions and ACL-protects the durable approval ledger directory,
   then verifies atomicity and recovery on the actual target deployment.
4. Distinct independent Checker reviews the exact digests, signed grants,
   revocation data and service adapter boundaries.
5. Only after those checks, port the three isolated MCP server processes,
   expose the exact approved tools in Aspire and test direct-call denial.

Existing Aspire topology is untouched. All unapproved tools and
production worker execution remain DENY.
