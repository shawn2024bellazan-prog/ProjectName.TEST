using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ProjectName.ControlPlane;
using ProjectName.Runtime;

// Test-only process probe. Not registered in AppHost or any production DI.
try
{
    string line = await Console.In.ReadLineAsync()
        ?? throw new InvalidDataException("Missing test probe request.");
    var input = JsonSerializer.Deserialize<ProbeRequest>(line)
        ?? throw new InvalidDataException("Invalid test probe request.");
    if (input.Count is < 1 or > 30)
        throw new ArgumentOutOfRangeException(nameof(input.Count));
    byte[] key = Convert.FromBase64String(input.KeyBase64);
    try
    {
        var store = new EncryptedMafCheckpointStore(
            new EncryptedTenantStateStore(input.StateDirectory, key),
            new VerifiedIdentity("tenant-a", "alice", "issuer-1"), input.WorkflowDigest);
        if (input.Mode == "kill-after-payload")
        {
            if (string.IsNullOrWhiteSpace(input.SignalPath))
                throw new InvalidDataException("Fault probe requires signal path.");
            store.AfterPayloadSavedForTesting = checkpoint =>
            {
                // Emit only opaque checkpoint ID after encrypted payload is durable.
                File.WriteAllText(input.SignalPath, checkpoint.CheckpointId);
                Process.GetCurrentProcess().Kill();
                throw new InvalidOperationException("Fault probe did not terminate.");
            };
        }
        else if (input.Mode is not null)
        {
            throw new InvalidDataException("Unrecognized test-only probe mode.");
        }

        await File.WriteAllTextAsync(input.ReadyPath, "READY");
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(input.GatePath))
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(15))
                throw new TimeoutException("Test-only process gate not released.");
            await Task.Delay(15);
        }

        for (int i = 0; i < input.Count; i++)
        {
            var payload = JsonSerializer.SerializeToElement(new
            {
                source = input.WorkerId,
                ordinal = i,
                phase = "CHECKPOINT-PROBE"
            });
            await store.CreateCheckpointAsync(input.SessionId, payload, null);
        }
        Console.WriteLine("COMMITTED:" + input.Count);
    }
    finally { CryptographicOperations.ZeroMemory(key); }
}
catch (Exception error)
{
    Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
    Environment.ExitCode = 2;
}

public sealed record ProbeRequest(
    string StateDirectory, string KeyBase64, string WorkflowDigest,
    string SessionId, string WorkerId, int Count,
    string ReadyPath, string GatePath, string? Mode = null,
    string? SignalPath = null);
