using System.Security.Cryptography;
using System.Text;

namespace ProjectName.ControlPlane;

/// <summary>
/// Encryption-at-rest primitive for verified tenant-owned session/checkpoint
/// snapshots. The directory and 256-bit key must be provisioned by the host.
/// Nothing is registered or activated in the default AgentGateway.
/// </summary>
public sealed class EncryptedTenantStateStore
{
    private const int MaxPlaintextBytes = 16 * 1024 * 1024;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly string _root;
    private readonly byte[] _key;

    public EncryptedTenantStateStore(string protectedRoot, ReadOnlySpan<byte> externallyProvidedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedRoot);
        if (!Path.IsPathFullyQualified(protectedRoot) || !Directory.Exists(protectedRoot))
            throw new InvalidOperationException("A host-provisioned absolute protected directory is required.");
        if (File.GetAttributes(protectedRoot).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("A reparse-point state directory is forbidden.");
        if (externallyProvidedKey.Length != 32)
            throw new ArgumentException("A host-provisioned AES-256 key is required.", nameof(externallyProvidedKey));

        _root = Path.GetFullPath(protectedRoot);
        _key = externallyProvidedKey.ToArray();
    }

    public async Task SaveAsync(
        VerifiedIdentity caller, string agentId, string stateId,
        ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default)
    {
        if (plaintext.Length > MaxPlaintextBytes)
            throw new ArgumentOutOfRangeException(nameof(plaintext), "State size exceeds the bounded limit.");
        byte[] associatedData = CreateAssociatedData(caller, agentId, stateId);
        string path = BuildPath(associatedData);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, plaintext.Span, ciphertext, tag, associatedData);

        byte[] envelope = new byte[1 + NonceSize + TagSize + ciphertext.Length];
        envelope[0] = 1;
        nonce.CopyTo(envelope, 1);
        tag.CopyTo(envelope, 1 + NonceSize);
        ciphertext.CopyTo(envelope, 1 + NonceSize + TagSize);

        string temporary = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var staged = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, bufferSize: 81920, FileOptions.Asynchronous))
            {
                await staged.WriteAsync(envelope, cancellationToken);
                staged.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<byte[]?> LoadAsync(
        VerifiedIdentity caller, string agentId, string stateId,
        CancellationToken cancellationToken = default)
    {
        byte[] associatedData = CreateAssociatedData(caller, agentId, stateId);
        string path = BuildPath(associatedData);
        if (!File.Exists(path)) return null;

        byte[] envelope = await File.ReadAllBytesAsync(path, cancellationToken);
        if (envelope.Length < 1 + NonceSize + TagSize || envelope[0] != 1 ||
            envelope.Length > MaxPlaintextBytes + 1 + NonceSize + TagSize)
            throw new InvalidDataException("Invalid encrypted state envelope.");

        byte[] plaintext = new byte[envelope.Length - 1 - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(
                envelope.AsSpan(1, NonceSize),
                envelope.AsSpan(1 + NonceSize + TagSize),
                envelope.AsSpan(1 + NonceSize, TagSize),
                plaintext, associatedData);
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException("Encrypted state authentication failed.");
        }
        return plaintext;
    }

    /// <summary>
    /// Exclusive OS file lease on a tenant-scoped opaque state key. FileShare.None
    /// serializes cooperating processes on the same protected local filesystem.
    /// Not a distributed lock; do not use for multi-host or network volumes.
    /// </summary>
    public async ValueTask<FileStream> AcquireExclusiveLeaseAsync(
        VerifiedIdentity caller, string agentId, string stateId,
        TimeSpan waitLimit, CancellationToken cancellationToken = default)
    {
        if (waitLimit <= TimeSpan.Zero || waitLimit > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(waitLimit));

        string lockPath = BuildPath(CreateAssociatedData(caller, agentId, stateId)) + ".lock";
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, bufferSize: 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                if (started.Elapsed >= waitLimit)
                    throw new TimeoutException("State write lease was not acquired within the bounded limit.");
                await Task.Delay(25, cancellationToken);
            }
        }
    }

    public Task DeleteAsync(
        VerifiedIdentity caller, string agentId, string stateId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = BuildPath(CreateAssociatedData(caller, agentId, stateId));
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string BuildPath(byte[] associatedData)
    {
        byte[] digest = HMACSHA256.HashData(_key, associatedData);
        return Path.Combine(_root, Convert.ToHexString(digest) + ".enc");
    }

    private static byte[] CreateAssociatedData(VerifiedIdentity caller, string agentId, string stateId)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (string.IsNullOrWhiteSpace(caller.TenantId) ||
            string.IsNullOrWhiteSpace(caller.ActorId) ||
            string.IsNullOrWhiteSpace(caller.IssuerId) ||
            string.IsNullOrWhiteSpace(agentId) ||
            string.IsNullOrWhiteSpace(stateId))
            throw new ArgumentException("Verified tenant, actor, issuer, agent and state IDs are required.");

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("projectname-encrypted-state-v1");
            writer.Write(caller.IssuerId);
            writer.Write(caller.TenantId);
            writer.Write(caller.ActorId);
            writer.Write(agentId);
            writer.Write(stateId);
        }
        return stream.ToArray();
    }
}