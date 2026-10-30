# REFLECT — Nvidia GPU-enabled Aspire Ollama

Canonical root: G:\ProjectName. Development-only change.
I AM @orchestrator coordinating engineering evidence, not an authenticated independent Checker.

## ORCHESTRATE / PLAN / MAKE

User authorized GPU configuration for Ollama after review of GitHub AppHost design.
Hardware discovery: RTX 4070 Laptop 8 GiB; Windows driver 616.92; Docker Nvidia runtime available.
Changed only the development AppHost Ollama builder by adding
`.WithGPUSupport(OllamaGpuVendor.Nvidia)` before existing persistent data volume.
Did not adopt old GitHub 16K-context/flash-attention settings, create a model resource,
download or call a model, enable MAF client routes, or alter Postgres/Redis data.

## CHECK

- Locked .NET 10 SDK 10.0.403 restore, Release build 0 warnings/0 errors, 31/31 xUnit tests PASS:
  `evidence/builds/core-20261029-005041.json`.
- Aspire stopped without volume removal and detached development AppHost restarted.
- 13/13 resources reported Running and Healthy.
- Docker inspect on GPU Ollama shows a GPU device request with Count=-1 and [[gpu]].
- Nvidia SMI inside Ollama container lists NVIDIA GeForce RTX 4070 Laptop GPU (8188 MiB).
- Ollama startup log detected CUDA, compute 8.9, 8.0 GiB total, 6.9 GiB available.
- Ollama API /api/tags HTTP 200, model list empty; no inference benchmark was performed.
- Gateway denied a guessed unadmitted route (HTTP 403).
- Additional evidence: `evidence/builds/ollama-gpu-20261029.json`.

## REFLECT

Development GPU passthrough PASS. Production authorization HALT remains.
No independent signed Checker PASS, identity issuer, effective grant, protected KMS
or activation. No historical G:\workspace seed/Trail edits.
The existing `.pmcro/next-seed.txt` remains the single UNEXECUTED NextSeed.
Future model selection/pull and MAF IChatClient injection require a separately
bounded development request with VRAM constraints and no unadmitted execution.
