# REFLECT — Local-only Aspire Ollama model and keyed client

Canonical root: G:\ProjectName. Date: 2026-10-29. Development-only.
I AM @orchestrator reporting engineering evidence, **not** an independently authenticated Checker.

## ORCHESTRATE / PLAN

User requested complete local Aspire configuration after GPU, PostgreSQL, Redis, Scalar and DevUI setup.
Verified GPU RTX 4070 Laptop (8188 MiB), free device memory, disk capacity,
existing AppHost resources, source instructions and deny-all gateway before editing.
Selected the smaller qwen3:4b rather than the GitHub qwen3:8b/16K precedent to reduce
memory pressure on this development GPU. Model pulls are authorized development assets;
production worker execution and model inference are not.

## MAKE

- Declared `model-orchestrator` -> `qwen3:4b` in Development-only AppHost.
- Preserved `projectname-ollama-data` and the PostgreSQL/Redis volumes.
- Set `OLLAMA_NO_CLOUD=1`; live Ollama log proved cloud features disabled.
- Referenced the model from AgentGateway; registered its keyed `IChatClient`
  only under Development and only when Aspire injects its connection string.
- Exposed **no** model-calling, OpenAI, A2A, AG-UI or worker-execution endpoint.
- Created `eng/verify-aspire-dev.ps1` with redacted, repeatable health and denial
  checks; documented start/stop commands and security boundaries.

## CHECK

- .NET SDK 10.0.403, locked restore, Release build: 0 warnings, 0 errors.
  31/31 governance tests PASS: `evidence/builds/core-20261029-010243.json`.
- Detached Aspire restart without force or volume removal; final 14/14 resources
  Running and Healthy; six HTTP checks passed (five 200, denied gateway path 403).
  `evidence/builds/aspire-dev-20261029-011035.json` is ENGINEERING_PASS.
- `qwen3:4b` downloaded; model digest:
  `359d7dd4bcdab3d86b87d73ac27966f4dbb9f5efdfcc75d34a8764a09474fae7`.
- Docker GPU request and in-container Nvidia SMI verified after restart.
- No model generation or MAF inference request was executed.
- Host authority preflight HALT for missing issuer/Checker/grants/KMS/ACL:
  `evidence/builds/authority-preflight-20261029-010208.json`.
- These engineering checks are **not** an independent Checker PASS.

## REFLECT

Local development infrastructure: engineering PASS.
Production worker authorization: HALT, default DENY. No grants, keys, queue workers,
signed receipts, GitHub promotion or historical G:\workspace evidence modified.
The pre-existing `.pmcro/next-seed.txt` remains the **one UNEXECUTED,
UNAUTHORIZED NextSeed**. Execute no future seed without new admission evidence.
