using System.Security.Cryptography;
using System.Text;

namespace ProjectName.Governance;

// Trust anchors must be supplied from an authenticated host configuration.
// The default configuration contains no keys; discovery never installs keys.
public sealed class EcdsaCheckerReceiptVerifier(
    IReadOnlyDictionary<string, byte[]> trustedPublicKeys) : IIndependentReceiptVerifier
{
    public bool Verify(CheckerReceipt receipt)
    {
        if (!trustedPublicKeys.TryGetValue(receipt.CheckerId, out byte[]? publicKey) ||
            publicKey.Length == 0)
            return false;
        try
        {
            byte[] signature = Convert.FromBase64String(receipt.Signature);
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKey, out _);
            return verifier.VerifyData(SigningPayload(receipt), signature, HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    public static byte[] SigningPayload(CheckerReceipt receipt) =>
        Encoding.UTF8.GetBytes(
            $"{receipt.CheckerId.Length}:{receipt.CheckerId}" +
            $"{receipt.CapabilityId.Length}:{receipt.CapabilityId}" +
            $"{receipt.Digest.Length}:{receipt.Digest}" +
            $"{receipt.EvidenceReference.Length}:{receipt.EvidenceReference}");
}
