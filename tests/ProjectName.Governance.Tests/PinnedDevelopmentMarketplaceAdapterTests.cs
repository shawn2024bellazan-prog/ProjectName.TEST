using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectName.Contracts;
using ProjectName.ControlPlane;
using ProjectName.Governance;

namespace ProjectName.Governance.Tests;

public sealed class PinnedDevelopmentMarketplaceAdapterTests
{
    private const string Version = "0.3.0-dev";
    private const string CatalogSha256 =
        "2d2c63cdee1aeb189db33bf68784b0a1e0f9288a82cca7b8a60efa9170650d5e";
    private static readonly string Fixture = Path.Combine(
        AppContext.BaseDirectory, "fixtures", "pmcro-marketplace-v2-0.3.0-dev.json");

    private static byte[] Canonical() => File.ReadAllBytes(Fixture);
    private static PinnedDevelopmentMarketplaceAdapter Adapter(string pin = CatalogSha256) =>
        new(Version, pin);
    private static string Pin(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static byte[] Change(string original, string replacement)
    {
        string text = Encoding.UTF8.GetString(Canonical());
        Assert.Contains(original, text, StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(text.Replace(original, replacement, StringComparison.Ordinal));
    }
    private static MarketplacePreflightVerdict Inspect(byte[] bytes, string capabilityId = "social:publish") =>
        Adapter(Pin(bytes)).Inspect(bytes, capabilityId);

    [Fact]
    public void Pin_manifest_binds_exact_external_marketplace_source_and_deny_scope()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "adapter.pin.json");
        using JsonDocument pin = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = pin.RootElement;
        Assert.Equal("projectname.marketplace-adapter-pin/v1", root.GetProperty("schema").GetString());
        Assert.Equal("2b4608416f9e4cf8742788154c9428d2884df5e8",
            root.GetProperty("sourceCommit").GetString());
        Assert.Equal("62f87721c72f36be64971fbb1bc9c3e49f982373783209b314704e65efab1408",
            root.GetProperty("sourceManifestSha256").GetString());
        Assert.Equal(Version, root.GetProperty("catalogVersion").GetString());
        Assert.Equal(CatalogSha256, root.GetProperty("catalogSha256").GetString());
        Assert.Equal("TEST_ONLY_DENY", root.GetProperty("integrationMode").GetString());
        Assert.Empty(root.GetProperty("allowedRuntimeCapabilityIds").EnumerateArray());
        Assert.False(root.GetProperty("hostTrustRootConfigured").GetBoolean());
        Assert.False(root.GetProperty("independentCheckerPass").GetBoolean());
    }

    [Fact]
    public void Fixture_matches_pinned_marketplace_commit_and_denies_missing_capability()
    {
        byte[] bytes = Canonical();
        Assert.Equal(CatalogSha256, Pin(bytes));
        var result = Adapter().Inspect(bytes, "social:publish");
        Assert.False(result.Admitted);
        Assert.Equal("CAPABILITY_NOT_DECLARED", result.Reason);
    }

    [Fact]
    public void Primordial_create_skill_is_not_executable_admission()
    {
        var result = Adapter().Inspect(Canonical(), "pmcro-bootstrap:create-skill");
        Assert.False(result.Admitted);
        Assert.Equal("PRE_INGRESS_NOT_EXECUTABLE", result.Reason);
    }

    [Fact]
    public void Rejects_missing_or_malformed_pins()
    {
        Assert.Equal("PIN_INVALID", Adapter("wrong").Inspect(Canonical(), "social:publish").Reason);
        Assert.Equal("CATALOG_DIGEST_MISMATCH", Adapter(new string('a', 64))
            .Inspect(Canonical(), "social:publish").Reason);
    }

    [Fact]
    public void Rejects_invalid_request_and_size()
    {
        Assert.Equal("INVALID_REQUEST", Adapter().Inspect(Canonical(), "").Reason);
        Assert.Equal("CATALOG_SIZE_INVALID", Adapter().Inspect(Array.Empty<byte>(), "social:publish").Reason);
        Assert.Equal("CATALOG_SIZE_INVALID",
            Adapter().Inspect(new byte[PinnedDevelopmentMarketplaceAdapter.MaximumCatalogBytes + 1],
                "social:publish").Reason);
    }

    [Fact]
    public void Rejects_syntactically_invalid_even_when_digest_matches()
    {
        Assert.Equal("CATALOG_JSON_INVALID", Inspect(Encoding.UTF8.GetBytes("{")).Reason);
        Assert.Equal("CATALOG_AMBIGUOUS_OR_INVALID", Inspect(Encoding.UTF8.GetBytes("[]")).Reason);
    }

