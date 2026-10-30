using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ProjectName.Governance.Tests")]

namespace ProjectName.Persistence;

/// <summary>Strict composite owner of one opaque, already encrypted envelope.</summary>
public sealed record CandidateCheckpointScope(
    string IssuerId, string TenantId, string ActorId, string WorkflowSha256,
    string SessionId, string CheckpointId)
{
    public bool IsValid =>
        Valid(IssuerId) && Valid(TenantId) && Valid(ActorId) &&
        WorkflowSha256 is { Length: 64 } && WorkflowSha256.All(Uri.IsHexDigit) &&
        Valid(SessionId) && Valid(CheckpointId);

    private static bool Valid(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Length <= 128 && !text.Contains('*') && !text.Contains('\0');
}

public sealed record CandidateCheckpoint(
    CandidateCheckpointScope Scope, long Revision, int KeyVersion,
    byte[] EncryptedEnvelope, string EnvelopeSha256, bool Revoked);

public enum CandidateCheckpointResult
{
    Accepted, InvalidInput, AlreadyExists, NotFound, CasConflict,
    RollbackDenied, Revoked, PrincipalDenied
}

/// <summary>
/// Isolated, unregistered PostgreSQL development candidate. The caller must
/// provide an already encrypted envelope; this component never receives keys,
/// decrypts, creates schema, registers in DI or admits any production worker.
/// Every write uses a serializable transaction and all six scope components.
/// </summary>
public sealed class PostgresTransactionalCheckpointCandidate(DbDataSource source)
{
    private const int MaxEnvelopeBytes = 1024 * 1024;
    private const string Table = "pmcro_checkpoint_candidate_v1";
    private const string ScopeWhere = """
        issuer_id=@issuer AND tenant_id=@tenant AND actor_id=@actor
        AND workflow_sha256=@workflow AND session_id=@session
        AND checkpoint_id=@checkpoint
        """;

    // Friend assembly only; provides deterministic rollback evidence.
    internal Func<DbConnection, CancellationToken, Task>? BeforeCommitForTesting { get; set; }

    public async Task<CandidateCheckpointResult> TryInitializeAsync(
        CandidateCheckpointScope scope, int keyVersion,
        ReadOnlyMemory<byte> encryptedEnvelope, CancellationToken ct = default)
    {
        if (!scope.IsValid || keyVersion != 1 || !ValidEnvelope(encryptedEnvelope))
            return CandidateCheckpointResult.InvalidInput;
        try
        {
            await using DbConnection connection = await source.OpenConnectionAsync(ct);
            if (!await ScopeRoleVerifiedAsync(connection, scope, ct))
                return CandidateCheckpointResult.PrincipalDenied;
            await using DbTransaction transaction =
                await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await using DbCommand cmd = Command(connection, transaction, $"""
                INSERT INTO {Table}
                  (issuer_id,tenant_id,actor_id,workflow_sha256,session_id,
                   checkpoint_id,revision,key_version,envelope,envelope_sha256,revoked)
                VALUES (@issuer,@tenant,@actor,@workflow,@session,
                   @checkpoint,1,@version,@bytes,@digest,false)
                ON CONFLICT DO NOTHING
                """, scope);
            Add(cmd, "version", DbType.Int32, keyVersion);
            Add(cmd, "bytes", DbType.Binary, encryptedEnvelope.ToArray());
            Add(cmd, "digest", DbType.String, Hash(encryptedEnvelope.Span));
            int rows = await cmd.ExecuteNonQueryAsync(ct);
            if (rows != 1) return CandidateCheckpointResult.AlreadyExists;
            if (BeforeCommitForTesting is { } hook)
                await hook(connection, ct);
            await transaction.CommitAsync(ct);
            return CandidateCheckpointResult.Accepted;
        }
        catch (DbException ex) when (Retryable(ex))
        {
            return CandidateCheckpointResult.CasConflict;
        }
    }

    public async Task<CandidateCheckpoint?> ReadAsync(
        CandidateCheckpointScope scope, CancellationToken ct = default)
    {
        if (!scope.IsValid) return null;
        await using DbConnection connection = await source.OpenConnectionAsync(ct);
        if (!await ScopeRoleVerifiedAsync(connection, scope, ct))
            throw new UnauthorizedAccessException("Candidate tenant role or active RLS policy does not match scope.");
        await using DbCommand cmd = Command(connection, null, $"""
            SELECT revision,key_version,envelope,envelope_sha256,revoked
            FROM {Table} WHERE {ScopeWhere}
            """, scope);
        await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        byte[] envelope = (byte[])reader.GetValue(2);
        string digest = reader.GetString(3);
        if (!string.Equals(Hash(envelope), digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate checkpoint digest verification failed.");
        bool revoked = reader.GetBoolean(4);
        // Revocation denies payload disclosure even to this test-only caller.
        return new(scope, reader.GetInt64(0), reader.GetInt32(1),
            revoked ? [] : envelope, digest, revoked);
    }

    public async Task<CandidateCheckpointResult> TryCommitAsync(
        CandidateCheckpointScope scope, long expectedRevision,
        string expectedDigest, int newKeyVersion,
        ReadOnlyMemory<byte> encryptedEnvelope, CancellationToken ct = default)
    {
        if (!scope.IsValid || expectedRevision < 1 || newKeyVersion < 2 ||
            !ValidDigest(expectedDigest) || !ValidEnvelope(encryptedEnvelope))
            return CandidateCheckpointResult.InvalidInput;
        try
        {
            await using DbConnection connection = await source.OpenConnectionAsync(ct);
            if (!await ScopeRoleVerifiedAsync(connection, scope, ct))
                return CandidateCheckpointResult.PrincipalDenied;
            await using DbTransaction transaction =
                await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await using DbCommand cmd = Command(connection, transaction, $"""
                UPDATE {Table}
                SET revision=revision+1,key_version=@version,
                    envelope=@bytes,envelope_sha256=@digest
                WHERE {ScopeWhere} AND revision=@revision
                  AND envelope_sha256=@expected AND key_version < @version
                  AND revoked=false
                """, scope);
            Add(cmd, "version", DbType.Int32, newKeyVersion);
            Add(cmd, "bytes", DbType.Binary, encryptedEnvelope.ToArray());
            Add(cmd, "digest", DbType.String, Hash(encryptedEnvelope.Span));
            Add(cmd, "revision", DbType.Int64, expectedRevision);
            Add(cmd, "expected", DbType.String, expectedDigest.ToUpperInvariant());
            int rows = await cmd.ExecuteNonQueryAsync(ct);
            if (rows == 1)
            {
                if (BeforeCommitForTesting is { } hook)
                    await hook(connection, ct);
                await transaction.CommitAsync(ct);
                return CandidateCheckpointResult.Accepted;
            }
            return await ClassifyFailureAsync(scope, expectedRevision,
                newKeyVersion, ct);
        }
        catch (DbException ex) when (Retryable(ex))
        {
            return CandidateCheckpointResult.CasConflict;
        }
    }

    public async Task<CandidateCheckpointResult> TryCommitBoundedAsync(
        CandidateCheckpointScope scope, long expectedRevision,
        string expectedDigest, int newKeyVersion, ReadOnlyMemory<byte> envelope,
        int maxAttempts = 3, CancellationToken ct = default)
    {
        if (maxAttempts is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            CandidateCheckpointResult result = await TryCommitAsync(scope,
                expectedRevision, expectedDigest, newKeyVersion, envelope, ct);
            if (result != CandidateCheckpointResult.CasConflict)
                return result;
            if (attempt < maxAttempts - 1)
                await Task.Delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)), ct);
        }
        return CandidateCheckpointResult.CasConflict;
    }

    public async Task<CandidateCheckpointResult> TryRevokeAsync(
        CandidateCheckpointScope scope, long expectedRevision,
        CancellationToken ct = default)
    {
        if (!scope.IsValid || expectedRevision < 1)
            return CandidateCheckpointResult.InvalidInput;
        try
        {
            await using DbConnection connection = await source.OpenConnectionAsync(ct);
            if (!await ScopeRoleVerifiedAsync(connection, scope, ct))
                return CandidateCheckpointResult.PrincipalDenied;
            await using DbTransaction transaction =
                await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await using DbCommand cmd = Command(connection, transaction, $"""
                UPDATE {Table} SET revoked=true,revision=revision+1
                WHERE {ScopeWhere} AND revision=@revision AND revoked=false
                """, scope);
            Add(cmd, "revision", DbType.Int64, expectedRevision);
            int rows = await cmd.ExecuteNonQueryAsync(ct);
            if (rows == 1)
            {
                await transaction.CommitAsync(ct);
                return CandidateCheckpointResult.Accepted;
            }
            CandidateCheckpoint? old = await ReadAsync(scope, ct);
            return old is null ? CandidateCheckpointResult.NotFound :
                old.Revoked ? CandidateCheckpointResult.Revoked :
                CandidateCheckpointResult.CasConflict;
        }
        catch (DbException ex) when (Retryable(ex))
        {
            return CandidateCheckpointResult.CasConflict;
        }
    }

    private async Task<CandidateCheckpointResult> ClassifyFailureAsync(
        CandidateCheckpointScope scope, long revision, int proposedVersion,
        CancellationToken ct)
    {
        CandidateCheckpoint? current = await ReadAsync(scope, ct);
        if (current is null) return CandidateCheckpointResult.NotFound;
        if (current.Revoked) return CandidateCheckpointResult.Revoked;
        if (current.Revision != revision) return CandidateCheckpointResult.CasConflict;
        return proposedVersion <= current.KeyVersion
            ? CandidateCheckpointResult.RollbackDenied
            : CandidateCheckpointResult.CasConflict;
    }

    private static async Task<bool> ScopeRoleVerifiedAsync(
        DbConnection connection, CandidateCheckpointScope scope, CancellationToken ct)
    {
        // A privileged database user bypasses RLS and MUST NOT run this candidate.
        // The exact tenant-scoped login role is mandatory for every operation.
        await using DbCommand probe = connection.CreateCommand();
        probe.CommandText = "SELECT current_user, row_security_active('pmcro_checkpoint_candidate_v1')";
        await using DbDataReader reader = await probe.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return false;
        return string.Equals(reader.GetString(0), scope.TenantId,
                StringComparison.Ordinal) && reader.GetBoolean(1);
    }

    private static DbCommand Command(DbConnection connection,
        DbTransaction? transaction, string sql, CandidateCheckpointScope scope)
    {
        DbCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        Add(cmd, "issuer", DbType.String, scope.IssuerId);
        Add(cmd, "tenant", DbType.String, scope.TenantId);
        Add(cmd, "actor", DbType.String, scope.ActorId);
        Add(cmd, "workflow", DbType.String, scope.WorkflowSha256.ToUpperInvariant());
        Add(cmd, "session", DbType.String, scope.SessionId);
        Add(cmd, "checkpoint", DbType.String, scope.CheckpointId);
        return cmd;
    }

    private static void Add(DbCommand cmd, string name, DbType type, object value)
    {
        DbParameter p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static string Hash(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data));

    private static bool ValidDigest(string? digest) =>
        digest is { Length: 64 } && digest.All(Uri.IsHexDigit);

    private static bool ValidEnvelope(ReadOnlyMemory<byte> envelope) =>
        envelope.Length is > 16 and <= MaxEnvelopeBytes;

    private static bool Retryable(DbException error) =>
        error.SqlState is "40001" or "40P01";
}
