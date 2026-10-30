using ProjectName.Contracts;
using ProjectName.Governance;

namespace ProjectName.ControlPlane;

/// <summary>A canonicalized, bounded tool request; this record is NOT an approval.</summary>
public sealed record McpToolInvocation(
    string TrailId, string ActorId, string ServerId, string ToolName,
    string Scope, string SourceDigest, string ArgumentsDigest,
    string InvocationId, bool HasSideEffects);

/// <summary>
/// Only a host verifier validating trusted approval provenance may return
/// this record. No request payload may be used as its source.
/// </summary>
public sealed record HostVerifiedMcpGrant(
    string GrantId, string PrincipalActorId, string IssuerId,
    string ServerId, string ToolName, string Scope, string SourceDigest,
    string ApprovalEvidenceId, string RevocationPolicyId,
    bool RequiresHumanApproval, DateTimeOffset ExpiresAt);

/// <summary>An independently verified, single-invocation human approval.</summary>
public sealed record HostVerifiedMcpHumanApproval(
    string GrantId, string InvocationId, string ArgumentsDigest,
    string ApproverActorId, string EvidenceId, DateTimeOffset ExpiresAt);

public interface IHostMcpApprovalSource
{
    HostVerifiedMcpGrant? FindVerifiedGrant(VerifiedIdentity identity, McpToolInvocation request);
    bool IsRevoked(string grantId, string revocationPolicyId);
    HostVerifiedMcpHumanApproval? FindVerifiedHumanApproval(
        McpToolInvocation request, HostVerifiedMcpGrant grant);
}

/// <summary>
/// Persistently consumes an already verified human approval exactly once.
/// The real host must implement a durable atomic store; no in-memory
/// implementation is registered for execution.
/// </summary>
public interface IHostMcpApprovalUseStore
{
    bool TryConsume(HostVerifiedMcpHumanApproval approval,
        McpToolInvocation request, DateTimeOffset now);
}

/// <summary>Production and development default: nobody may invoke a candidate MCP tool.</summary>
public sealed class DenyAllHostMcpApprovalSource : IHostMcpApprovalSource
{
    public HostVerifiedMcpGrant? FindVerifiedGrant(
        VerifiedIdentity identity, McpToolInvocation request) => null;
    public bool IsRevoked(string grantId, string revocationPolicyId) => true;
    public HostVerifiedMcpHumanApproval? FindVerifiedHumanApproval(
        McpToolInvocation request, HostVerifiedMcpGrant grant) => null;
}

