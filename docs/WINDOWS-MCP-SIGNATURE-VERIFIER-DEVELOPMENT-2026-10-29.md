# Windows host approval evidence verifier — unconfigured candidate

Date: 2026-10-29
Status: DRAFT_UNAPPROVED / NO TRUST ANCHOR / NO KEYS / NO ISSUER / NO GRANTS.

ProjectName.ControlPlane.PinnedWindowsApprovalSignatureVerifier verifies
an ECDSA P-256 / SHA-256 signature over bounded UTF-8 payload bytes against
an externally supplied, exact SHA-256 X.509 certificate pin.

- No local key generation, private key import, certificate enrollment or
  automatic signer selection is performed.
- A missing certificate or pin always fails verification.
- The signed bytes must begin with the domain-purpose marker and newline.
  Separate purpose markers exist for grant, human invocation, revocation.
- The certificate must be within its validity interval, have the
  digitalSignature key usage, and contain a P-256 public key.
- The public key signature is checked as IEEE P1363 fixed-length format.
- The pin is SHA-256 over the complete certificate, not the SHA-1 Thumbprint.
- Cert/key mismatch, malformed payload, wrong purpose or verification errors
  fail closed. Verification is not itself a PMCR-O grant or Checker verdict.

The ECDSA X.509 host-approval evidence format is distinct from PMCR-O
agent identity (which has a separate Ed25519 specification). This code
does NOT claim to implement signed agent identity.

## Required host adaptation before any tool activation

1. An accountable human/host authority must explicitly designate a trusted
   approver certificate and record its SHA-256 fingerprint in protected
   configuration; no default trusted certificate is bundled.
2. An approved issuer must produce one canonical, domain-separated signed
   document binding grant ID, principal, tool, scope, source digest, expiry,
   revocation policy and separate human invocation when side effects apply.
3. The host adapter must verify the signature, parse the exact signed fields
   and compare every field to the actual canonicalized invocation. It must
   never accept caller-supplied claims as verified identity.
4. Verify signed revocation state with fail-closed freshness and an atomic
   single-use approval-consumption store before executing each tool call.
5. Pass independently authenticated Checker evidence through the existing
   AdmissionGate and record an append-only audit decision.
6. Recheck all predicates immediately before execution; deny on exceptions,
   mismatches, stale revocation data, missing signer or missing checker.

Current implementation intentionally stops at the pure verifier. There is
no approved issuer, verified host grant, revocation ledger, registered MCP
adapter, Aspire resource or production worker. All remain DENY.

Unit tests use ephemeral certificates created and discarded in-process;
they provide no real host signature or authorization evidence.
