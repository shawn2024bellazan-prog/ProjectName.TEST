using ProjectName.Contracts;
using ProjectName.Governance;

namespace ProjectName.ControlPlane;

public sealed class AdmissionGate(
    IAuthorityRepository authority,
    IIndependentReceiptVerifier receiptVerifier)
{
    public AdmissionResult Evaluate(
        CycleSubmission request,
        CheckerReceipt? receipt,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(request.TrailId) ||
            string.IsNullOrWhiteSpace(request.CapabilityId) ||
            string.IsNullOrWhiteSpace(request.ActorId))
            return AdmissionResult.Deny("INVALID_REQUEST");

        CapabilityRegistration? registration = authority.FindRegistration(request.CapabilityId);
        if (registration is null)
            return AdmissionResult.Deny("UNREGISTERED_CAPABILITY");

        if (string.IsNullOrWhiteSpace(registration.Digest) ||
            authority.IsRevoked(registration.CapabilityId, registration.Digest))
            return AdmissionResult.Deny("REVOKED_OR_UNBOUND");

        EffectiveGrant? grant = authority.FindGrant(request.ActorId, request.CapabilityId);
        if (grant is null || grant.ExpiresAt <= now ||
            !string.Equals(grant.Digest, registration.Digest, StringComparison.Ordinal))
            return AdmissionResult.Deny("NO_EFFECTIVE_GRANT");

        if (receipt is null ||
            string.IsNullOrWhiteSpace(receipt.Signature) ||
            string.IsNullOrWhiteSpace(receipt.EvidenceReference) ||
            string.Equals(receipt.CheckerId, request.ActorId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(receipt.CapabilityId, registration.CapabilityId, StringComparison.Ordinal) ||
            !string.Equals(receipt.Digest, registration.Digest, StringComparison.Ordinal) ||
            !receiptVerifier.Verify(receipt))
            return AdmissionResult.Deny("NO_INDEPENDENT_CHECKER_PASS");

        return new AdmissionResult(true, "ADMITTED", registration.Digest);
    }
}