/// <summary>
/// Candidate call-boundary guard. It is deliberately NOT wired to an MCP
/// endpoint. Host approval and Checker evidence must be independently verified.
/// </summary>
public sealed class McpToolAuthorizationGate(
    AdmissionGate admission,
    IHostMcpApprovalSource approvals,
    IHostMcpApprovalUseStore? approvalUseStore = null)
{
    public AdmissionResult Evaluate(
        VerifiedIdentity? identity, McpToolInvocation? request,
        CheckerReceipt? independentCheckerReceipt, DateTimeOffset now)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.TrailId) ||
            string.IsNullOrWhiteSpace(request.ActorId) ||
            string.IsNullOrWhiteSpace(request.ServerId) ||
            string.IsNullOrWhiteSpace(request.ToolName) ||
            string.IsNullOrWhiteSpace(request.Scope) ||
            string.IsNullOrWhiteSpace(request.InvocationId) ||
            !IsSha256(request.SourceDigest) ||
            !IsSha256(request.ArgumentsDigest) ||
            request.ServerId.Contains('*') ||
            request.ToolName.Contains('*') ||
            request.Scope.Contains('*') ||
            request.Scope.Contains("..", StringComparison.Ordinal))
            return AdmissionResult.Deny("INVALID_MCP_TOOL_REQUEST");

        if (identity is null ||
            string.IsNullOrWhiteSpace(identity.TenantId) ||
            string.IsNullOrWhiteSpace(identity.ActorId) ||
            string.IsNullOrWhiteSpace(identity.IssuerId))
            return AdmissionResult.Deny("UNVERIFIED_IDENTITY");

        if (!string.Equals(identity.ActorId, request.ActorId, StringComparison.Ordinal))
            return AdmissionResult.Deny("ACTOR_IDENTITY_MISMATCH");

        HostVerifiedMcpHumanApproval? humanApprovalToConsume = null;
        try
        {
            HostVerifiedMcpGrant? grant = approvals.FindVerifiedGrant(identity, request);
            if (grant is null)
                return AdmissionResult.Deny("NO_HOST_APPROVED_MCP_GRANT");

            if (string.IsNullOrWhiteSpace(grant.GrantId) ||
                string.IsNullOrWhiteSpace(grant.IssuerId) ||
                string.IsNullOrWhiteSpace(grant.ApprovalEvidenceId) ||
                string.IsNullOrWhiteSpace(grant.RevocationPolicyId) ||
                grant.ExpiresAt <= now ||
                !string.Equals(grant.PrincipalActorId, identity.ActorId, StringComparison.Ordinal) ||
                !string.Equals(grant.ServerId, request.ServerId, StringComparison.Ordinal) ||
                !string.Equals(grant.ToolName, request.ToolName, StringComparison.Ordinal) ||
                !string.Equals(grant.Scope, request.Scope, StringComparison.Ordinal) ||
                !string.Equals(grant.SourceDigest, request.SourceDigest, StringComparison.Ordinal))
                return AdmissionResult.Deny("INVALID_OR_EXPIRED_HOST_GRANT");

            if (approvals.IsRevoked(grant.GrantId, grant.RevocationPolicyId))
                return AdmissionResult.Deny("REVOKED_HOST_GRANT");

            // The signed host classification cannot be downgraded by caller input.
            if (grant.RequiresHumanApproval || request.HasSideEffects)
            {
                HostVerifiedMcpHumanApproval? human =
                    approvals.FindVerifiedHumanApproval(request, grant);
                if (human is null ||
                    string.IsNullOrWhiteSpace(human.EvidenceId) ||
                    string.IsNullOrWhiteSpace(human.ApproverActorId) ||
                    string.Equals(human.ApproverActorId, request.ActorId, StringComparison.Ordinal) ||
                    human.ExpiresAt <= now ||
                    !string.Equals(human.GrantId, grant.GrantId, StringComparison.Ordinal) ||
                    !string.Equals(human.InvocationId, request.InvocationId, StringComparison.Ordinal) ||
                    !string.Equals(human.ArgumentsDigest, request.ArgumentsDigest, StringComparison.Ordinal))
                    return AdmissionResult.Deny("HUMAN_APPROVAL_REQUIRED");
                humanApprovalToConsume = human;
            }
        }
        catch
        {
            // Unavailable or failing host authentication/revocation never fails open.
            return AdmissionResult.Deny("APPROVAL_VERIFICATION_UNAVAILABLE");
        }

        // The existing capability gate still requires registration, grant,
        // digest binding, non-revocation and independently checked evidence.
        var submission = new CycleSubmission(request.TrailId,
            $"{request.ServerId}:{request.ToolName}", identity.ActorId,
            "mcp-tool-invocation");
        AdmissionResult decision = admission.Evaluate(
            submission, independentCheckerReceipt, now);
        if (!decision.Allowed || humanApprovalToConsume is null)
            return decision;

        // A real host must atomically consume a durable single-use approval.
        // Missing store, duplicate invocation and store failure all deny.
        try
        {
            if (approvalUseStore?.TryConsume(humanApprovalToConsume, request, now) != true)
                return AdmissionResult.Deny("HUMAN_APPROVAL_CONSUMPTION_UNAVAILABLE");
        }
        catch
        {
            return AdmissionResult.Deny("HUMAN_APPROVAL_CONSUMPTION_UNAVAILABLE");
        }
        return decision;
    }

    private static bool IsSha256(string? digest) =>
        digest is { Length: 71 } &&
        digest.StartsWith("sha256:", StringComparison.Ordinal) &&
        digest[7..].All(Uri.IsHexDigit);
}
