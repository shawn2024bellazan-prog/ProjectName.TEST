namespace ProjectName.ControlPlane;

/// <summary>Development-only key metadata scope; never contains real key bytes.</summary>
public sealed record MockKeyScope(string IssuerId, string TenantId,
    string ActorId, string WorkflowSha256)
{
    public bool IsValid =>
        Valid(IssuerId) && Valid(TenantId) && Valid(ActorId) &&
        WorkflowSha256 is { Length: 64 } && WorkflowSha256.All(Uri.IsHexDigit);

    private static bool Valid(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Length <= 128 && !text.Contains('*');
}

public enum MockKeyStatus { Active, ReadOnly, Revoked }

public sealed record MockKeyDescriptor(string KeyId, int Version,
    MockKeyStatus Status, DateTimeOffset ExpiresAt);

public enum MockRotationResult
{
    Accepted, InvalidScope, Conflict, RollbackDenied, InvalidKey,
    Revoked
}

/// <summary>
/// Metadata-only versioned key-ring. It cannot encrypt, decrypt, provision
/// keys, or authorize a worker. No production dependency injection uses it.
/// </summary>
public sealed class MockCheckpointKeyRing
{
    private sealed class Ring
    {
        public long Epoch = 1;
        public Dictionary<string, MockKeyDescriptor> Keys = new(StringComparer.Ordinal);
    }

    private readonly object sync = new();
    private readonly Dictionary<MockKeyScope, Ring> rings = new();

    public MockRotationResult TryInitialize(MockKeyScope scope,
        string keyId, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (!scope.IsValid) return MockRotationResult.InvalidScope;
        if (!ValidKeyId(keyId) || expiresAt <= now)
            return MockRotationResult.InvalidKey;
        lock (sync)
        {
            if (rings.ContainsKey(scope)) return MockRotationResult.Conflict;
            var ring = new Ring();
            ring.Keys.Add(keyId, new(keyId, 1, MockKeyStatus.Active, expiresAt));
            rings.Add(scope, ring);
            return MockRotationResult.Accepted;
        }
    }

    public MockRotationResult TryRotate(MockKeyScope scope, long expectedEpoch,
        string newKeyId, int newVersion, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (!scope.IsValid) return MockRotationResult.InvalidScope;
        if (!ValidKeyId(newKeyId) || expiresAt <= now)
            return MockRotationResult.InvalidKey;
        lock (sync)
        {
            if (!rings.TryGetValue(scope, out var ring))
                return MockRotationResult.InvalidKey;
            if (ring.Epoch != expectedEpoch) return MockRotationResult.Conflict;
            if (ring.Keys.ContainsKey(newKeyId) ||
                newVersion <= ring.Keys.Values.Max(k => k.Version) ||
                newVersion > 64)
                return MockRotationResult.RollbackDenied;
            MockKeyDescriptor? active =
                ring.Keys.Values.SingleOrDefault(k => k.Status == MockKeyStatus.Active);
            if (active is null || active.ExpiresAt <= now)
                return MockRotationResult.Revoked;
            ring.Keys[active.KeyId] = active with { Status = MockKeyStatus.ReadOnly };
            ring.Keys.Add(newKeyId,
                new(newKeyId, newVersion, MockKeyStatus.Active, expiresAt));
            ring.Epoch++;
            return MockRotationResult.Accepted;
        }
    }

    public MockRotationResult TryRevoke(MockKeyScope scope,
        long expectedEpoch, string keyId)
    {
        if (!scope.IsValid) return MockRotationResult.InvalidScope;
        lock (sync)
        {
            if (!rings.TryGetValue(scope, out var ring) ||
                !ring.Keys.TryGetValue(keyId, out var entry))
                return MockRotationResult.InvalidKey;
            if (ring.Epoch != expectedEpoch) return MockRotationResult.Conflict;
            if (entry.Status == MockKeyStatus.Revoked)
                return MockRotationResult.Revoked;
            ring.Keys[keyId] = entry with { Status = MockKeyStatus.Revoked };
            ring.Epoch++;
            return MockRotationResult.Accepted;
        }
    }

    public (long Epoch, MockKeyDescriptor? Key) Lookup(
        MockKeyScope scope, string keyId)
    {
        lock (sync)
        {
            if (!scope.IsValid || string.IsNullOrWhiteSpace(keyId) ||
                !rings.TryGetValue(scope, out var ring))
                return (0, null);
            return (ring.Epoch,
                ring.Keys.TryGetValue(keyId, out var entry) ? entry : null);
        }
    }

    public bool CanWrite(MockKeyScope scope, string keyId, DateTimeOffset now)
    {
        MockKeyDescriptor? key = Lookup(scope, keyId).Key;
        return key is { Status: MockKeyStatus.Active } && key.ExpiresAt > now;
    }

    public bool CanRead(MockKeyScope scope, string keyId, DateTimeOffset now)
    {
        MockKeyDescriptor? key = Lookup(scope, keyId).Key;
        return key is not null &&
            key.Status is MockKeyStatus.Active or MockKeyStatus.ReadOnly &&
            key.ExpiresAt > now;
    }

    private static bool ValidKeyId(string? keyId) =>
        !string.IsNullOrWhiteSpace(keyId) &&
        keyId.Length <= 128 && !keyId.Contains('*');
}
