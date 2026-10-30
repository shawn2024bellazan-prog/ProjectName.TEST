using System.Security.Cryptography;
using System.Text;

namespace ProjectName.ControlPlane;

/// <summary>
/// Development candidate for durable, single-use human MCP approvals.
/// The host must configure a pre-existing, access-controlled local directory.
/// No application registers this store; constructing it does not issue grants.
/// </summary>
public sealed class FileMcpApprovalUseStore : IHostMcpApprovalUseStore
{
    private readonly string directory;

    public FileMcpApprovalUseStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) ||
            !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("An absolute, host-managed directory is required.", nameof(directory));

        this.directory = Path.GetFullPath(directory);
        if (!Directory.Exists(this.directory))
            throw new DirectoryNotFoundException("Approval ledger directory must be pre-provisioned by the host.");
    }

    public bool TryConsume(HostVerifiedMcpHumanApproval approval,
        McpToolInvocation request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(approval.GrantId) ||
            string.IsNullOrWhiteSpace(approval.InvocationId) ||
            string.IsNullOrWhiteSpace(approval.ArgumentsDigest) ||
            string.IsNullOrWhiteSpace(approval.EvidenceId) ||
            string.IsNullOrWhiteSpace(approval.ApproverActorId) ||
            approval.ExpiresAt <= now ||
            string.IsNullOrWhiteSpace(request.InvocationId) ||
            !string.Equals(approval.InvocationId, request.InvocationId, StringComparison.Ordinal) ||
            !string.Equals(approval.ArgumentsDigest, request.ArgumentsDigest, StringComparison.Ordinal) ||
            string.Equals(approval.ApproverActorId, request.ActorId, StringComparison.Ordinal))
            return false;

        // Length-prefix keys to make concatenation unambiguous; digest hides
        // principal and invocation identifiers from directory listings.
        string key = $"{approval.GrantId.Length}:{approval.GrantId}{approval.InvocationId.Length}:{approval.InvocationId}";
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        string marker = Path.Combine(directory, digest + ".used");

        try
        {
            // CreateNew is atomic across processes on the same filesystem:
            // exactly one caller claims the grant/invocation pair. Never delete
            // markers; removal would re-enable replay.
            using var claim = new FileStream(marker, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, bufferSize: 4096, options: FileOptions.WriteThrough);
            claim.WriteByte(1);
            claim.Flush(flushToDisk: true);
            return true;
        }
        catch (IOException) when (File.Exists(marker))
        {
            return false;
        }
    }
}
