using ProjectName.Contracts;

namespace ProjectName.Governance;

public sealed record CapabilityRegistration(string CapabilityId, string Digest);
public sealed record EffectiveGrant(
    string ActorId, string CapabilityId, string Digest, DateTimeOffset ExpiresAt);
public sealed record CheckerReceipt(
    string CheckerId, string CapabilityId, string Digest,
    string EvidenceReference, string Signature);

public interface IAuthorityRepository
{
    CapabilityRegistration? FindRegistration(string capabilityId);
    EffectiveGrant? FindGrant(string actorId, string capabilityId);
    bool IsRevoked(string capabilityId, string digest);
}

public interface IIndependentReceiptVerifier
{
    bool Verify(CheckerReceipt receipt);
}

// The development default is intentionally closed even if an API client
// forges registration, grant or checker fields in its request.
public sealed class DenyAllAuthorityRepository : IAuthorityRepository
{
    public CapabilityRegistration? FindRegistration(string capabilityId) => null;
    public EffectiveGrant? FindGrant(string actorId, string capabilityId) => null;
    public bool IsRevoked(string capabilityId, string digest) => true;
}

public sealed class DenyAllReceiptVerifier : IIndependentReceiptVerifier
{
    public bool Verify(CheckerReceipt receipt) => false;
}
