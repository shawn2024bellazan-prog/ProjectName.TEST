using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ProjectName.ControlPlane;

/// <summary>
/// Serialized signed payload. A valid signature alone is not a grant:
/// the consuming host must parse the payload, match every request field,
/// consult revocation and obtain independent Checker evidence.
/// </summary>
public sealed record SignedMcpApprovalEvidence(
    string Purpose, string PayloadBase64,
    string SignatureBase64, string CertificateSha256Hex);

/// <summary>
/// Verifies an ECDSA-P256/SHA256 host approval signature against a separately
/// pinned X.509 public certificate. Does not generate, import or issue keys.
/// This is NOT the PMCR-O agent-identity/Ed25519 protocol.
/// </summary>
public sealed class PinnedWindowsApprovalSignatureVerifier(
    X509Certificate2? trustedCertificate,
    string? pinnedCertificateSha256Hex)
{
    public const string GrantPurpose = "pmcro.mcp.host-grant.v1";
    public const string HumanInvocationPurpose = "pmcro.mcp.human-invocation.v1";
    public const string RevocationPurpose = "pmcro.mcp.revocation-snapshot.v1";

    public bool Verify(SignedMcpApprovalEvidence? evidence,
        string expectedPurpose, DateTimeOffset now)
    {
        try
        {
            if (trustedCertificate is null ||
                evidence is null ||
                string.IsNullOrWhiteSpace(pinnedCertificateSha256Hex) ||
                pinnedCertificateSha256Hex.Length != 64 ||
                !pinnedCertificateSha256Hex.All(Uri.IsHexDigit) ||
                expectedPurpose is not (GrantPurpose or HumanInvocationPurpose or RevocationPurpose) ||
                !string.Equals(evidence.Purpose, expectedPurpose, StringComparison.Ordinal) ||
                !string.Equals(evidence.CertificateSha256Hex,
                    pinnedCertificateSha256Hex, StringComparison.OrdinalIgnoreCase) ||
                now.UtcDateTime < trustedCertificate.NotBefore.ToUniversalTime() ||
                now.UtcDateTime >= trustedCertificate.NotAfter.ToUniversalTime())
                return false;

            // Pin SHA-256 of the entire certificate; Thumbprint is SHA-1.
            byte[] trustedHash = Convert.FromHexString(pinnedCertificateSha256Hex);
            byte[] actualHash = trustedCertificate.GetCertHash(HashAlgorithmName.SHA256);
            if (!CryptographicOperations.FixedTimeEquals(trustedHash, actualHash))
                return false;

            // Signature only; prohibit certificates lacking digitalSignature usage.
            bool hasDigitalSignatureUsage = trustedCertificate.Extensions
                .OfType<X509KeyUsageExtension>()
                .Any(usage => (usage.KeyUsages & X509KeyUsageFlags.DigitalSignature) != 0);
            if (!hasDigitalSignatureUsage)
                return false;

            byte[] payload = Convert.FromBase64String(evidence.PayloadBase64);
            byte[] signature = Convert.FromBase64String(evidence.SignatureBase64);
            if (payload.Length == 0 || payload.Length > 16384 ||
                signature.Length != 64)
                return false;

            // Purpose/domain separation is inside the signed byte sequence.
            byte[] domainPrefix = Encoding.UTF8.GetBytes(expectedPurpose + "\n");
            if (!payload.AsSpan().StartsWith(domainPrefix))
                return false;

            using ECDsa? key = trustedCertificate.GetECDsaPublicKey();
            return key?.KeySize == 256 &&
                key.VerifyData(payload, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is CryptographicException or
                                   FormatException or ObjectDisposedException or
                                   ArgumentException)
        {
            return false;
        }
    }
}
