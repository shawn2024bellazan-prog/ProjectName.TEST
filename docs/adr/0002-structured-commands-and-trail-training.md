# ADR 0002 — Structured commands, cognitive Trail frames and training candidates

Status: PROPOSED (development only)
Date: 2026-10-29
Evidence tier: pattern-valid only after local tests; independent Checker unverified

## Context

Every agent is marketplace-addressable. The same role must be able to orchestrate, plan, make, check and reflect without silently becoming a higher-authority worker. Trail evidence can teach future skills or models, but raw transcripts and model-private reasoning are not authorized training data.

## Decision

- Canonical invocation: `@<agent> /<plugin>:<skill> "<intent>"`.
- PMCR-O plugin skills are fixed in order: orchestrate, plan, make, check, reflect. The actor role is orthogonal to the skill/phase.
- One phase emits one bounded I AM Trail frame with participant, domain, parent run, phase, evidence reference, authority state and outcome; neither message arrival nor tool completion advances a phase.
- The Orchestrator owns Trail and next-seed continuation. Every role may produce a complete five-frame micro-cycle only inside its own charter.
- All draft commands and frames explicitly claim simulated/pattern-only status. Signed approval and independent Checker evidence require distinct verified principals.
- Structured Trail frames may derive dataset candidates through authorized ingestion, consent/license check, redaction, source digest, independent review and separate model-training grant. Training never creates tool permissions.

## Consequences

The marketplace contains typed declarative candidates instead of executable secret-bearing prompts. Tests can validate the entire 5 x 5 role/phase matrix without mounting MCP, A2A, AG-UI or model routes.

## Rejected alternative

Automatic fine-tuning from unchecked raw Trails or allowing simulated check frames to become production grants is prohibited.

## Verification

`marketplaces/pmcro-core/cascading-orchestration.candidate.json` and `CascadingOrchestrationCandidateTests` establish the draft contract. No agent registration, grant, package installation, model call, route mount or production activation is implied.
