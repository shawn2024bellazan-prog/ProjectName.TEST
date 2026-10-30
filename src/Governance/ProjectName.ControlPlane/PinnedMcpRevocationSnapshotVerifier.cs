using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

namespace ProjectName.ControlPlane;

/// <summary>
/// A freshly verified signed revocation snapshot. Not a grant or a Checker receipt.
/// The set is immutable and exact; unknown grants are not authorized by this object.
/// </summary>
public sealed record HostVerifiedMcpRevocationSnapshot(
    string IssuerId, string PolicyId,
    DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc,
    IReadOnlySet<string> RevokedGrantIds)
{
    public bool IsRevoked(string grantId) => RevokedGrantIds.Contains(grantId);
}

/// <summary>
/// Offline, fail-closed verifier of a small signed revocation snapshot. The
/// host must provide a pinned verifier and exact issuer/policy identities.
/// No key material, trust anchors or grants are provisioned here.
/// </summary>
public sealed class PinnedMcpRevocationSnapshotVerifier(
    PinnedWindowsApprovalSignatureVerifier signatures)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxWindow = TimeSpan.FromMinutes(15);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "issuerId", "policyId", "issuedAtUtc", "expiresAtUtc", "revokedGrantIds"
    };

    public bool TryVerify(SignedMcpApprovalEvidence? evidence,
        string expectedIssuerId, string expectedPolicyId, DateTimeOffset now,
        out HostVerifiedMcpRevocationSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(expectedIssuerId) ||
            string.IsNullOrWhiteSpace(expectedPolicyId) ||
            evidence is null ||
            !signatures.Verify(evidence,
                PinnedWindowsApprovalSignatureVerifier.RevocationPurpose, now))
            return false;

        try
        {
            byte[] payload = Convert.FromBase64String(evidence.PayloadBase64);
            string fullText = StrictUtf8.GetString(payload);
            string prefix = PinnedWindowsApprovalSignatureVerifier.RevocationPurpose + "\n";
            if (!fullText.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            using var document = JsonDocument.Parse(fullText[prefix.Length..]);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
                if (!Fields.Contains(property.Name) || !found.Add(property.Name))
                    return false;

            if (found.Count != Fields.Count ||
                root.GetProperty("issuerId").GetString() != expectedIssuerId ||
                root.GetProperty("policyId").GetString() != expectedPolicyId)
                return false;

            DateTimeOffset issuedAt = root.GetProperty("issuedAtUtc").GetDateTimeOffset();
            DateTimeOffset expiresAt = root.GetProperty("expiresAtUtc").GetDateTimeOffset();
            if (issuedAt > now || expiresAt <= now ||
                now - issuedAt > MaxAge ||
                expiresAt - issuedAt > MaxWindow ||
                expiresAt <= issuedAt)
                return false;

            JsonElement list = root.GetProperty("revokedGrantIds");
            if (list.ValueKind != JsonValueKind.Array ||
                list.GetArrayLength() > 256)
                return false;

            var revoked = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    return false;
                string? id = item.GetString();
                if (string.IsNullOrWhiteSpace(id) || id.Length > 128 ||
                    id.Contains('*') || !revoked.Add(id))
                    return false;
            }

            snapshot = new(expectedIssuerId, expectedPolicyId,
                issuedAt, expiresAt, revoked.ToFrozenSet(StringComparer.Ordinal));
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or
                                   ArgumentException or InvalidOperationException or
                                   DecoderFallbackException or OverflowException)
        {
            return false;
        }
    }
}
