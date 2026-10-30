using System.Security.Claims;
using ProjectName.Contracts;
using ProjectName.Governance;

namespace ProjectName.ControlPlane;

/// <summary>
/// Trust boundary shared by future MAF-native HTTP adapters. A ClaimsPrincipal
/// alone is not proof that a configured issuer authenticated the caller.
/// </summary>
public sealed class NativeProtocolAdmission(
    AdmissionGate gate,
    IVerifiedIdentityResolver trustedIdentity)
{
    public AdmissionResult Evaluate(
        ClaimsPrincipal principal,
        CycleSubmission request,
        CheckerReceipt? independentReceipt,
        DateTimeOffset now)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return AdmissionResult.Deny("UNAUTHENTICATED");

        VerifiedIdentity? identity = trustedIdentity.Resolve(principal);
        if (identity is null ||
            string.IsNullOrWhiteSpace(identity.TenantId) ||
            string.IsNullOrWhiteSpace(identity.ActorId) ||
            string.IsNullOrWhiteSpace(identity.IssuerId))
            return AdmissionResult.Deny("UNVERIFIED_IDENTITY_OR_TENANT");

        string? authenticatedSubject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.Equals(identity.ActorId, request.ActorId, StringComparison.Ordinal) ||
            !string.Equals(identity.ActorId, authenticatedSubject, StringComparison.Ordinal))
            return AdmissionResult.Deny("CALLER_ACTOR_MISMATCH");

        // Effective registration, grant, digest, revocation and independently
        // verified signed Checker receipt remain mandatory after identity proof.
        return gate.Evaluate(request, independentReceipt, now);
    }
}