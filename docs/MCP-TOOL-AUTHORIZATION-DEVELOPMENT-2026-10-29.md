# MCP tool authorization guard — development candidate (2026-10-29)

Status: DRAFT_UNAPPROVED; NOT REGISTERED; NOT IN APPHOST; NO TOOL EXECUTION.

Added ProjectName.ControlPlane.McpToolAuthorizationGate as a reusable
development candidate. It composes the existing AdmissionGate and does not
register an MCP endpoint, grant issuer, tool, worker or authentication service.

## Fail-closed predicates

1. Request has an exact server ID, tool name, actor, scope and invocation ID.
   Both source and argument digests must be sha256: followed by 64 hex digits.
   Wildcards and literal parent-traversal scopes are denied.
2. VerifiedIdentity must come from an authenticated host adapter and match
   the invocation actor. Caller-provided claims or request fields are not
   sufficient to construct a trusted identity.
3. IHostMcpApprovalSource must return independently authenticated,
   provenance-bound host approval (principal, issuer, server, tool, exact
   resource scope, source digest, grant ID, evidence, expiry and revocation
   policy). The shipped implementation denies all approvals.
4. Revocation lookup must complete successfully and report not revoked.
   Errors in verification or revocation fail closed.
5. Side-effecting tools require a separately verified, non-self-issued human
   approval bound to exact grant, invocation ID and arguments digest.
   Host-signed side-effect classification cannot be downgraded by a caller.
   A real provider must consume single-use approvals atomically.
6. Existing AdmissionGate must also find registration, effective grant,
   matching source digest, current revocation state and independently verified
   Checker PASS. No new bypass to its existing deny-all defaults exists.

The host adapter for signatures, authenticated human approval, atomic replay
prevention and revocation has NOT been implemented or activated. Positive
unit tests use explicit in-memory test doubles only and are not evidence
of a live host authorization, nor permission to start an MCP server.

## Prior prerequisites for any production or development tool exposure

- A signed exact allowlist, approved principal, scope, source digest,
  expiry and revocation policy verified by trusted host identity.
- Independently authenticated Checker evidence tied to the same digest.
- Scoped, audited host approval per side-effecting invocation.
- Tool-specific canonicalization (filesystem paths, browser origins,
  terminal command policy) inside isolated MCP service boundaries.
- Negative security tests for direct MCP calls, replay, service spoofing,
  wildcard/traversal, expired/revoked grant, missing Checker and missing
  per-call human approval.

Never mount the three candidate MCP servers until those prerequisites
are independently satisfied. Production worker admission remains DENY.
