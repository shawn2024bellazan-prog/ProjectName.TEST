using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using ProjectName.ControlPlane;

namespace ProjectName.Runtime;

/// <summary>
/// Encrypted JSON checkpoint store for a host-verified tenant.
/// Local cooperating processes serialize index updates with an exclusive file
/// lease; no plaintext FileSystemJsonCheckpointStore is used.
/// Candidate-only: multi-host transactions, secure key management, ACL
/// verification and Checker authorization are still unconfigured.
/// </summary>
public sealed class EncryptedMafCheckpointStore(
    EncryptedTenantStateStore vault,
    VerifiedIdentity verifiedOwner,
    string boundWorkflowDigest) : ICheckpointStore<JsonElement>
{
    private readonly string _agentScope = RequireDigest(boundWorkflowDigest);

    // Test-only, friend-assembly fault seam; never configured by production DI.
    internal Action<CheckpointInfo>? AfterPayloadSavedForTesting { get; set; }

    public async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId, JsonElement value, CheckpointInfo? parent)
    {
        if (parent is not null && !string.Equals(parent.SessionId, sessionId, StringComparison.Ordinal))
            throw new ArgumentException("Cross-session checkpoint parent is forbidden.", nameof(parent));
        string indexKey = StateKey(sessionId, "index");
        var created = new CheckpointInfo(sessionId, Guid.NewGuid().ToString("N"));
        await using (FileStream lease = await vault.AcquireExclusiveLeaseAsync(
            verifiedOwner, _agentScope, indexKey, TimeSpan.FromSeconds(10)))
        {
            var index = await LoadIndexAsync(sessionId);
            // Persist payload before updating the index; an interrupted commit
            // can leave an unreferenced encrypted blob, never a half-written index.
            await vault.SaveAsync(verifiedOwner, _agentScope,
                StateKey(sessionId, created.CheckpointId),
                Encoding.UTF8.GetBytes(value.GetRawText()));
            AfterPayloadSavedForTesting?.Invoke(created);
            index.Add(new IndexEntry(created.CheckpointId, parent?.CheckpointId));
            await vault.SaveAsync(verifiedOwner, _agentScope, indexKey,
                JsonSerializer.SerializeToUtf8Bytes(index));
        }
        return created;
    }

    public async ValueTask<JsonElement> RetrieveCheckpointAsync(
        string sessionId, CheckpointInfo key)
    {
        if (!string.Equals(key.SessionId, sessionId, StringComparison.Ordinal))
            throw new KeyNotFoundException("Checkpoint session mismatch.");
        var index = await LoadIndexAsync(sessionId);
        if (!index.Any(entry => entry.Id == key.CheckpointId))
            throw new KeyNotFoundException("Checkpoint was not found in this session.");
        byte[] bytes = await vault.LoadAsync(verifiedOwner, _agentScope,
            StateKey(sessionId, key.CheckpointId))
            ?? throw new KeyNotFoundException("Checkpoint payload not found.");
        using JsonDocument document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }

    public async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId, CheckpointInfo? withParent)
    {
        if (withParent is not null &&
            !string.Equals(withParent.SessionId, sessionId, StringComparison.Ordinal))
            return [];
        var index = await LoadIndexAsync(sessionId);
        return index
            .Where(entry => withParent is null || entry.ParentId == withParent.CheckpointId)
            .Select(entry => new CheckpointInfo(sessionId, entry.Id))
            .ToArray();
    }

    private async Task<List<IndexEntry>> LoadIndexAsync(string sessionId)
    {
        byte[]? bytes = await vault.LoadAsync(verifiedOwner, _agentScope, StateKey(sessionId, "index"));
        return bytes is null ? [] :
            JsonSerializer.Deserialize<List<IndexEntry>>(bytes)
                ?? throw new InvalidDataException("Checkpoint index is invalid.");
    }

    private static string RequireDigest(string digest)
    {
        if (string.IsNullOrWhiteSpace(digest) || digest.Length != 64 ||
            !digest.All(Uri.IsHexDigit))
            throw new ArgumentException("A bound SHA-256 workflow digest is required.", nameof(digest));
        return "workflow:" + digest.ToUpperInvariant();
    }

    private static string StateKey(string sessionId, string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("maf-workflow-checkpoint-v1");
            writer.Write(sessionId);
            writer.Write(itemId);
        }
        return Convert.ToHexString(SHA256.HashData(memory.ToArray()));
    }

    public sealed record IndexEntry(string Id, string? ParentId);
}