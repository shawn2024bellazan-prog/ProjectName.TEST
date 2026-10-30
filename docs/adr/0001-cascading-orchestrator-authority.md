# ADR 0001 — Cascading domain-bounded Orchestrator authority

Status: PROPOSED (development only)
Date: 2026-10-29
Decision owner: PMCR-O domain Orchestrator, subject to host/tenant/security policy
Evidence tier: pattern-valid only after local tests; no independent Checker PASS

## Context

Everything-as-Agent requires broad Meta Orchestration and narrower specialty Orchestrators: CEO, Planner, Maker, Checker, Reflector, Social Media and Facebook can each coordinate their own micro-cycles. A role title or message cannot confer privileges.

## Decision

- Orchestrator is the highest accountable coordinator within its signed domain charter, never above host, platform, tenant or security constraints.
- Each child inherits a strict subdomain, a subset of permitted tools and a finite depth/turn/tool/time budget. Missing, expired or revoked authority denies execution.
- A child may perform ORCHESTRATE -> PLAN -> MAKE -> CHECK -> REFLECT within its narrower domain; macro parent coordination accepts evidence, not new authority.
- Dynamic reassignment is a request for policy evaluation and bounded delegation, not a grant.
- The same five-phase grammar applies to @orchestrator, @planner, @maker, @checker and @reflector. Specialist role and cognitive phase are separate axes.
- A Maker's self-check or a simulated @checker frame is never independent Checker PASS.
- AGT is the policy-decision integration candidate; only the authenticated host enforces allow/deny/escalation before any real tool call.

## Alternatives

A flat role list hides domain scope. Separate bespoke runtimes duplicate governance. Unbounded recursive dispatch risks authority amplification. All are rejected.

## Consequences and invariants

ChildDomain must be strictly narrower than ParentDomain; ChildTools is a subset of ParentTools; child depth is bounded. Five phases cannot skip, swap, or restart in the same cycle. Development role frames are simulations until independently admitted.

## Verification

CascadingOrchestrationCandidateTests in xUnit: 5 role self-application cycles, hierarchy and capability contraction, spoofing and out-of-order denial, depth exhaustion and no automatic NextSeed execution. Production activation remains DENY.
