using System.Security.Cryptography;
using ProjectName.Governance;

namespace ProjectName.Governance.Tests;

public sealed class SignedReceiptTests
{
    [Fact]
    public void Unknown_checker_identity_fails_closed()
    {
        var receipt = new CheckerReceipt("checker-1", "cap", "sha256:abc", "EV-001", "AA==");
        var verifier = new EcdsaCheckerReceiptVerifier(
            new Dictionary<string, byte[]>());
        Assert.False(verifier.Verify(receipt));
    }

    [Fact]
    public void Signature_binds_checker_capability_digest_and_evidence()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = new CheckerReceipt("checker-1", "cap", "sha256:abc", "EV-001", "");
        string signature = Convert.ToBase64String(signer.SignData(
            EcdsaCheckerReceiptVerifier.SigningPayload(payload), HashAlgorithmName.SHA256));
        var signed = payload with { Signature = signature };
        var verifier = new EcdsaCheckerReceiptVerifier(
            new Dictionary<string, byte[]> {
                ["checker-1"] = signer.ExportSubjectPublicKeyInfo()
            });
        Assert.True(verifier.Verify(signed));
        Assert.False(verifier.Verify(signed with { EvidenceReference = "EV-002" }));
        Assert.False(verifier.Verify(signed with { Digest = "sha256:other" }));
    }
}
