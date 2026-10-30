using System.Text;
using Npgsql;
using ProjectName.Persistence;

namespace ProjectName.Governance.Tests;

/// <summary>
/// Explicit ephemeral integration: no fallback to Aspire's persistent DB.
/// PostgreSQL tenant logins have no BYPASSRLS privilege and cannot SET ROLE.
/// </summary>
public sealed class PostgresTransactionalCheckpointCandidateTests
{
    private static readonly string Workflow = new('a', 64);
    private static readonly byte[] V1 = Encoding.UTF8.GetBytes("TEST ONLY opaque ciphertext envelope v1");
    private static readonly byte[] V2 = Encoding.UTF8.GetBytes("TEST ONLY opaque ciphertext envelope v2");
    private static readonly byte[] V3 = Encoding.UTF8.GetBytes("TEST ONLY opaque ciphertext envelope v3");

    [Fact]
    public async Task Role_bound_rls_cas_revocation_and_backend_termination_are_isolated()
    {
        string? connectionText = Environment.GetEnvironmentVariable(
            "PROJECTNAME_EPHEMERAL_POSTGRES");
        if (string.IsNullOrEmpty(connectionText))
            return; // No database is contacted without explicit test scope.

        var rootCs = new NpgsqlConnectionStringBuilder(connectionText);
        Assert.Equal("127.0.0.1", rootCs.Host);
        Assert.Equal("pmcro_checkpoint_candidate_tests", rootCs.Database);
        Assert.True(rootCs.Port is >= 1024 and <= 65535);
        Assert.Equal("pmcro_test", rootCs.Username);

        await using var rootSource = new NpgsqlDataSourceBuilder(rootCs.ConnectionString).Build();
        await using (var connection = await rootSource.OpenConnectionAsync())
        await using (var schema = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS pmcro_checkpoint_candidate_v1 (
                issuer_id varchar(128) NOT NULL,
                tenant_id varchar(128) NOT NULL,
                actor_id varchar(128) NOT NULL,
                workflow_sha256 char(64) NOT NULL,
                session_id varchar(128) NOT NULL,
                checkpoint_id varchar(128) NOT NULL,
                revision bigint NOT NULL CHECK (revision > 0),
                key_version integer NOT NULL CHECK (key_version > 0),
                envelope bytea NOT NULL,
                envelope_sha256 char(64) NOT NULL,
                revoked boolean NOT NULL DEFAULT false,
                PRIMARY KEY (issuer_id,tenant_id,actor_id,workflow_sha256,
                             session_id,checkpoint_id)
            );
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='pmcro_tenant_a') THEN
                    CREATE ROLE pmcro_tenant_a LOGIN PASSWORD 'disposable-tenant-a';
                END IF;
                IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='pmcro_tenant_b') THEN
                    CREATE ROLE pmcro_tenant_b LOGIN PASSWORD 'disposable-tenant-b';
                END IF;
            END $$;
            GRANT USAGE ON SCHEMA public TO pmcro_tenant_a,pmcro_tenant_b;
            GRANT SELECT,INSERT,UPDATE ON pmcro_checkpoint_candidate_v1
                TO pmcro_tenant_a,pmcro_tenant_b;
            ALTER TABLE pmcro_checkpoint_candidate_v1 ENABLE ROW LEVEL SECURITY;
            ALTER TABLE pmcro_checkpoint_candidate_v1 FORCE ROW LEVEL SECURITY;
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT FROM pg_policies WHERE tablename =
                    'pmcro_checkpoint_candidate_v1' AND policyname='candidate_role_tenant') THEN
                    CREATE POLICY candidate_role_tenant ON pmcro_checkpoint_candidate_v1
                        USING (tenant_id = current_user)
                        WITH CHECK (tenant_id = current_user);
                END IF;
            END $$;
            """, connection))
            await schema.ExecuteNonQueryAsync();

        var aCs = new NpgsqlConnectionStringBuilder(rootCs.ConnectionString)
        {
            Username = "pmcro_tenant_a", Password = "disposable-tenant-a"
        };
        var bCs = new NpgsqlConnectionStringBuilder(rootCs.ConnectionString)
        {
            Username = "pmcro_tenant_b", Password = "disposable-tenant-b"
        };
        await using var sourceA = new NpgsqlDataSourceBuilder(aCs.ConnectionString).Build();
        await using var sourceA2 = new NpgsqlDataSourceBuilder(aCs.ConnectionString).Build();
        await using var sourceB = new NpgsqlDataSourceBuilder(bCs.ConnectionString).Build();

        var a = new PostgresTransactionalCheckpointCandidate(sourceA);
        var a2 = new PostgresTransactionalCheckpointCandidate(sourceA2);
        var b = new PostgresTransactionalCheckpointCandidate(sourceB);
        var privileged = new PostgresTransactionalCheckpointCandidate(rootSource);
        string id = Guid.NewGuid().ToString("N");
        var scope = new CandidateCheckpointScope("issuer-test", "pmcro_tenant_a",
            "actor-a", Workflow, "session-"+id, "checkpoint-a");
        var tenantBScope = scope with { TenantId = "pmcro_tenant_b" };

        Assert.Equal(CandidateCheckpointResult.PrincipalDenied,
            await privileged.TryInitializeAsync(
                scope with { TenantId = "pmcro_test" }, 1, V1));
        Assert.Equal(CandidateCheckpointResult.PrincipalDenied,
            await b.TryInitializeAsync(scope, 1, V1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => b.ReadAsync(scope));

        Assert.Equal(CandidateCheckpointResult.Accepted,
            await a.TryInitializeAsync(scope, 1, V1));
        Assert.Equal(CandidateCheckpointResult.AlreadyExists,
            await a2.TryInitializeAsync(scope, 1, V1));
        CandidateCheckpoint first = (await a2.ReadAsync(scope))!;
        Assert.Equal(1, first.Revision);
        Assert.Equal(V1, first.EncryptedEnvelope);

        Assert.Equal(CandidateCheckpointResult.Accepted,
            await b.TryInitializeAsync(tenantBScope, 1, V1));
        Assert.NotNull(await b.ReadAsync(tenantBScope));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            a.ReadAsync(tenantBScope));
        Assert.Null(await a.ReadAsync(scope with { IssuerId = "issuer-b" }));
        Assert.Null(await a.ReadAsync(scope with { WorkflowSha256 = new('b', 64) }));
        Assert.Null(await a.ReadAsync(scope with { ActorId = "actor-b" }));

        var outcomes = await Task.WhenAll(
            a.TryCommitAsync(scope, first.Revision, first.EnvelopeSha256, 2, V2),
            a2.TryCommitAsync(scope, first.Revision, first.EnvelopeSha256, 2, V3));
        Assert.Equal(1, outcomes.Count(x => x == CandidateCheckpointResult.Accepted));
        Assert.Equal(1, outcomes.Count(x => x == CandidateCheckpointResult.CasConflict));
        CandidateCheckpoint second = (await a.ReadAsync(scope))!;
        Assert.Equal(2, second.Revision);
        Assert.Equal(2, second.KeyVersion);
        Assert.Equal(CandidateCheckpointResult.RollbackDenied,
            await a2.TryCommitAsync(scope, 2, second.EnvelopeSha256, 2, V1));
        Assert.Equal(CandidateCheckpointResult.CasConflict,
            await a2.TryCommitAsync(scope, 1, first.EnvelopeSha256, 3, V3));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            a.TryCommitBoundedAsync(scope, 2, second.EnvelopeSha256, 3, V3, 4));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            a.TryCommitBoundedAsync(scope, 2, second.EnvelopeSha256, 3, V3,
                3, canceled.Token));

        // Sever a real PostgreSQL connection after UPDATE and before COMMIT.
        // This is NOT evidence of physical power-loss or separate physical hosts.
        var crashScope = scope with { CheckpointId = "crash-"+id };
        Assert.Equal(CandidateCheckpointResult.Accepted,
            await a.TryInitializeAsync(crashScope, 1, V1));
        CandidateCheckpoint before = (await a.ReadAsync(crashScope))!;
        a.BeforeCommitForTesting = async (connection, cancellation) =>
        {
            int pid = ((NpgsqlConnection)connection).ProcessID;
            await using var killer = await rootSource.OpenConnectionAsync(cancellation);
            await using var kill = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", killer);
            kill.Parameters.AddWithValue("pid", pid);
            Assert.Equal(true, await kill.ExecuteScalarAsync(cancellation));
        };
        await Assert.ThrowsAnyAsync<Exception>(() =>
            a.TryCommitAsync(crashScope, 1, before.EnvelopeSha256, 2, V2));
        a.BeforeCommitForTesting = null;
        CandidateCheckpoint after = (await a2.ReadAsync(crashScope))!;
        Assert.Equal(1, after.Revision);
        Assert.Equal(V1, after.EncryptedEnvelope);
        Assert.Equal(CandidateCheckpointResult.Accepted,
            await a2.TryCommitAsync(crashScope, 1, after.EnvelopeSha256, 2, V2));

        Assert.Equal(CandidateCheckpointResult.Accepted,
            await a.TryRevokeAsync(scope, second.Revision));
        CandidateCheckpoint revoked = (await a2.ReadAsync(scope))!;
        Assert.True(revoked.Revoked);
        Assert.Empty(revoked.EncryptedEnvelope);
        Assert.Equal(CandidateCheckpointResult.Revoked,
            await a.TryCommitAsync(scope, revoked.Revision,
                revoked.EnvelopeSha256, 3, V3));
        Assert.Equal(CandidateCheckpointResult.Revoked,
            await a.TryRevokeAsync(scope, revoked.Revision));

        // Fail closed even when a deployment accidentally disables the RLS policy.
        // Only this disposable test fixture can alter the temporary table.
        await using (var root = await rootSource.OpenConnectionAsync())
        await using (var disable = new NpgsqlCommand(
            "ALTER TABLE pmcro_checkpoint_candidate_v1 DISABLE ROW LEVEL SECURITY", root))
            await disable.ExecuteNonQueryAsync();
        try
        {
            Assert.Equal(CandidateCheckpointResult.PrincipalDenied,
                await a.TryInitializeAsync(
                    scope with { CheckpointId = "rls-disabled-" + id }, 1, V1));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => a.ReadAsync(scope));
        }
        finally
        {
            await using var root = await rootSource.OpenConnectionAsync();
            await using var enable = new NpgsqlCommand(
                "ALTER TABLE pmcro_checkpoint_candidate_v1 ENABLE ROW LEVEL SECURITY", root);
            await enable.ExecuteNonQueryAsync();
        }
    }
}
