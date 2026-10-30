using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ProjectName.ControlPlane;

namespace ProjectName.Governance.Tests;

public sealed class PinnedMcpRevocationSnapshotVerifierTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private const string ValidJson =
        """{"issuerId":"windows-host","policyId":"mcp-revocations-v1","issuedAtUtc":"2026-10-28T23:59:00Z","expiresAtUtc":"2026-10-29T00:04:00Z","revokedGrantIds":["revoked-grant"]}""";

    private static (X509Certificate2 Certificate,
        SignedMcpApprovalEvidence Evidence, string Pin) Sign(string json)
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=PMCR-O revocation test-only", signer, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature, critical: true));
        using var disposable = request.CreateSelfSigned(
            Now.AddDays(-1), Now.AddDays(1));
        var certificate = X509CertificateLoader.LoadCertificate(
            disposable.Export(X509ContentType.Cert));
        string pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        byte[] payload = Encoding.UTF8.GetBytes(
            PinnedWindowsApprovalSignatureVerifier.RevocationPurpose + "\n" + json);
        byte[] signature = signer.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return (certificate, new(
            PinnedWindowsApprovalSignatureVerifier.RevocationPurpose,
            Convert.ToBase64String(payload),
            Convert.ToBase64String(signature), pin), pin);
    }

    private static bool Verify(string json, DateTimeOffset? now = null,
        string issuer = "windows-host", string policy = "mcp-revocations-v1")
    {
        var f = Sign(json);
        using var cert = f.Certificate;
        return new PinnedMcpRevocationSnapshotVerifier(
            new PinnedWindowsApprovalSignatureVerifier(cert, f.Pin))
            .TryVerify(f.Evidence, issuer, policy, now ?? Now, out _);
    }

    [Fact]
    public void Exact_signed_fresh_snapshot_reads_revocation_set()
    {
        var f = Sign(ValidJson);
        using var cert = f.Certificate;
        var verifier = new PinnedMcpRevocationSnapshotVerifier(
            new PinnedWindowsApprovalSignatureVerifier(cert, f.Pin));
        Assert.True(verifier.TryVerify(f.Evidence,
            "windows-host", "mcp-revocations-v1", Now, out var snapshot));
        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsRevoked("revoked-grant"));
        Assert.False(snapshot.IsRevoked("unknown-grant"));
    }

    [Fact]
    public void Without_a_pinned_trust_anchor_snapshot_is_denied()
    {
        var f = Sign(ValidJson);
        using var cert = f.Certificate;
        var verifier = new PinnedMcpRevocationSnapshotVerifier(
            new PinnedWindowsApprovalSignatureVerifier(null, null));
        Assert.False(verifier.TryVerify(f.Evidence,
            "windows-host", "mcp-revocations-v1", Now, out var snapshot));
        Assert.Null(snapshot);
    }

    [Fact]
    public void Expired_or_stale_signed_snapshot_is_denied()
    {
        Assert.False(Verify(ValidJson, now: Now.AddMinutes(6)));
        Assert.False(Verify(ValidJson.Replace("23:59:00", "23:50:00", StringComparison.Ordinal)));
        Assert.False(Verify(ValidJson.Replace("23:59:00", "00:01:00", StringComparison.Ordinal)));
    }

    [Fact]
    public void Wrong_issuer_policy_or_unknown_fields_are_denied()
    {
        Assert.False(Verify(ValidJson, issuer: "untrusted-issuer"));
        Assert.False(Verify(ValidJson, policy: "unapproved-policy"));
        Assert.False(Verify(ValidJson.Replace(
            "\"revokedGrantIds\"", "\"extra\":1,\"revokedGrantIds\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void Duplicate_ids_and_wildcards_are_denied()
    {
        Assert.False(Verify(ValidJson.Replace(
            "[\"revoked-grant\"]", "[\"revoked-grant\",\"revoked-grant\"]", StringComparison.Ordinal)));
        Assert.False(Verify(ValidJson.Replace(
            "[\"revoked-grant\"]", "[\"*\"]", StringComparison.Ordinal)));
    }

    [Fact]
    public void Tampered_signed_payload_is_denied()
    {
        var f = Sign(ValidJson);
        using var cert = f.Certificate;
        var corrupted = f.Evidence with
        {
            PayloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                PinnedWindowsApprovalSignatureVerifier.RevocationPurpose + "\n" +
                ValidJson.Replace("revoked-grant", "not-revoked", StringComparison.Ordinal)))
        };
        var verifier = new PinnedMcpRevocationSnapshotVerifier(
            new PinnedWindowsApprovalSignatureVerifier(cert, f.Pin));
        Assert.False(verifier.TryVerify(corrupted,
            "windows-host", "mcp-revocations-v1", Now, out _));
    }
}
