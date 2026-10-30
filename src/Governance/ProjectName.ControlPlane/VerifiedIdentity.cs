using System.Security.Claims;

namespace ProjectName.ControlPlane;

/// <summary>Only a configured, validating host authentication adapter may issue this.</summary>
public sealed record VerifiedIdentity(string TenantId, string ActorId, string IssuerId);

public interface IVerifiedIdentityResolver
{
    VerifiedIdentity? Resolve(ClaimsPrincipal authenticatedPrincipal);
}

/// <summary>No OIDC/JWT validator or issuing authority is configured in the candidate.</summary>
public sealed class DenyAllVerifiedIdentityResolver : IVerifiedIdentityResolver
{
    public VerifiedIdentity? Resolve(ClaimsPrincipal authenticatedPrincipal) => null;
}