using System.Security.Cryptography;
using System.Text.Json;
using ProjectName.Contracts;
using ProjectName.Governance;

namespace ProjectName.ControlPlane;

// Development-only contract reader for a pinned marketplace projection.
// A catalog, even when digest-matched, cannot issue a host grant or activate a worker.
public sealed record MarketplacePreflightVerdict(bool Admitted, string Reason, string? CatalogDigest)
{
    public static MarketplacePreflightVerdict Deny(string reason) => new(false, reason, null);
}

public sealed class PinnedDevelopmentMarketplaceAdapter(string version, string expectedCatalogSha256)
{
    public const int MaximumCatalogBytes = 256 * 1024;

    public MarketplacePreflightVerdict Inspect(ReadOnlyMemory<byte> catalogBytes, string? capabilityId)
    {
        if (string.IsNullOrWhiteSpace(capabilityId))
            return MarketplacePreflightVerdict.Deny("INVALID_REQUEST");
        if (catalogBytes.IsEmpty || catalogBytes.Length > MaximumCatalogBytes)
            return MarketplacePreflightVerdict.Deny("CATALOG_SIZE_INVALID");
        if (!TryParseDigest(expectedCatalogSha256, out byte[] expected))
            return MarketplacePreflightVerdict.Deny("PIN_INVALID");

        byte[] actual = SHA256.HashData(catalogBytes.Span);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            return MarketplacePreflightVerdict.Deny("CATALOG_DIGEST_MISMATCH");

        try
        {
            using JsonDocument document = JsonDocument.Parse(catalogBytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 24
            });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root))
                return MarketplacePreflightVerdict.Deny("CATALOG_AMBIGUOUS_OR_INVALID");

            if (!IsString(root, "schema", "pmcro.marketplace-index/v1") ||
                !IsString(root, "version", version) ||
                !IsString(root, "state", "DRAFT_UNREGISTERED") ||
                !IsString(root, "productionAdmission", "DENY") ||
                !IsString(root, "developmentAdmission", "DENY") ||
                !IsFalse(root, "nextSeedExecuted") ||
                !HasCanonicalPhaseOrder(root))
                return MarketplacePreflightVerdict.Deny("CATALOG_POLICY_INVALID");

            if (!TryObject(root, "checker", out JsonElement checker) ||
                !IsFalse(checker, "independentIdentityVerified") ||
                !IsNull(checker, "verdict") ||
                !TryObject(root, "admissionGate", out JsonElement gate) ||
                !IsString(gate, "state", "DRAFT_DENY_ONLY") ||
                !IsFalse(gate, "hostTrustedVerifierInstalled") ||
                !IsFalse(gate, "mayReturnAllow") ||
                !TryObject(root, "trustAdapter", out JsonElement trust) ||
                !IsString(trust, "state", "DRAFT_UNTRUSTED") ||
                !IsFalse(trust, "hostTrustRootConfigured") ||
                !IsFalse(trust, "mayIssueGrants") ||
                !IsFalse(trust, "mayActivateWorkers"))
                return MarketplacePreflightVerdict.Deny("UNTRUSTED_AUTHORITY_CLAIM");

            if (!root.TryGetProperty("primordialCapabilities", out JsonElement primordial) ||
                primordial.ValueKind != JsonValueKind.Array || primordial.GetArrayLength() != 1)
                return MarketplacePreflightVerdict.Deny("PRIMORDIAL_CONTRACT_INVALID");
            JsonElement constructor = primordial[0];
            if (constructor.ValueKind != JsonValueKind.Object ||
                !IsString(constructor, "id", "pmcro-bootstrap:create-skill") ||
                !IsString(constructor, "mode", "PRE_INGRESS_AUTHORING_ONLY") ||
                !IsFalse(constructor, "registered") ||
                !IsFalse(constructor, "granted") ||
                !IsFalse(constructor, "activated") ||
                !IsFalse(constructor, "executionAuthorized"))
                return MarketplacePreflightVerdict.Deny("PRIMORDIAL_AUTHORITY_DENY");

            if (!root.TryGetProperty("capabilities", out JsonElement capabilities) ||
                capabilities.ValueKind != JsonValueKind.Array)
                return MarketplacePreflightVerdict.Deny("CAPABILITY_CATALOG_INVALID");
            if (capabilities.GetArrayLength() != 0)
                return MarketplacePreflightVerdict.Deny("UNTRUSTED_RUNTIME_CAPABILITIES");

            // Pre-ingress creation is not an executable marketplace admission.
            if (string.Equals(capabilityId, "pmcro-bootstrap:create-skill", StringComparison.Ordinal))
                return MarketplacePreflightVerdict.Deny("PRE_INGRESS_NOT_EXECUTABLE");

            return MarketplacePreflightVerdict.Deny("CAPABILITY_NOT_DECLARED");
        }
        catch (JsonException)
        {
            return MarketplacePreflightVerdict.Deny("CATALOG_JSON_INVALID");
        }
        catch (InvalidOperationException)
        {
            return MarketplacePreflightVerdict.Deny("CATALOG_STRUCTURE_INVALID");
        }
    }

    private static bool TryParseDigest(string? hex, out byte[] digest)
    {
        digest = [];
        if (hex is null || hex.Length != 64) return false;
        try { digest = Convert.FromHexString(hex); return digest.Length == 32; }
        catch (FormatException) { return false; }
    }

    private static bool TryObject(JsonElement parent, string name, out JsonElement result)
    {
        result = default;
        return parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(name, out result) &&
            result.ValueKind == JsonValueKind.Object;
    }

    private static bool IsString(JsonElement parent, string name, string expected) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out JsonElement result) &&
        result.ValueKind == JsonValueKind.String &&
        string.Equals(result.GetString(), expected, StringComparison.Ordinal);

    private static bool IsFalse(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out JsonElement result) &&
        result.ValueKind == JsonValueKind.False;

    private static bool IsNull(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out JsonElement result) &&
        result.ValueKind == JsonValueKind.Null;

    private static bool HasCanonicalPhaseOrder(JsonElement root)
    {
        if (!root.TryGetProperty("phaseOrder", out JsonElement phases) ||
            phases.ValueKind != JsonValueKind.Array || phases.GetArrayLength() != 5)
            return false;
        string[] expected = ["ORCHESTRATE", "PLAN", "MAKE", "CHECK", "REFLECT"];
        return phases.EnumerateArray().Select((phase, index) =>
            phase.ValueKind == JsonValueKind.String &&
            string.Equals(phase.GetString(), expected[index], StringComparison.Ordinal)).All(x => x);
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name) || HasDuplicateProperties(property.Value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
                if (HasDuplicateProperties(item)) return true;
        }
        return false;
    }
}

// The Development composition proves the host gate cannot be reached through an
// unverified marketplace catalog. It is deliberately not registered in live ingress.
public sealed class DevelopmentMarketplaceAdmission(
    PinnedDevelopmentMarketplaceAdapter catalog, AdmissionGate hostGate)
{
    public AdmissionResult Evaluate(
        ReadOnlyMemory<byte> catalogBytes,
        CycleSubmission request,
        CheckerReceipt? receipt,
        DateTimeOffset now)
    {
        MarketplacePreflightVerdict decision = catalog.Inspect(catalogBytes, request.CapabilityId);
        return decision.Admitted
            ? hostGate.Evaluate(request, receipt, now)
            : AdmissionResult.Deny("MARKETPLACE_" + decision.Reason);
    }
}
