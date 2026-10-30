using ProjectName.Contracts;
using ProjectName.ControlPlane;
using ProjectName.Governance;

namespace ProjectName.Governance.Tests;

public sealed class McpToolAuthorizationGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Source = "sha256:" + new string('a', 64);
    private static readonly string Arguments = "sha256:" + new string('b', 64);
    private const string Capability = "mcp-filesystem:desktop-commander__read_file";
    private static readonly VerifiedIdentity Identity =
        new("local-dev", "windows-actor-1", "windows-negotiate-local");

    private static McpToolInvocation Request(bool sideEffects = false) => new(
        "TS-TEST", Identity.ActorId, "mcp-filesystem",
        "desktop-commander__read_file", "G:/ProjectName/docs",
        Source, Arguments, "invocation-1", sideEffects);

    private static HostVerifiedMcpGrant Grant(bool requiresHumanApproval = false) => new(
        "host-grant-1", Identity.ActorId, "windows-host-approval",
        "mcp-filesystem", "desktop-commander__read_file",
        "G:/ProjectName/docs", Source, "EV-HOST", "revocations:v1",
        requiresHumanApproval, Now.AddMinutes(10));

    private static CheckerReceipt Receipt() => new(
        "independent-test-checker", Capability, Source, "EV-TEST", "fixture-signature");

    private static McpToolAuthorizationGate Gate(
        IHostMcpApprovalSource? host = null, bool registered = false,
        IHostMcpApprovalUseStore? useStore = null) => new(
            new AdmissionGate(
                registered ? new TestAuthority() : new DenyAllAuthorityRepository(),
                registered ? new TestReceiptVerifier() : new DenyAllReceiptVerifier()),
            host ?? new DenyAllHostMcpApprovalSource(), useStore);

    [Fact]
    public void No_host_verified_approval_is_denied()
    {
        var result = Gate().Evaluate(Identity, Request(), Receipt(), Now);
        Assert.Equal("NO_HOST_APPROVED_MCP_GRANT", result.Reason);
        Assert.False(result.Allowed);
    }

    [Fact]
    public void Missing_identity_is_denied()
    {
        Assert.Equal("UNVERIFIED_IDENTITY",
            Gate().Evaluate(null, Request(), Receipt(), Now).Reason);
    }

    [Fact]
    public void Spoofed_actor_is_denied_before_approval_lookup()
    {
        var request = Request() with { ActorId = "different-actor" };
        Assert.Equal("ACTOR_IDENTITY_MISMATCH",
            Gate().Evaluate(Identity, request, Receipt(), Now).Reason);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("../secrets")]
    public void Wildcards_and_traversal_scopes_are_denied(string scope)
    {
        var request = Request() with { Scope = scope };
        Assert.Equal("INVALID_MCP_TOOL_REQUEST",
            Gate().Evaluate(Identity, request, Receipt(), Now).Reason);
    }

    [Fact]
    public void Unbound_grant_does_not_authorize_wrong_tool()
    {
        var host = new TestHost { VerifiedGrant = Grant() with { ToolName = "write_file" } };
        Assert.Equal("INVALID_OR_EXPIRED_HOST_GRANT",
            Gate(host).Evaluate(Identity, Request(), Receipt(), Now).Reason);
    }

    [Fact]
    public void Expired_grant_is_denied()
    {
        var host = new TestHost { VerifiedGrant = Grant() with { ExpiresAt = Now } };
        Assert.Equal("INVALID_OR_EXPIRED_HOST_GRANT",
            Gate(host).Evaluate(Identity, Request(), Receipt(), Now).Reason);
    }

    [Fact]
    public void Revoked_grant_is_denied()
    {
        var host = new TestHost { VerifiedGrant = Grant(), Revoked = true };
        Assert.Equal("REVOKED_HOST_GRANT",
            Gate(host).Evaluate(Identity, Request(), Receipt(), Now).Reason);
    }

    [Fact]
    public void Host_verifier_failure_denies_closed()
    {
        var host = new TestHost { Throws = true };
        Assert.Equal("APPROVAL_VERIFICATION_UNAVAILABLE",
            Gate(host).Evaluate(Identity, Request(), Receipt(), Now).Reason);
    }

    [Fact]
    public void Read_only_grant_still_requires_registration_and_checker()
    {
        var host = new TestHost { VerifiedGrant = Grant() };
        Assert.Equal("UNREGISTERED_CAPABILITY",
            Gate(host).Evaluate(Identity, Request(), Receipt(), Now).Reason);
        Assert.Equal("NO_INDEPENDENT_CHECKER_PASS",
            Gate(host, registered: true).Evaluate(Identity, Request(), null, Now).Reason);
    }

    [Fact]
    public void Signed_host_side_effect_classification_cannot_be_downgraded()
    {
        var host = new TestHost { VerifiedGrant = Grant(requiresHumanApproval: true) };
        Assert.Equal("HUMAN_APPROVAL_REQUIRED",
            Gate(host, registered: true).Evaluate(
                Identity, Request(sideEffects: false), Receipt(), Now).Reason);
    }

    [Fact]
    public void Side_effecting_call_requires_separate_human_approval()
    {
        var host = new TestHost { VerifiedGrant = Grant() };
        Assert.Equal("HUMAN_APPROVAL_REQUIRED",
            Gate(host, registered: true).Evaluate(
                Identity, Request(sideEffects: true), Receipt(), Now).Reason);
    }

    [Fact]
    public void Human_approval_must_bind_exact_arguments_and_principal()
    {
        var host = new TestHost
        {
            VerifiedGrant = Grant(),
            VerifiedHuman = new("host-grant-1", "invocation-1",
                "sha256:" + new string('f', 64), "human-2", "EV-HUMAN", Now.AddMinutes(1))
        };
        Assert.Equal("HUMAN_APPROVAL_REQUIRED",
            Gate(host, registered: true).Evaluate(
                Identity, Request(sideEffects: true), Receipt(), Now).Reason);
        host.VerifiedHuman = host.VerifiedHuman with { ArgumentsDigest = Arguments, ApproverActorId = Identity.ActorId };
        Assert.Equal("HUMAN_APPROVAL_REQUIRED",
            Gate(host, registered: true).Evaluate(
                Identity, Request(sideEffects: true), Receipt(), Now).Reason);
    }

    [Fact]
    public void All_predicates_can_pass_only_in_explicit_test_doubles()
    {
        var host = new TestHost
        {
            VerifiedGrant = Grant(),
            VerifiedHuman = new("host-grant-1", "invocation-1",
                Arguments, "separate-human-2", "EV-HUMAN", Now.AddMinutes(1))
        };
        Assert.True(Gate(host, registered: true).Evaluate(
            Identity, Request(), Receipt(), Now).Allowed);
        Assert.True(Gate(host, registered: true, useStore: new TestApprovalUseStore()).Evaluate(
            Identity, Request(sideEffects: true), Receipt(), Now).Allowed);
        // No TestHost or TestAuthority is registered in any application.
    }

    [Fact]
    public void Human_approval_without_a_consumption_store_is_denied()
    {
        var host = new TestHost
        {
            VerifiedGrant = Grant(requiresHumanApproval: true),
            VerifiedHuman = new("host-grant-1", "invocation-1",
                Arguments, "separate-human-2", "EV-HUMAN", Now.AddMinutes(1))
        };
        Assert.Equal("HUMAN_APPROVAL_CONSUMPTION_UNAVAILABLE",
            Gate(host, registered: true).Evaluate(Identity, Request(), Receipt(), Now).Reason);
    }

    [Fact]
    public void Approval_cannot_be_reused_in_test_only_atomic_store()
    {
        var host = new TestHost
        {
            VerifiedGrant = Grant(requiresHumanApproval: true),
            VerifiedHuman = new("host-grant-1", "invocation-1",
                Arguments, "separate-human-2", "EV-HUMAN", Now.AddMinutes(1))
        };
        var gate = Gate(host, registered: true, useStore: new TestApprovalUseStore());
        Assert.True(gate.Evaluate(Identity, Request(), Receipt(), Now).Allowed);
        Assert.Equal("HUMAN_APPROVAL_CONSUMPTION_UNAVAILABLE",
            gate.Evaluate(Identity, Request(), Receipt(), Now).Reason);
    }

    private sealed class TestApprovalUseStore : IHostMcpApprovalUseStore
    {
        private readonly HashSet<string> consumed = new(StringComparer.Ordinal);

        public bool TryConsume(HostVerifiedMcpHumanApproval approval,
            McpToolInvocation request, DateTimeOffset now)
        {
            lock (consumed)
                return consumed.Add(approval.GrantId + ":" + approval.InvocationId);
        }
    }

    private sealed class TestHost : IHostMcpApprovalSource
    {
        public HostVerifiedMcpGrant? VerifiedGrant { get; set; }
        public HostVerifiedMcpHumanApproval? VerifiedHuman { get; set; }
        public bool Revoked { get; set; }
        public bool Throws { get; set; }

        public HostVerifiedMcpGrant? FindVerifiedGrant(VerifiedIdentity identity, McpToolInvocation request) =>
            Throws ? throw new InvalidOperationException("test-unavailable") : VerifiedGrant;
        public bool IsRevoked(string grantId, string revocationPolicyId) => Revoked;
        public HostVerifiedMcpHumanApproval? FindVerifiedHumanApproval(
            McpToolInvocation request, HostVerifiedMcpGrant grant) => VerifiedHuman;
    }

    private sealed class TestAuthority : IAuthorityRepository
    {
        public CapabilityRegistration? FindRegistration(string id) =>
            id == Capability ? new(id, Source) : null;
        public EffectiveGrant? FindGrant(string actor, string capability) =>
            actor == Identity.ActorId && capability == Capability
                ? new(actor, capability, Source, Now.AddHours(1)) : null;
        public bool IsRevoked(string capabilityId, string digest) => false;
    }

    private sealed class TestReceiptVerifier : IIndependentReceiptVerifier
    {
        public bool Verify(CheckerReceipt receipt) =>
            receipt.CheckerId == "independent-test-checker" &&
            receipt.Signature == "fixture-signature";
    }
}
