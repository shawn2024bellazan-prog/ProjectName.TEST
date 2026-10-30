# A2A Agent Card (v1.0)

Each generated agent gets an `agent-card.json`. In A2A the card is the self-describing manifest an agent publishes at `/.well-known/agent-card.json`; a client reads it before its first request.

## Required fields used by the generator

`name`, `description`, `version`, `supportedInterfaces[]` (each with `url`, `protocolBinding`, `protocolVersion`), `capabilities`, `defaultInputModes`, `defaultOutputModes`, and `skills[]` (each with `id`, `name`, `description`, `tags`). Keys are camelCase.

## Rules in this repo

- The card is discovery only. DISCOVERY != REGISTRATION != GRANT != ACTIVATION != EXECUTION.
- The interface URL is a placeholder on the reserved `.invalid` domain, so nothing can resolve or be called.
- No A2A route is mounted. Mounting needs registration, an effective grant, a signed allowlist and an independent Checker PASS bound to the card digest.
- Skills are `inspect`, plus `guide` and/or `propose-fix` according to mode. `propose-fix` never applies a change.
- Capabilities are all false (no streaming, no push, no extended card).
- The card has no non-standard fields. Draft state lives in `agent.spec.json`.

## Before the spec is trusted

The A2A spec moved between 0.3 and 1.0 (protocol version moved into each interface). Re-check the field list against the current spec before any mount.
