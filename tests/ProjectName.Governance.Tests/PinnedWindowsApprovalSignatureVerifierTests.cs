using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ProjectName.ControlPlane;

namespace ProjectName.Governance.Tests;

public sealed class PinnedWindowsApprovalSignatureVerifierTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private static (X509Certificate2 Certificate, SignedMcpApprovalEvidence Evidence, string Pin)
        SignFixture(string purpose = PinnedWindowsApprovalSignatureVerifier.GrantPurpose,
            bool digitalSignatureUsage = true,
            DateTimeOffset? expiresAt = null)
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certificateRequest = new CertificateRequest(
            "CN=PMCR-O MCP test-only signer", signer, HashAlgorithmName.SHA256);
        if (digitalSignatureUsage)
            certificateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature, critical: true));

        DateTimeOffset end = expiresAt ?? Now.AddDays(1);
        using var disposableCertificate =
            certificateRequest.CreateSelfSigned(Now.AddDays(-2), end);
        var certificate = X509CertificateLoader.LoadCertificate(
            disposableCertificate.Export(X509ContentType.Cert));
        string pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        byte[] bytes = Encoding.UTF8.GetBytes(purpose + "\n" +
            "{\"grantId\":\"test-only\",\"principal\":\"windows-test\"}");
        byte[] signature = signer.SignData(bytes, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return (certificate,
            new SignedMcpApprovalEvidence(
                purpose, Convert.ToBase64String(bytes),
                Convert.ToBase64String(signature), pin),
            pin);
    }

    [Fact]
    public void No_host_trust_anchor_is_denied()
    {
        var fixture = SignFixture();
        using var cert = fixture.Certificate;
        Assert.False(new PinnedWindowsApprovalSignatureVerifier(null, null)
            .Verify(fixture.Evidence, PinnedWindowsApprovalSignatureVerifier.GrantPurpose, Now));
    }

    [Fact]
    public void Pinned_test_certificate_can_verify_signed_data_without_issuing_a_grant()
    {
        var fixture = SignFixture();
        using var cert = fixture.Certificate;
        var verifier = new PinnedWindowsApprovalSignatureVerifier(cert, fixture.Pin);
        Assert.True(verifier.Verify(fixture.Evidence,
            PinnedWindowsApprovalSignatureVerifier.GrantPurpose, Now));
    }

    [Fact]
    public void Wrong_certificate_pin_is_denied()
    {
        var fixture = SignFixture();
        using var cert = fixture.Certificate;
        Assert.False(new PinnedWindowsApprovalSignatureVerifier(cert, new string('0', 64))
            .Verify(fixture.Evidence, PinnedWindowsApprovalSignatureVerifier.GrantPurpose, Now));
    }

    [Fact]
    public void Cross_purpose_replay_is_denied()
    {
        var fixture = SignFixture();
        using var cert = fixture.Certificate;
        Assert.False(new PinnedWindowsApprovalSignatureVerifier(cert, fixture.Pin)
            .Verify(fixture.Evidence,
                PinnedWindowsApprovalSignatureVerifier.HumanInvocationPurpose, Now));
    }

    [Fact]
    public void Tampered_payload_is_denied()
    {
        var fixture = SignFixture();
        using var cert = fixture.Certificate;
        var forged = fixture.Evidence with
        {
            PayloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                PinnedWindowsApprovalSignatureVerifier.GrantPurpose +
                "\n{\"grantId\":\"forged\"}"))
        };
        Assert.False(new PinnedWindowsApprovalSignatureVerifier(cert, fixture.Pin)
            .Verify(forged, PinnedWindowsApprovalSignatureVerifier.GrantPurpose, Now));
    }

    [Fact]
    public void Malformed_signature_is_denied()
    {
        var fixture = SignFixture();
        using var cert = fixture.Certificate;
        var malformed = fixture.Evidence with { SignatureBase64 = "not-base64" };
        Assert.False(new PinnedWindowsApprovalSignatureVerifier(cert, fixture.Pin)
            .Verify(malformed, PinnedWindowsApprovalSignatureVerifier.GrantPurpose, Now));
    }

    [Fact]
    public void Expired_certificate_is_denied()
    {
        var fixture = SignFixture(expiresAt: Now.AddHours(-1));
        using var cert = fixture.Certificate;
        Assert.False(new PinnedWindowsApprovalSignatureVerifier(cert, fixture.Pin)
            .Verify(fixture.Evidence, PinnedWindowsApprovalSignatureVerifier.GrantPurpose, Now));
    }

    [Fact]
    public void Certificate_without_digital_signature_usage_is_denied()
    {
        var fixture = SignFixture(digitalSignatureUsage: false);
        using var cert = fixture.Certificate;
        Assert.False(new PinnedWindowsApprovalSignatureVerifier(cert, fixture.Pin)
            .Verify(fixture.Evidence, PinnedWindowsApprovalSignatureVerifier.GrantPurpose, Now));
    }
}
