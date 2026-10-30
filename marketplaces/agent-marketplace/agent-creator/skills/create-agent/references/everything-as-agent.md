# Everything-as-Agent

Any subject can become a self-inspecting agent. A subject is a repo (for example Microsoft Agent Framework), a local project, a photo of a messy bed, a TV, a process, a tool, or a role seat inside a Chief's team. The agent runs the PMCR-O cycle on its own subject through self-reference, then guides or proposes a fix.

## Shape of every generated agent

| File | Purpose |
| --- | --- |
| `<id>.agent.md` | Host-convention agent file. First person, no tools, state DRAFT_UNREGISTERED. |
| `agent.spec.json` | The declarative spec: subject, identity, mode, self-inspection, authority flags, checker contract, earned constraint, next seed. |
| `agent-card.json` | A2A v1.0 Agent Card (discovery only). See `a2a-card.md`. |

## Modes

- `guide`: names the next smallest step and what would show it worked.
- `fix`: drafts a reversible fix for human review. It never applies it.
- `both`: both skills.

## Self-reference, bounded

The agent inspects its own subject and its own earlier output. It stops at the authority boundary. Self-inspection cannot register, grant, activate or execute anything, and it cannot produce a Checker PASS.

## Authority laws (copied from the repo, never relaxed here)

DISCOVERY != REGISTRATION != GRANT != ACTIVATION != EXECUTION. Maker cannot approve its own work. Default admission is DENY.

## PMCR-O mapped to Anthropic agentic patterns (proposed mapping)

| Phase | Closest Anthropic pattern |
| --- | --- |
| ORCHESTRATE | Orchestrator-workers, with routing |
| PLAN | Prompt chaining (bare-minimum plan from validated resources) |
| MAKE | Augmented LLM doing the work |
| CHECK | Evaluator-optimizer, evaluator side |
| REFLECT | Evaluator-optimizer, feedback side (the earned constraint) |

## Backward and forward flow

Backward flow: the agent learns while its trail is created and earns constraints. Forward flow: a finished trail runs as a product. Identity injection lets a buyer supply their own identity so one trail serves many people.
