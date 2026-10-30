using System.Security.Cryptography;

namespace ProjectName.Governance;

/// <summary>
/// Scope of a read-only authority preflight. This is not an admission request.
/// Only the host may supply authenticated evidence providers; fixture providers
/// must never be registered in the running service.
/// </summary>
public sealed record AuthorityPreflightScope(string IssuerId, string ActorId,
    string CapabilityId, string ArtifactSha256, string StorageDirectory,
    DateTimeOffset Now)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(IssuerId) && !string.IsNullOrWhiteSpace(ActorId) &&
        !string.IsNullOrWhiteSpace(CapabilityId) &&
        ArtifactSha256 is { Length: 64 } && ArtifactSha256.All(Uri.IsHexDigit) &&
        !string.IsNullOrWhiteSpace(StorageDirectory);
}

public sealed record IssuerAssertion(string IssuerId, string ActorId,
    string ArtifactSha256, DateTimeOffset ExpiresAt, string SignedEvidenceId);

public interface IHostIssuerAssertionVerifier
{
    // Implementations must verify a host-pinned signature, audience and principal.
    // A Windows account name or environment variable alone is never sufficient.
    bool Verify(IssuerAssertion assertion);
}

public interface IReadOnlyCheckerReceiptSource
{
    CheckerReceipt? Find(string capabilityId, string digest);
}

public interface IProtectedKeyProviderInspector
{
    // Inspection only: no creation, export, encryption or key access.
    bool HasHostVerifiedProvider(string issuerId);
}

public interface IRestrictedDirectoryAclInspector
{
    // Must inspect actual OS ACL and deny write access to broad principals.
    bool IsRestricted(string fullyQualifiedDirectory);
}

public sealed record AuthorityPreflightVerdict(
    bool IssuerVerified, bool RegistrationVerified, bool GrantVerified,
    bool RevocationClear, bool IndependentCheckerVerified,
    bool KeyProviderVerified, bool DirectoryAclVerified,
    bool DevelopmentOnly, bool ProductionActivationAllowed)
{
    public bool EvidenceReady => IssuerVerified && RegistrationVerified &&
        GrantVerified && RevocationClear && IndependentCheckerVerified &&
        KeyProviderVerified && DirectoryAclVerified;
}

/// <summary>
/// Evidence aggregator. The results are observations only; even an
/// EvidenceReady result cannot register or activate a capability.
/// </summary>
public sealed class ReadOnlyAuthorityPreflight(
    IHostIssuerAssertionVerifier issuers, IAuthorityRepository repository,
    IReadOnlyCheckerReceiptSource checkerReceipts,
    IIndependentReceiptVerifier checkerSignatures,
    IProtectedKeyProviderInspector keys,
    IRestrictedDirectoryAclInspector directories)
{
    public AuthorityPreflightVerdict Inspect(AuthorityPreflightScope scope,
        IssuerAssertion? assertion)
    {
        if (!scope.IsValid)
            return Denied();

        bool issuer = assertion is not null &&
            assertion.IssuerId == scope.IssuerId &&
            assertion.ActorId == scope.ActorId &&
            string.Equals(assertion.ArtifactSha256, scope.ArtifactSha256,
                StringComparison.OrdinalIgnoreCase) &&
            assertion.ExpiresAt > scope.Now &&
            !string.IsNullOrWhiteSpace(assertion.SignedEvidenceId) &&
            issuers.Verify(assertion);

        CapabilityRegistration? registration =
            repository.FindRegistration(scope.CapabilityId);
        bool registered = registration is not null &&
            string.Equals(registration.Digest, scope.ArtifactSha256,
                StringComparison.OrdinalIgnoreCase);

        EffectiveGrant? grant = repository.FindGrant(scope.ActorId, scope.CapabilityId);
        bool granted = grant is not null &&
            grant.ActorId == scope.ActorId &&
            grant.CapabilityId == scope.CapabilityId &&
            string.Equals(grant.Digest, scope.ArtifactSha256,
                StringComparison.OrdinalIgnoreCase) &&
            grant.ExpiresAt > scope.Now;

        bool revocationClear = !repository.IsRevoked(scope.CapabilityId, scope.ArtifactSha256);
        CheckerReceipt? receipt = checkerReceipts.Find(scope.CapabilityId, scope.ArtifactSha256);
        bool checker = receipt is not null &&
            receipt.CheckerId != scope.ActorId &&
            receipt.CapabilityId == scope.CapabilityId &&
            !string.IsNullOrWhiteSpace(receipt.EvidenceReference) &&
            string.Equals(receipt.Digest, scope.ArtifactSha256,
                StringComparison.OrdinalIgnoreCase) &&
            checkerSignatures.Verify(receipt);

        return new(issuer, registered, granted, revocationClear, checker,
            keys.HasHostVerifiedProvider(scope.IssuerId),
            directories.IsRestricted(scope.StorageDirectory),
            DevelopmentOnly: true, ProductionActivationAllowed: false);
    }

    private static AuthorityPreflightVerdict Denied() =>
        new(false, false, false, false, false, false, false, true, false);
}
