using ProjectName.Contracts;
using ProjectName.Governance;
using ProjectName.ControlPlane;

namespace ProjectName.Governance.Tests;

public sealed class AdmissionGateTests
{
    private static readonly CycleSubmission Request = new(
        "TS-0001", "capability:example", "maker-1", "test");

    [Fact]
    public void Denies_without_host_authority()
    {
        var gate = new AdmissionGate(new DenyAllAuthorityRepository(), new DenyAllReceiptVerifier());
        var result = gate.Evaluate(Request, null, DateTimeOffset.UtcNow);
        Assert.False(result.Allowed);
        Assert.Equal("UNREGISTERED_CAPABILITY", result.Reason);
    }

    [Fact]
    public void Denies_self_approved_receipt()
    {
        var gate = new AdmissionGate(new TestAuthority(), new TestVerifier());
        var result = gate.Evaluate(Request,
            new CheckerReceipt("maker-1", "capability:example", "sha256:test", "EV-1", "signed"),
            DateTimeOffset.UtcNow);
        Assert.Equal("NO_INDEPENDENT_CHECKER_PASS", result.Reason);
    }

    [Fact]
    public void Denies_missing_verification_even_with_grant()
    {
        var gate = new AdmissionGate(new TestAuthority(), new DenyAllReceiptVerifier());
        var result = gate.Evaluate(Request,
            new CheckerReceipt("checker-2", "capability:example", "sha256:test", "EV-1", "signed"),
            DateTimeOffset.UtcNow);
        Assert.False(result.Allowed);
    }

    [Fact]
    public void Admits_only_with_all_predicates_in_test_double()
    {
        var gate = new AdmissionGate(new TestAuthority(), new TestVerifier());
        var result = gate.Evaluate(Request,
            new CheckerReceipt("checker-2", "capability:example", "sha256:test", "EV-1", "signed"),
            DateTimeOffset.UtcNow);
        Assert.True(result.Allowed);
        Assert.Equal("sha256:test", result.BoundDigest);
    }

    private sealed class TestAuthority : IAuthorityRepository
    {
        public CapabilityRegistration? FindRegistration(string id) => new(id, "sha256:test");
        public EffectiveGrant? FindGrant(string actor, string cap) =>
            new(actor, cap, "sha256:test", DateTimeOffset.UtcNow.AddHours(1));
        public bool IsRevoked(string capabilityId, string digest) => false;
    }

    private sealed class TestVerifier : IIndependentReceiptVerifier
    {
        public bool Verify(CheckerReceipt receipt) => receipt.CheckerId == "checker-2";
    }
}
