using ProjectName.Governance;

namespace ProjectName.Governance.Tests;

public sealed class ReadOnlyAuthorityPreflightTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Digest = new('a', 64);
    private static readonly AuthorityPreflightScope Scope =
        new("issuer-a", "maker-a", "checkpoint-candidate", Digest,
            @"G:\ProjectName\protected-state", Now);
    private static readonly IssuerAssertion Assertion =
        new("issuer-a", "maker-a", Digest, Now.AddMinutes(5), "signed-issuer-1");

    [Fact]
    public void Missing_host_evidence_never_admits_production()
    {
        var result = Build(issuer: false, registration: false, grant: false,
            revoked: true, checker: false, kms: false, acl: false)
            .Inspect(Scope, null);
        Assert.False(result.EvidenceReady);
        Assert.False(result.ProductionActivationAllowed);
        Assert.True(result.DevelopmentOnly);
        Assert.False(result.IssuerVerified);
        Assert.False(result.IndependentCheckerVerified);
    }

    [Fact]
    public void Host_proof_must_bind_exact_issuer_actor_artifact_and_expiry()
    {
        var preflight = Build();
        Assert.False(preflight.Inspect(Scope, Assertion with { IssuerId = "other" })
            .IssuerVerified);
        Assert.False(preflight.Inspect(Scope, Assertion with { ActorId = "other" })
            .IssuerVerified);
        Assert.False(preflight.Inspect(Scope, Assertion with { ArtifactSha256 = new('b', 64) })
            .IssuerVerified);
        Assert.False(preflight.Inspect(Scope, Assertion with { ExpiresAt = Now })
            .IssuerVerified);
        Assert.False(preflight.Inspect(Scope, Assertion with { SignedEvidenceId = "" })
            .IssuerVerified);
    }

    [Fact]
    public void Stale_or_revoked_grant_prevents_readiness_even_with_signature()
    {
        Assert.False(Build(grant: false).Inspect(Scope, Assertion).EvidenceReady);
        Assert.False(Build(revoked: true).Inspect(Scope, Assertion).EvidenceReady);
        Assert.False(Build(registration: false).Inspect(Scope, Assertion).EvidenceReady);
    }

    [Fact]
    public void Checker_must_be_independent_and_bound_to_exact_digest()
    {
        var missing = Build(checker: false).Inspect(Scope, Assertion);
        Assert.False(missing.IndependentCheckerVerified);

        var forged = Build(checkerId: "maker-a").Inspect(Scope, Assertion);
        Assert.False(forged.IndependentCheckerVerified);

        var mismatch = Build(checkerDigest: new('c', 64)).Inspect(Scope, Assertion);
        Assert.False(mismatch.IndependentCheckerVerified);
    }

    [Fact]
    public void Kms_and_acl_are_separate_required_read_only_checks()
    {
        Assert.False(Build(kms: false).Inspect(Scope, Assertion).EvidenceReady);
        Assert.False(Build(acl: false).Inspect(Scope, Assertion).EvidenceReady);
    }

    [Fact]
    public void Even_fully_satisfied_test_fixtures_cannot_activate_production()
    {
        // Everything in this test is a fixture, NOT an authenticated host assertion.
        var result = Build().Inspect(Scope, Assertion);
        Assert.True(result.EvidenceReady);
        Assert.True(result.DevelopmentOnly);
        Assert.False(result.ProductionActivationAllowed);
    }

    private static ReadOnlyAuthorityPreflight Build(
        bool issuer = true, bool registration = true, bool grant = true,
        bool revoked = false, bool checker = true, bool kms = true,
        bool acl = true, string checkerId = "checker-b", string? checkerDigest = null)
    {
        var repo = new FixtureRepository(registration, grant, revoked);
        return new(new FixtureIssuer(issuer), repo,
            new FixtureReceipt(checkerId, checkerDigest ?? Digest),
            new FixtureSignature(checker), new FixtureKey(kms), new FixtureAcl(acl));
    }

    private sealed class FixtureIssuer(bool verified) : IHostIssuerAssertionVerifier
    {
        public bool Verify(IssuerAssertion assertion) => verified;
    }

    private sealed class FixtureRepository(bool registered, bool granted, bool revoked) :
        IAuthorityRepository
    {
        public CapabilityRegistration? FindRegistration(string capabilityId) =>
            registered ? new(capabilityId, Digest) : null;
        public EffectiveGrant? FindGrant(string actorId, string capabilityId) =>
            granted ? new(actorId, capabilityId, Digest, Now.AddMinutes(5)) : null;
        public bool IsRevoked(string capabilityId, string digest) => revoked;
    }

    private sealed class FixtureReceipt(string checkerId, string digest) :
        IReadOnlyCheckerReceiptSource
    {
        public CheckerReceipt? Find(string capabilityId, string expectedDigest) =>
            new(checkerId, capabilityId, digest, "EV-fixture", "fixture-signature");
    }

    private sealed class FixtureSignature(bool verified) : IIndependentReceiptVerifier
    {
        public bool Verify(CheckerReceipt receipt) => verified;
    }

    private sealed class FixtureKey(bool verified) : IProtectedKeyProviderInspector
    {
        public bool HasHostVerifiedProvider(string issuerId) => verified;
    }

    private sealed class FixtureAcl(bool verified) : IRestrictedDirectoryAclInspector
    {
        public bool IsRestricted(string directory) => verified;
    }
}
