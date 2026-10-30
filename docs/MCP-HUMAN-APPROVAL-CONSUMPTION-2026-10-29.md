# MCP human-approval consumption boundary — development candidate

Date: 2026-10-29
Root: G:\ProjectName
State: BUILT_AND_UNIT_TESTED_ONLY / NO_HOST_APPROVAL / NOT_ACTIVE

## Change

`ProjectName.ControlPlane.McpToolAuthorizationGate` now fails closed for any
MCP invocation classified as side-effecting or requiring human approval unless
an independently host-verified, invocation-bound approval is consumed exactly
once through `IHostMcpApprovalUseStore.TryConsume`.

The approval binds grant ID, invocation ID, arguments digest, distinct human
approver, evidence ID and expiry. Before consumption, the gate also requires
host grant match, non-revocation, the existing capability admission gate, and
independent Checker receipt verification.

No production implementation of this store is registered. An absent store,
duplicate use, or exception returns HUMAN_APPROVAL_CONSUMPTION_UNAVAILABLE.
This is deliberately stricter than allowing an approval to be replayed.

## Scope and remaining controls

- Test-only lock/HashSet implementation is never registered in AppHost.
- An eventual host implementation must persist usage atomically across
  concurrent requests and process restarts; no in-memory ledger is sufficient.
- A consumed approval is not authorization to run a tool without fresh
  matching source digest, principal, scope and trusted revocation evidence.
- The pure ECDSA verifier does not issue or validate an effective host grant
  end-to-end; the signed host adapter and trust anchor remain unconfigured.
- No approved tools/allowlist.json, authenticated issuer, durable revocation
  store, independent Checker PASS, or approved human-approval adapter exists.
- Do not advertise or mount Filesystem, Playwright, or Terminal MCP tools
  in Aspire until that evidence is independently established.
- Existing Aspire topology and production worker admission are unchanged;
  production worker admission remains DENY.

## Validation

.NET 10 Release solution tests: 76 passed, 0 failed, 0 skipped.
.NET 10 Release solution build: succeeded, 0 warnings, 0 errors.
Two regression cases: no approval-consumption store denies; same approval
cannot be consumed twice in the test-only atomic store.

These development checks are not an independent Checker verdict and do not
authorize MCP registration, activation, or execution.
