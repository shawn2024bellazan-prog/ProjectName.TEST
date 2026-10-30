# PMCR-O Reflection — Cycle 0017 Parent-Owned Gateway Watchdog

Date: 2026-10-29
Canonical root: G:\ProjectName
Identity: one assistant narrating five PMCR-O roles, not five separately admitted agents.
Engineering verdict: LOOP (observability improved; historical runtime cause unresolved).
Production verdict: HALT; independent authenticated Checker PASS=false.

## ORCHESTRATE
Fresh development-only user intent "coninue" saved in
.pmcro/seeds/seed-0017-development-continuation.txt.
Prior 0016 UNEXECUTED NextSeed archived byte-for-byte at
.pmcro/seeds/staged-nextseed-after-seed-0016.txt,
SHA-256 2AD4754E6F8DF06295297D96ACB9D6D00C7709D2C80EDAF616AACC60637AC1E7.
Read G:\ProjectName\AGENTS.md; G:\workspace history untouched.

## PLAN
Build a parent-owned gateway watchdog for 1–3 isolated child smokes,
bounded to 2–90 seconds per child. The parent writes receipt even if a
child terminates abruptly. Preserve original gateway script and app
topology. Test child success/failure/abrupt exit/timeout before live
gateway smoke. Keep native protocol routes unmounted and denied.

## MAKE
New eng/watch-agent-gateway.ps1 with uniquely named CreateNew receipts,
exact exit code, timeouts, child output paths and port-release check.
New tests/fixtures/watchdog-child.ps1 and
tests/fixtures/inspect-coreclr-unavailable.ps1; new explanation at
docs/GATEWAY-WATCHDOG-0017-2026-10-29.md.
No application, AppHost, MAF, agent, gateway or PostgreSQL source changed.
No full Aspire or production worker started.

## CHECK
FixturePass: 2/2 passed,
gateway-watchdog-20261029-151313-5182805-731e4e9e.json.
FixtureFail: exit 23 captured; stop after first,
gateway-watchdog-20261029-151321-6056899-3d4d9f17.json.
FixtureAbruptExit: synthetic 0xC0000409 captured, not attributed
to actual CoreCLR fault,
gateway-watchdog-20261029-151322-3269995-4d091183.json.
FixtureTimeout: 2-second cap stopped exact fixture process,
gateway-watchdog-20261029-151323-0306968-15ec13f0.json.
Actual isolated gateway: 3/3 runs exited 0, Development/Production
denial checks passed, no orphan listener,
gateway-watchdog-20261029-151343-4493450-d4bcc154.json.
Receipts above reside in evidence/builds/.
Read-only CoreCLR inspector historical match=2, later match=0;
48-hour window rejected; mocked event-log inaccessible rejected.
New 15:10+ matching CoreCLR events=0, not proof of no other crash.
Locked .NET 10 SDK 10.0.403 Release restore/build: PASS, zero
warnings/errors, 102/102 governance tests PASS;
evidence/builds/core-20261029-151500.json.
Standalone DevUI/Scalar/MCP, simulated workflow DENY and production
route denial PASS: evidence/builds/dev-surfaces-20261029-151523.json.
Explicit disposable PostgreSQL 18.3 role-bound forced RLS tests:
102/102 PASS, zero failures, TRX at
evidence/builds/checkpoint-cycle-0017-20261029.trx.
RLS enabled and forced, temporary container pmcro-candidate-pg-0017
stopped and auto-removed. No persistent volumes.
Read-only authority preflight HALT:
evidence/builds/authority-preflight-20261029-151618.json;
issuer, independent signed Checker, grants, revocation, KMS and ACL
remain unverified. Self-check only; no independently authenticated verdict.
No separately authorized multi-host fenced failover fixture.

## REFLECT
Watchdog now preserves evidence for synthetic abrupt child exits.
Prior CoreCLR 0xc0000005 and later 0xC0000409 runner exit remain
unexplained and not proven causally equivalent. Do not change gateway
application code without demonstrated defect. No sensitive dumps or
native traces were collected. Preserve production admission DENY,
CheckerPass=false, ProductionActivationAllowed=false.
One new NextSeed is UNEXECUTED/UNAUTHORIZED.
