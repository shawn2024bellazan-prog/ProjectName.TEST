using System.Reflection;
using ProjectName.ControlPlane;
using ProjectName.Runtime;

namespace ProjectName.Governance.Tests;

public sealed class MockCheckpointRotationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expires = Now.AddDays(1);
    private static readonly string OldDigest = new('a', 64);
    private static readonly string NewDigest = new('b', 64);

    private static MockCheckpointScope Scope(string issuer = "issuer-a",
        string tenant = "tenant-a", string workflow = "a") => new(
        new MockKeyScope(issuer, tenant, "actor-a",
            new string(workflow[0], 64)), "session-a", "checkpoint-a");

    private static (MockCheckpointKeyRing Ring,
        MockCheckpointMigrationManifest Manifest,
        MockCheckpointScope Scope) Create()
    {
        var ring = new MockCheckpointKeyRing();
        MockCheckpointScope scope = Scope();
        Assert.Equal(MockRotationResult.Accepted,
            ring.TryInitialize(scope.Owner, "key-v1", Expires, Now));
        var manifest = new MockCheckpointMigrationManifest(ring);
        Assert.Equal(MockManifestResult.Accepted,
            manifest.TryInitialize(scope, "key-v1", OldDigest, Now));
        return (ring, manifest, scope);
    }

    [Fact]
    public void Ring_versions_increase_strictly_and_old_key_becomes_read_only()
    {
        var (ring, _, scope) = Create();
        Assert.Equal(MockRotationResult.Conflict,
            ring.TryRotate(scope.Owner, 0, "key-v2", 2, Expires, Now));
        Assert.Equal(MockRotationResult.RollbackDenied,
            ring.TryRotate(scope.Owner, 1, "key-old", 1, Expires, Now));
        Assert.Equal(MockRotationResult.Accepted,
            ring.TryRotate(scope.Owner, 1, "key-v2", 2, Expires, Now));
        Assert.Equal(2, ring.Lookup(scope.Owner, "key-v2").Epoch);
        Assert.False(ring.CanWrite(scope.Owner, "key-v1", Now));
        Assert.True(ring.CanRead(scope.Owner, "key-v1", Now));
        Assert.True(ring.CanWrite(scope.Owner, "key-v2", Now));
        Assert.False(ring.CanRead(scope.Owner, "key-v2", Expires));
    }

    [Fact]
    public void Revocation_is_irreversible_and_prevents_new_writes_and_reads()
    {
        var (ring, _, scope) = Create();
        Assert.Equal(MockRotationResult.Accepted,
            ring.TryRevoke(scope.Owner, 1, "key-v1"));
        Assert.False(ring.CanRead(scope.Owner, "key-v1", Now));
        Assert.False(ring.CanWrite(scope.Owner, "key-v1", Now));
        Assert.Equal(MockRotationResult.Revoked,
            ring.TryRevoke(scope.Owner, 2, "key-v1"));
        Assert.Equal(MockRotationResult.Revoked,
            ring.TryRotate(scope.Owner, 2, "key-v2", 2, Expires, Now));
        Assert.Equal(MockRotationResult.RollbackDenied,
            ring.TryRotate(scope.Owner, 2, "key-v1", 2, Expires, Now));
    }

    [Fact]
    public void Atomic_compare_and_swap_allows_one_concurrent_migration()
    {
        var (ring, manifest, scope) = Create();
        Assert.Equal(MockRotationResult.Accepted,
            ring.TryRotate(scope.Owner, 1, "key-v2", 2, Expires, Now));
        var results = new System.Collections.Concurrent.ConcurrentBag<MockManifestResult>();
        Parallel.For(0, 16, _ => results.Add(manifest.TryCommit(
            scope, 1, OldDigest, "key-v2", NewDigest, "EV-mock", Now)));
        Assert.Equal(1, results.Count(r => r == MockManifestResult.Accepted));
        Assert.Equal(15, results.Count(r => r == MockManifestResult.CasConflict));
        Assert.Equal(2, manifest.Read(scope)!.Revision);
        Assert.Equal("key-v2", manifest.Read(scope)!.KeyId);
    }

    [Fact]
    public void Committed_manifest_cannot_roll_back_to_prior_key_or_digest()
    {
        var (ring, manifest, scope) = Create();
        ring.TryRotate(scope.Owner, 1, "key-v2", 2, Expires, Now);
        Assert.Equal(MockManifestResult.Accepted,
            manifest.TryCommit(scope, 1, OldDigest, "key-v2", NewDigest, "EV-2", Now));
        Assert.Equal(MockManifestResult.RollbackDenied,
            manifest.TryCommit(scope, 2, NewDigest, "key-v1", OldDigest, "EV-rollback", Now));
        Assert.Equal(MockManifestResult.CasConflict,
            manifest.TryCommit(scope, 1, OldDigest, "key-v2", NewDigest, "EV-replay", Now));
        Assert.Equal("EV-2", manifest.Read(scope)!.ReceiptId);
    }

    [Fact]
    public void Tenant_issuer_and_workflow_scopes_are_not_interchangeable()
    {
        var (ring, manifest, scope) = Create();
        MockCheckpointScope otherTenant = Scope(tenant: "tenant-b");
        MockCheckpointScope otherIssuer = Scope(issuer: "issuer-b");
        MockCheckpointScope otherWorkflow = Scope(workflow: "c");
        foreach (MockCheckpointScope other in new[] { otherTenant, otherIssuer, otherWorkflow })
        {
            Assert.Null(manifest.Read(other));
            Assert.False(ring.CanRead(other.Owner, "key-v1", Now));
            Assert.Equal(MockManifestResult.NotFound,
                manifest.TryCommit(other, 1, OldDigest, "key-v1",
                    NewDigest, "EV-cross", Now));
        }
        Assert.Equal(MockRotationResult.InvalidScope,
            ring.TryInitialize(scope.Owner with { WorkflowSha256 = "*" },
                "key-invalid", Expires, Now));
    }

    [Fact]
    public void Bounded_retries_and_cancellation_cannot_commit_unboundedly()
    {
        var (ring, manifest, scope) = Create();
        ring.TryRotate(scope.Owner, 1, "key-v2", 2, Expires, Now);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            manifest.TryCommitBounded(scope, "key-v2", NewDigest, "EV", Now, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            manifest.TryCommitBounded(scope, "key-v2", NewDigest, "EV", Now, 4));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            manifest.TryCommitBounded(scope, "key-v2", NewDigest, "EV",
                Now, 3, cancel.Token));
        Assert.Equal(MockManifestResult.Accepted,
            manifest.TryCommitBounded(scope, "key-v2", NewDigest, "EV", Now, 3));
        Assert.Equal(MockManifestResult.RollbackDenied,
            manifest.TryCommitBounded(scope, "key-v2", NewDigest, "EV-again", Now, 3));
    }

    [Fact]
    public void Interrupted_stage_is_orphan_candidate_not_authoritative_pointer()
    {
        var (ring, manifest, scope) = Create();
        ring.TryRotate(scope.Owner, 1, "key-v2", 2, Expires, Now);
        // Simulated encrypted v2 payload staged, process interrupted
        // before compare-and-swap index publication.
        string uncommittedDigest = NewDigest;
        Assert.Equal(OldDigest.ToUpperInvariant(),
            manifest.Read(scope)!.CiphertextSha256);
        Assert.NotEqual(uncommittedDigest.ToUpperInvariant(),
            manifest.Read(scope)!.CiphertextSha256);
        Assert.Equal(MockManifestResult.Accepted,
            manifest.TryCommit(scope, 1, OldDigest, "key-v2", NewDigest, "EV-final", Now));
        Assert.Equal(NewDigest.ToUpperInvariant(), manifest.Read(scope)!.CiphertextSha256);
    }

    [Fact]
    public void Orphan_retention_is_non_deleting_and_requires_stable_review()
    {
        TimeSpan retention = TimeSpan.FromHours(24);
        DateTimeOffset old = Now.AddHours(-30);
        Assert.Equal(MockOrphanVerdict.Keep,
            MockCheckpointOrphanPolicy.Assess(true, false, 3, old, Now, retention));
        Assert.Equal(MockOrphanVerdict.Keep,
            MockCheckpointOrphanPolicy.Assess(false, true, 3, old, Now, retention));
        Assert.Equal(MockOrphanVerdict.Keep,
            MockCheckpointOrphanPolicy.Assess(false, false, 3, Now.AddHours(-1), Now, retention));
        Assert.Equal(MockOrphanVerdict.Quarantine,
            MockCheckpointOrphanPolicy.Assess(false, false, 1, old, Now, retention));
        Assert.Equal(MockOrphanVerdict.ManualReviewRequired,
            MockCheckpointOrphanPolicy.Assess(false, false, 2, old, Now, retention));
    }

    [Fact]
    public void Fault_injection_member_is_not_a_public_runtime_api()
    {
        var publicMember = typeof(EncryptedMafCheckpointStore)
            .GetProperty("AfterPayloadSavedForTesting", BindingFlags.Public | BindingFlags.Instance);
        Assert.Null(publicMember);
        var internalMember = typeof(EncryptedMafCheckpointStore)
            .GetProperty("AfterPayloadSavedForTesting", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(internalMember);
        Assert.True(internalMember!.GetMethod?.IsAssembly);
    }
}
