# Request to the host administrator: name an approval signer, sign a three-tool allowlist

Date: 2026-10-29. Project: G:\ProjectName (development only). Status: DRAFT REQUEST. This is not a grant, not an allowlist file and not an approval.

## Why you are asked

Every cycle since 0016 has stopped at the same point: nothing in the project can be admitted, because no principal is authorized to approve and no allowlist is signed. Default admission is DENY, a Maker cannot approve its own work, and an agent cannot name its own signer. Only a host authority can break this.

## Decision 1: name the approval signer

Please name one human (or a controlled service principal you operate) who may sign allowlists and grants for this project, and give us:
- principal name and role, and how the principal authenticates;
- where the signing key lives (protected KMS or equivalent) and who can use it;
- the revocation contact or endpoint, and the default expiry you want;
- who the independent Checker is. It must be a different principal from the Maker.

## Decision 2: sign an allowlist of exactly three tools

Exact server ID, exact tool name, read-only, Development only. No wildcards.

| Server | Tool | Bound | Note |
|---|---|---|---|
| mcp-filesystem | `desktop-commander__read_file` | Read only, inside paths you approve (suggested: `G:\ProjectName\docs`, `G:\ProjectName\.pmcro`). Excludes `.env*`, user secrets, key files. | `write_file` stays DENY. |
| mcp-terminal | `Which` | Resolves an executable name to a path. | `ExecuteRunCommand`, `ExecuteRunScript`, `ExecuteKillProcess`, `GetEnvironment` stay DENY. |
| mcp-playwright | `GetPageTitle` | Read only. | Inert until a navigation grant exists, because navigation, click, fill, submit and `Execute*` stay DENY. You may swap this slot. |

Each entry is bound to a source digest, an expiry (suggested 30 days) and a revocation check. Please compute the digest over the final allowlist bytes yourself when you sign; the project will not pre-compute a digest for you to trust.

## What your signature does NOT do

It does not register any agent, grant any tool, activate any service or execute any seed. Those are separate gates, each needing its own decision. Production stays DENY. Nothing is mounted in the AppHost until you have signed and an independent Checker has verified against the digest.

## Draft allowlist for your review (unsigned, not effective)

```json
{
  "status": "DRAFT_UNSIGNED",
  "environment": "Development",
  "defaultAdmission": "DENY",
  "tools": [
    { "server": "mcp-filesystem", "tool": "desktop-commander__read_file", "mode": "READ_ONLY" },
    { "server": "mcp-terminal", "tool": "Which", "mode": "READ_ONLY" },
    { "server": "mcp-playwright", "tool": "GetPageTitle", "mode": "READ_ONLY" }
  ]
}
```

## Please reply with

Signer: ____________  Principal ID: ____________  Key fingerprint: ____________
Approved path subtree: ____________  Expiry: ____________  Revocation contact: ____________
Independent Checker principal: ____________  Allowlist SHA-256 (computed by you): ____________

## Separate note, not part of the signature

Today's host log shows 5 unexpected power losses, 2 bugchecks (one HYPERVISOR_ERROR) and crashes across unrelated programs. Details and suggested host checks are in `docs\CORECLR-DIAGNOSIS-0025-2026-10-29.md`.
