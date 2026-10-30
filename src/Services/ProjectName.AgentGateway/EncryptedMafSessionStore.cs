using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using ProjectName.ControlPlane;

// MAF 1.26's session-store API is experimental. This opt-in adapter is
// intentionally not registered or production activated.
#pragma warning disable MAAI001
namespace ProjectName.AgentGateway;

/// <summary>
/// Native MAF AgentSessionStore backed by authenticated tenant-scoped ciphertext.
/// Not registered in the default host: identity issuer and encryption key remain
/// unprovisioned, and all protocol endpoints are intentionally unmounted.
/// </summary>
public sealed class EncryptedMafSessionStore(
    EncryptedTenantStateStore vault,
    IHttpContextAccessor httpContext,
    IVerifiedIdentityResolver trustedIdentity) : AgentSessionStore
{
    public override async ValueTask SaveSessionAsync(
        AIAgent agent, AgentSessionStoreKey key, AgentSession session,
        CancellationToken cancellationToken = default)
    {
        VerifiedIdentity caller = RequireVerifiedCaller();
        JsonElement snapshot = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        byte[] content = Encoding.UTF8.GetBytes(snapshot.GetRawText());
        await vault.SaveAsync(caller, agent.Id, StableKey(key), content, cancellationToken);
    }

    public override async ValueTask<AgentSession?> GetSessionAsync(
        AIAgent agent, AgentSessionStoreKey key,
        CancellationToken cancellationToken = default)
    {
        VerifiedIdentity caller = RequireVerifiedCaller();
        byte[]? content = await vault.LoadAsync(caller, agent.Id, StableKey(key), cancellationToken);
        if (content is null) return null;
        using JsonDocument doc = JsonDocument.Parse(content);
        // Always produce an independent session instance, not shared mutable state.
        return await agent.DeserializeSessionAsync(doc.RootElement.Clone(), cancellationToken: cancellationToken);
    }

    public override async ValueTask DeleteSessionAsync(
        AIAgent agent, AgentSessionStoreKey key,
        CancellationToken cancellationToken = default)
    {
        VerifiedIdentity caller = RequireVerifiedCaller();
        await vault.DeleteAsync(caller, agent.Id, StableKey(key), cancellationToken);
    }

    private VerifiedIdentity RequireVerifiedCaller()
    {
        ClaimsPrincipal? principal = httpContext.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("UNAUTHENTICATED");

        VerifiedIdentity? identity = trustedIdentity.Resolve(principal);
        if (identity is null ||
            string.IsNullOrWhiteSpace(identity.TenantId) ||
            string.IsNullOrWhiteSpace(identity.ActorId) ||
            string.IsNullOrWhiteSpace(identity.IssuerId) ||
            !string.Equals(identity.ActorId,
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("UNVERIFIED_IDENTITY_OR_TENANT");
        return identity;
    }

    private static string StableKey(AgentSessionStoreKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        // An opaque protocol continuation ID plus sorted partition pairs.
        // Length-prefixed field serialization avoids collisions on ':' or '/'.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("maf-session-key-v1");
            writer.Write(key.SessionId);
            if (key.Partitions is null) writer.Write(0);
            else
            {
                writer.Write(key.Partitions.Count);
                foreach (KeyValuePair<string, string> entry in
                    key.Partitions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.Write(entry.Key);
                    writer.Write(entry.Value);
                }
            }
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray()));
    }
}
#pragma warning restore MAAI001