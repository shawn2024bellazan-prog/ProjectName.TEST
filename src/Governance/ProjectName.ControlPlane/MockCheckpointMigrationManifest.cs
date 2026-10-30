namespace ProjectName.ControlPlane;

public sealed record MockCheckpointScope(MockKeyScope Owner, string SessionId,
    string StateId)
{
    public bool IsValid => Owner is { IsValid: true } &&
        Valid(SessionId) && Valid(StateId);

    private static bool Valid(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 128 && !id.Contains('*');
}

/// <summary>Immutable development metadata; no secret material or ciphertext.</summary>
public sealed record MockMigrationPointer(MockCheckpointScope Scope,
    long Revision, string KeyId, int KeyVersion, string CiphertextSha256,
    string ReceiptId);

public enum MockManifestResult
{
    Accepted, InvalidScope, NotFound, AlreadyExists, CasConflict,
    RollbackDenied, KeyUnavailable, InvalidEvidence
}

/// <summary>
/// In-memory model of a transactional migration manifest with atomic CAS.
/// NOT a persistent, encrypted, production or multi-host checkpoint backend.
/// </summary>
public sealed class MockCheckpointMigrationManifest(MockCheckpointKeyRing ring)
{
    private readonly object sync = new();
    private readonly Dictionary<MockCheckpointScope, MockMigrationPointer> pointers = new();

    public MockManifestResult TryInitialize(MockCheckpointScope scope,
        string keyId, string ciphertextSha256, DateTimeOffset now)
    {
        if (!scope.IsValid) return MockManifestResult.InvalidScope;
        if (!Digest(ciphertextSha256)) return MockManifestResult.InvalidEvidence;
        MockKeyDescriptor? key = ring.Lookup(scope.Owner, keyId).Key;
        if (key is null || key.Version != 1 || !ring.CanWrite(scope.Owner, keyId, now))
            return MockManifestResult.KeyUnavailable;
        lock (sync)
        {
            if (pointers.ContainsKey(scope)) return MockManifestResult.AlreadyExists;
            pointers.Add(scope, new(scope, 1, keyId, key.Version,
                ciphertextSha256.ToUpperInvariant(), "initial-v1"));
            return MockManifestResult.Accepted;
        }
    }

    public MockMigrationPointer? Read(MockCheckpointScope scope)
    {
        lock (sync)
            return pointers.TryGetValue(scope, out var pointer) ? pointer : null;
    }

    public MockManifestResult TryCommit(MockCheckpointScope scope,
        long expectedRevision, string expectedOldCiphertextSha256,
        string newKeyId, string newCiphertextSha256, string migrationReceiptId,
        DateTimeOffset now)
    {
        if (!scope.IsValid) return MockManifestResult.InvalidScope;
        if (!Digest(expectedOldCiphertextSha256) ||
            !Digest(newCiphertextSha256) ||
            string.IsNullOrWhiteSpace(migrationReceiptId) ||
            migrationReceiptId.Length > 128 || migrationReceiptId.Contains('*'))
            return MockManifestResult.InvalidEvidence;

        lock (sync)
        {
            if (!pointers.TryGetValue(scope, out var previous))
                return MockManifestResult.NotFound;
            if (previous.Revision != expectedRevision ||
                !string.Equals(previous.CiphertextSha256,
                    expectedOldCiphertextSha256, StringComparison.OrdinalIgnoreCase))
                return MockManifestResult.CasConflict;

            MockKeyDescriptor? target = ring.Lookup(scope.Owner, newKeyId).Key;
            if (target is null)
                return MockManifestResult.KeyUnavailable;
            if (target.Version <= previous.KeyVersion)
                return MockManifestResult.RollbackDenied;
            if (!ring.CanWrite(scope.Owner, newKeyId, now))
                return MockManifestResult.KeyUnavailable;

            pointers[scope] = new(scope, checked(previous.Revision + 1),
                target.KeyId, target.Version, newCiphertextSha256.ToUpperInvariant(),
                migrationReceiptId);
            return MockManifestResult.Accepted;
        }
    }

    public MockManifestResult TryCommitBounded(MockCheckpointScope scope,
        string newKeyId, string newCiphertextSha256, string migrationReceiptId,
        DateTimeOffset now, int maxAttempts, CancellationToken cancellation = default)
    {
        if (maxAttempts is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            MockMigrationPointer? current = Read(scope);
            if (current is null) return MockManifestResult.NotFound;
            MockManifestResult result = TryCommit(scope,
                current.Revision, current.CiphertextSha256, newKeyId,
                newCiphertextSha256, migrationReceiptId, now);
            if (result != MockManifestResult.CasConflict) return result;
        }
        return MockManifestResult.CasConflict;
    }

    private static bool Digest(string? text) =>
        text is { Length: 64 } && text.All(Uri.IsHexDigit);
}

/// <summary>
/// Metadata-only retention simulation. No filesystem action or deletion is possible.
/// Candidate objects are never garbage-collected by this API.
/// </summary>
public static class MockCheckpointOrphanPolicy
{
    public static MockOrphanVerdict Assess(bool indexed, bool leaseActive,
        int stableObservations, DateTimeOffset createdAt, DateTimeOffset now,
        TimeSpan minimumAge)
    {
        if (indexed || leaseActive || minimumAge <= TimeSpan.Zero ||
            createdAt > now || now - createdAt < minimumAge)
            return MockOrphanVerdict.Keep;
        if (stableObservations < 2)
            return MockOrphanVerdict.Quarantine;
        return MockOrphanVerdict.ManualReviewRequired;
    }
}

public enum MockOrphanVerdict { Keep, Quarantine, ManualReviewRequired }
