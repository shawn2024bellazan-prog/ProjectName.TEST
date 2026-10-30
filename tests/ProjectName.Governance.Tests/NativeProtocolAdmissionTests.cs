using System.Security.Claims;
using ProjectName.Contracts;
using ProjectName.ControlPlane;

namespace ProjectName.Governance.Tests;

public sealed class NativeProtocolAdmissionTests
{
    private static readonly CycleSubmission Request =
        new("TS-0002", "protocol:openai-responses", "maker-1", "test");

    private static NativeProtocolAdmission CreateGate(bool trustIdentity = false) =>
        new(new AdmissionGate(new DenyAllAuthorityRepository(), new DenyAllReceiptVerifier()),
            trustIdentity
                ? new TestVerifiedIdentityResolver()
                : new DenyAllVerifiedIdentityResolver());

    private static ClaimsPrincipal Principal(string actor) =>
        new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, actor) }, "test-identity"));

    [Fact]
    public void Anonymous_protocol_caller_is_denied()
    {
        var result = CreateGate().Evaluate(new ClaimsPrincipal(new ClaimsIdentity()),
            Request, null, DateTimeOffset.UtcNow);
        Assert.Equal("UNAUTHENTICATED", result.Reason);
    }

    [Fact]
    public void Unverified_claims_are_denied_even_if_actor_matches()
    {
        var result = CreateGate().Evaluate(Principal("maker-1"),
            Request, null, DateTimeOffset.UtcNow);
        Assert.Equal("UNVERIFIED_IDENTITY_OR_TENANT", result.Reason);
    }

    [Fact]
    public void Verified_identity_must_match_authenticated_subject_and_request_actor()
    {
        var result = CreateGate(trustIdentity: true).Evaluate(Principal("other-actor"),
            Request, null, DateTimeOffset.UtcNow);
        Assert.Equal("CALLER_ACTOR_MISMATCH", result.Reason);
    }

    [Fact]
    public void Verified_identity_still_needs_a_real_grant_and_checker()
    {
        var result = CreateGate(trustIdentity: true).Evaluate(Principal("maker-1"),
            Request, null, DateTimeOffset.UtcNow);
        Assert.Equal("UNREGISTERED_CAPABILITY", result.Reason);
    }

    [Fact]
    public void Unauthenticated_caller_cannot_launder_a_checker_receipt()
    {
        var receipt = new CheckerReceipt(
            "independent-checker", "protocol:openai-responses", "sha256:unverified",
            "EV-unverified", "unverified-signature");
        var result = CreateGate(trustIdentity: true).Evaluate(
            new ClaimsPrincipal(new ClaimsIdentity()), Request, receipt, DateTimeOffset.UtcNow);
        Assert.Equal("UNAUTHENTICATED", result.Reason);
    }

    private sealed class TestVerifiedIdentityResolver : IVerifiedIdentityResolver
    {
        public VerifiedIdentity? Resolve(ClaimsPrincipal _) =>
            new("tenant-1", "maker-1", "test-issuer");
    }
}