    [Theory]
    [InlineData("\"developmentAdmission\": \"DENY\"", "\"developmentAdmission\": \"ALLOW\"", "CATALOG_POLICY_INVALID")]
    [InlineData("\"productionAdmission\": \"DENY\"", "\"productionAdmission\": \"ALLOW\"", "CATALOG_POLICY_INVALID")]
    [InlineData("\"state\": \"DRAFT_UNREGISTERED\"", "\"state\": \"ACTIVE\"", "CATALOG_POLICY_INVALID")]
    [InlineData("\"nextSeedExecuted\": false", "\"nextSeedExecuted\": true", "CATALOG_POLICY_INVALID")]
    [InlineData("\"independentIdentityVerified\": false", "\"independentIdentityVerified\": true", "UNTRUSTED_AUTHORITY_CLAIM")]
    [InlineData("\"hostTrustedVerifierInstalled\": false", "\"hostTrustedVerifierInstalled\": true", "UNTRUSTED_AUTHORITY_CLAIM")]
    [InlineData("\"mayReturnAllow\": false", "\"mayReturnAllow\": true", "UNTRUSTED_AUTHORITY_CLAIM")]
    [InlineData("\"hostTrustRootConfigured\": false", "\"hostTrustRootConfigured\": true", "UNTRUSTED_AUTHORITY_CLAIM")]
    [InlineData("\"mayIssueGrants\": false", "\"mayIssueGrants\": true", "UNTRUSTED_AUTHORITY_CLAIM")]
    [InlineData("\"mayActivateWorkers\": false", "\"mayActivateWorkers\": true", "UNTRUSTED_AUTHORITY_CLAIM")]
    [InlineData("\"granted\": false", "\"granted\": true", "PRIMORDIAL_AUTHORITY_DENY")]
    [InlineData("\"activated\": false", "\"activated\": true", "PRIMORDIAL_AUTHORITY_DENY")]
    [InlineData("\"capabilities\": []", "\"capabilities\": [{\"id\":\"social:publish\"}]", "UNTRUSTED_RUNTIME_CAPABILITIES")]
    public void Rejects_privilege_injections_even_if_attacker_changes_pin(
        string before, string after, string expectedReason)
    {
        byte[] bytes = Change(before, after);
        var result = Inspect(bytes);
        Assert.False(result.Admitted);
        Assert.Equal(expectedReason, result.Reason);
    }

    [Fact]
    public void Rejects_duplicate_json_properties_even_if_rehashed()
    {
        byte[] bytes = Change("\"productionAdmission\": \"DENY\"",
            "\"productionAdmission\": \"DENY\", \"productionAdmission\": \"DENY\"");
        Assert.Equal("CATALOG_AMBIGUOUS_OR_INVALID", Inspect(bytes).Reason);
    }

    [Fact]
    public void Rejects_phase_sequence_tampering_even_if_rehashed()
    {
        byte[] bytes = Change("\"ORCHESTRATE\"", "\"PLAN\"");
        Assert.Equal("CATALOG_POLICY_INVALID", Inspect(bytes).Reason);
    }

    [Fact]
    public void Test_double_grant_cannot_bypass_marketplace_contract()
    {
        var host = new AdmissionGate(new PermissiveTestAuthority(), new PermissiveTestVerifier());
        var preflight = new DevelopmentMarketplaceAdmission(Adapter(), host);
        var result = preflight.Evaluate(Canonical(),
            new CycleSubmission("TS-1000", "social:publish", "maker-1", "test"),
            new CheckerReceipt("checker-2", "social:publish", "sha256:test", "EV-1", "signed"),
            DateTimeOffset.UtcNow);
        Assert.False(result.Allowed);
        Assert.Equal("MARKETPLACE_CAPABILITY_NOT_DECLARED", result.Reason);
    }

    private sealed class PermissiveTestAuthority : IAuthorityRepository
    {
        public CapabilityRegistration? FindRegistration(string capabilityId) =>
            new(capabilityId, "sha256:test");
        public EffectiveGrant? FindGrant(string actorId, string capabilityId) =>
            new(actorId, capabilityId, "sha256:test", DateTimeOffset.UtcNow.AddHours(1));
        public bool IsRevoked(string capabilityId, string digest) => false;
    }
    private sealed class PermissiveTestVerifier : IIndependentReceiptVerifier
    {
        public bool Verify(CheckerReceipt receipt) => true;
    }
}
