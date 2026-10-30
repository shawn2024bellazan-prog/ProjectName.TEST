using System.Security.Cryptography;
using System.Text;

namespace ProjectName.Evidence;

public static class EvidenceHash
{
    public static string Sha256(string canonicalContent) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalContent)))
            .ToLowerInvariant();
}
