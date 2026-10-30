---
name: create-agent
description: Turn any subject (repo, local project, object, process, tool, skill, role seat) into a draft, deny-by-default, self-inspecting agent with an A2A v1.0 Agent Card. Use to create one agent or a batch. Never registers, grants, activates or executes.
---

# create-agent

I am invoked by the agent-creator agent as `@agent-creator /agent-creator:create-agent "<subject or batch intent>"`.

## Read order (progressive disclosure)

1. `references/everything-as-agent.md` - the pattern and its limits.
2. `references/a2a-card.md` - what the Agent Card is and is not.
3. `assets/` - the three templates every agent is filled from. Do not hand-write what a template can produce.
4. `scripts/new-agent.ps1` then `scripts/validate-agents.ps1`.

## Procedure

1. ORCHESTRATE: confirm the subject is one the owner supplied. Pick a kind: repo, local-repo, object, process, tool, skill, seat.
2. PLAN: add one entry per subject to `marketplaces/agent-marketplace/subjects.json` with id, kind, group, name, ref, purpose and mode (guide, fix or both). Seats for the C-suite are expanded from the csuite catalog with `-IncludeCSuiteSeats`.
3. MAKE: run `powershell.exe -NoProfile -File scripts\new-agent.ps1 -IncludeCSuiteSeats`. It writes `agents/<id>/` and refreshes `marketplace.json`.
4. CHECK: run `scripts\validate-agents.ps1 -EvidencePath <receipt.json>`. Request an independent Checker; my own validator result is not a PASS.
5. REFLECT: leave one earned constraint and one UNEXECUTED next seed.

## Reject gate

The generator throws on an invalid or duplicate id, an unknown kind or mode, or a missing field. It never overwrites outside `agents/<id>/`, never deletes, and never writes grants.

## Hard limits

Generated agents hold no tools and no MCP grants. Registration, grant, activation and execution stay false. No A2A route is mounted from a card. Credentials never go in a spec.
