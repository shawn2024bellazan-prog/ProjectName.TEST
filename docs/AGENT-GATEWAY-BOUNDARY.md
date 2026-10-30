# Agent gateway boundary — candidate increment, 2026-10-28

Root: G:\ProjectName. This is a separate new host from ProjectName.OrchestrationApi.

## What is implemented
- ProjectName.AgentGateway is included in ProjectName.slnx and in the Aspire AppHost.
- NuGet centrally pins Microsoft.Agents.AI.Hosting.OpenAI (1.26.0-alpha.261023.1),
  Hosting.A2A.AspNetCore (1.26.0-preview.261023.1), and
  Hosting.AGUI.AspNetCore (1.26.0-preview.261023.1).
- NativeProtocolServices contains compiling service-registration calls for OpenAI
  Responses, Conversations, A2A, and AG-UI. It is not invoked by Program.
- NativeProtocolAdmission now requires a host-verified issuer/tenant/actor identity
  (IVerifiedIdentityResolver), the same authenticated subject, and then the
  registration, effective grant, bound digest, revocation, and separate Checker.
  The default resolver denies all identities, even with caller-supplied claims.
- EncryptedTenantStateStore provides AES-256-GCM authenticated ciphertext,
  tenant/actor/issuer/agent/opaque-ID binding, HMAC-derived filenames, atomic
  writes, and deletion. It accepts only an externally provided key and an
  existing host-provisioned absolute storage root; the host must enforce ACLs.
- EncryptedMafSessionStore is a compiled MAF 1.26 AgentSessionStore adapter,
  returning fresh deserialized sessions and rejecting unverified callers.
  It is opt-in, not registered in default DI and not live-tested with an agent.
- EncryptedMafCheckpointStore implements the native MAF 1.26 JSON checkpoint
  storage interface. Offline five-phase run, encrypted commit, fresh-store
  resume, tenant/digest isolation, and tamper denial are proven by tests.
  Its index now uses a bounded OS file lease to serialize cooperating local
  writers; a second-process lease denial, terminated-holder release, and
  interrupted-stage integrity tests pass. Two independent test-only .NET
  processes also commit 24 additional checkpoints into a shared encrypted
  local index without loss. A test-only child killed exactly between encrypted
  payload and index save leaves the committed index valid on restart. Staged
  ciphertext is flushed before local atomic rename. See
  docs/CHECKPOINT-DURABILITY-BOUNDARY.md for limits.
  Multi-host transactions, true power-loss recovery, key rotation and
  production key/ACL policy are not implemented.
- PrivacySafeGatewayMetrics exposes denial and throttle counts with no user,
  tenant, prompt, trace payload or session tags. OpenTelemetry observes this
  meter locally; no exporter is configured.
- Development and production both expose health and an honest, read-only
  status manifest. All other paths return HTTP 403, including guessed native
  protocol paths and agent-card discovery paths.
- Per-IP fixed-window limit: 30 requests/minute, no queue, 429 on overage.

## Not yet implemented / never claim as active
- No AIAgent or protocol route has been registered, mapped, granted or activated.
- No authentication issuer, signed checker route, trusted agent registry, provider
  credentials, background worker, activated hosted session store, key source,
  verified storage ACLs, or a production distributed CheckpointManager backend
  is configured. The encrypted vault and locally lease-serialized checkpoint
  adapter are checked development components, not deployed stores.
- There is no live OpenAI Responses/Conversations, A2A or AG-UI conformance
  result. The packages are pinned and compile; endpoints are intentionally
  non-executable.
- No inference/model/tool calls were made. No new agent was added to DevUI.
- No telemetry exporter, trace payload capture, plaintext session persistence,
  or signed production activation has been enabled in this gateway.

## Promotion requirements
1. Authenticate host principal with a verified issuer and tenant claim; do not
   derive authority from client-supplied actor IDs or forwarded headers.
2. Bind the capability/agent digest to an effective grant; reject revocations.
3. Verify signed, independent Checker PASS and recheck on each invocation.
4. Provide tenant-isolated encrypted durable sessions, checkpoints and ACLs.
5. Add rate-limit policies by authenticated tenant, privacy-safe telemetry, and
   protocol request/stream denial/conformance tests.
6. Only then mount each native protocol in its own authorized route group.
7. Require a separate release/activation authority before any production use.

Engineering validation and independent Checker review are different. A
successful build or smoke test never promotes this candidate.
