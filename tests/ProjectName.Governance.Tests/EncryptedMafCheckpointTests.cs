using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using ProjectName.ControlPlane;
using ProjectName.Runtime;

namespace ProjectName.Governance.Tests;

public sealed class EncryptedMafCheckpointTests
{
    [Fact]
    public async Task Native_five_phase_checkpoints_are_encrypted_tenant_bound_and_resumable()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "ProjectNameEncryptedCheckpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var alice = new VerifiedIdentity("tenant-a", "alice", "issuer-1");
        var bob = new VerifiedIdentity("tenant-b", "alice", "issuer-1");
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PMCR-O-DRYRUN-V1")));
        try
        {
            var vault = new EncryptedTenantStateStore(directory, key);
            var store = new EncryptedMafCheckpointStore(vault, alice, digest);
            var manager = CheckpointManager.CreateJson(store);
            await using StreamingRun first = await InProcessExecution.RunStreamingAsync(
                PmcroDryRunWorkflow.Create(),
                new PmcroDryRunInput("TS-ENC-1", "PRIVATE_CHECKPOINT_MESSAGE"),
                manager);
            await foreach (WorkflowEvent _ in first.WatchStreamAsync()) { }
            Assert.True(first.Checkpoints.Count >= 4);
            CheckpointInfo earlier = first.Checkpoints[1];
            Assert.NotEmpty(Directory.EnumerateFiles(directory, "*.enc"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.json"));

            var anotherTenant = new EncryptedMafCheckpointStore(vault, bob, digest);
            Assert.Empty(await anotherTenant.RetrieveIndexAsync(earlier.SessionId, null));

            // Simulate restart with a new store/manager object, same external key.
            var restarted = new EncryptedMafCheckpointStore(
                new EncryptedTenantStateStore(directory, key), alice, digest);
            var manager2 = CheckpointManager.CreateJson(restarted);
            await using StreamingRun resumed = await InProcessExecution.ResumeStreamingAsync(
                PmcroDryRunWorkflow.Create(), earlier, manager2);
            var results = new List<PmcroDryRunOutcome>();
            await foreach (WorkflowEvent evt in resumed.WatchStreamAsync())
            {
                if (evt is WorkflowOutputEvent output && output.Data is PmcroDryRunOutcome outcome)
                    results.Add(outcome);
            }
            Assert.Contains(results, x => x.Verdict == "HALT" && !x.ProductionExecutionAuthorized);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }


    [Fact]
    public async Task Competing_store_instances_do_not_lose_checkpoint_index_entries()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "ProjectNameEncryptedCheckpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] key = RandomNumberGenerator.GetBytes(32);
            var owner = new VerifiedIdentity("tenant-a", "alice", "issuer-1");
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("WORKFLOW-CONCURRENT")));
            const string session = "TS-CONCURRENT";
            using var doc = System.Text.Json.JsonDocument.Parse("{\"kind\":\"concurrency-test\"}");

            Task<CheckpointInfo>[] tasks = Enumerable.Range(0, 20).Select(async _ =>
            {
                var vault = new EncryptedTenantStateStore(directory, key);
                var instance = new EncryptedMafCheckpointStore(vault, owner, digest);
                return await instance.CreateCheckpointAsync(session, doc.RootElement.Clone(), null);
            }).ToArray();
            CheckpointInfo[] committed = await Task.WhenAll(tasks);
            Assert.Equal(20, committed.Select(x => x.CheckpointId).Distinct().Count());

            var restarted = new EncryptedMafCheckpointStore(
                new EncryptedTenantStateStore(directory, key), owner, digest);
            CheckpointInfo[] indexed = (await restarted.RetrieveIndexAsync(session, null)).ToArray();
            Assert.Equal(20, indexed.Length);
            Assert.Equal(
                committed.Select(x => x.CheckpointId).OrderBy(x => x),
                indexed.Select(x => x.CheckpointId).OrderBy(x => x));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Separate_dotnet_processes_commit_one_encrypted_index_without_lost_entries()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "ProjectNameCrossProcessCheckpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        Process? first = null;
        Process? second = null;
        try
        {
            var owner = new VerifiedIdentity("tenant-a", "alice", "issuer-1");
            string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("WORKFLOW-CROSS-PROCESS")));
            const string session = "TS-MULTIPROCESS";
            var store = new EncryptedMafCheckpointStore(
                new EncryptedTenantStateStore(directory, key), owner, digest);
            await store.CreateCheckpointAsync(session,
                JsonSerializer.SerializeToElement(new { source = "initial", ordinal = -1 }), null);

            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            string harness = Path.Combine(repository, "tests", "ProjectName.CheckpointProbe",
                "bin", configuration, "net10.0", "ProjectName.CheckpointProbe.dll");
            Assert.True(File.Exists(harness), "Build the test-only CheckpointProbe project first.");

            string ready1 = Path.Combine(directory, "writer-one.ready");
            string ready2 = Path.Combine(directory, "writer-two.ready");
            string gate = Path.Combine(directory, "release.gate");
            string key64 = Convert.ToBase64String(key);
            first = StartProbe(harness, JsonSerializer.Serialize(new
            {
                StateDirectory = directory, KeyBase64 = key64, WorkflowDigest = digest,
                SessionId = session, WorkerId = "one", Count = 12, ReadyPath = ready1,
                GatePath = gate
            }));
            second = StartProbe(harness, JsonSerializer.Serialize(new
            {
                StateDirectory = directory, KeyBase64 = key64, WorkflowDigest = digest,
                SessionId = session, WorkerId = "two", Count = 12, ReadyPath = ready2,
                GatePath = gate
            }));

            var readyClock = Stopwatch.StartNew();
            while (!File.Exists(ready1) || !File.Exists(ready2))
            {
                if (first.HasExited || second.HasExited ||
                    readyClock.Elapsed > TimeSpan.FromSeconds(15))
                    throw new InvalidOperationException("Checkpoint child failed to reach test gate.");
                await Task.Delay(20);
            }
            await File.WriteAllTextAsync(gate, "START");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await Task.WhenAll(
                first.WaitForExitAsync(timeout.Token),
                second.WaitForExitAsync(timeout.Token));
            string errors = (await first.StandardError.ReadToEndAsync()) + " " +
                (await second.StandardError.ReadToEndAsync());
            Assert.True(first.ExitCode == 0 && second.ExitCode == 0,
                "Child exit codes: " + first.ExitCode + ", " + second.ExitCode +
                ". Errors: " + errors);

            var restarted = new EncryptedMafCheckpointStore(
                new EncryptedTenantStateStore(directory, key), owner, digest);
            CheckpointInfo[] index = (await restarted.RetrieveIndexAsync(session, null)).ToArray();
            Assert.Equal(25, index.Length);
            Assert.Equal(25, index.Select(x => x.CheckpointId).Distinct().Count());
            var sources = new List<string>();
            foreach (CheckpointInfo info in index)
            {
                JsonElement checkpoint = await restarted.RetrieveCheckpointAsync(session, info);
                sources.Add(checkpoint.GetProperty("source").GetString()!);
            }
            Assert.Equal(1, sources.Count(x => x == "initial"));
            Assert.Equal(12, sources.Count(x => x == "one"));
            Assert.Equal(12, sources.Count(x => x == "two"));
        }
        finally
        {
            foreach (Process? child in new[] { first, second })
            {
                if (child is null) continue;
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
                child.Dispose();
            }
            CryptographicOperations.ZeroMemory(key);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Killed_child_after_payload_write_cannot_publish_orphan_index_entry()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "ProjectNameKilledCheckpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        Process? child = null;
        try
        {
            var owner = new VerifiedIdentity("tenant-a", "alice", "issuer-1");
            string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("WORKFLOW-KILLED-COMMIT")));
            const string session = "TS-KILLED-COMMIT";
            var vault = new EncryptedTenantStateStore(directory, key);
            var store = new EncryptedMafCheckpointStore(vault, owner, digest);
            CheckpointInfo before = await store.CreateCheckpointAsync(session,
                JsonSerializer.SerializeToElement(new { source = "before" }), null);

            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            string harness = Path.Combine(repository, "tests", "ProjectName.CheckpointProbe",
                "bin", configuration, "net10.0", "ProjectName.CheckpointProbe.dll");
            Assert.True(File.Exists(harness));
            string ready = Path.Combine(directory, "fault.ready");
            string gate = Path.Combine(directory, "fault.gate");
            string signal = Path.Combine(directory, "fault-opaque-checkpoint-id");
            child = StartProbe(harness, JsonSerializer.Serialize(new
            {
                StateDirectory = directory, KeyBase64 = Convert.ToBase64String(key),
                WorkflowDigest = digest, SessionId = session,
                WorkerId = "killed", Count = 1, ReadyPath = ready,
                GatePath = gate, Mode = "kill-after-payload", SignalPath = signal
            }));

            var readyClock = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                if (child.HasExited || readyClock.Elapsed > TimeSpan.FromSeconds(15))
                    throw new InvalidOperationException("Fault-injection child never became ready.");
                await Task.Delay(20);
            }
            await File.WriteAllTextAsync(gate, "START");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await child.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, child.ExitCode);
            Assert.True(File.Exists(signal), "Child did not reach post-payload fault seam.");
            string orphanId = (await File.ReadAllTextAsync(signal)).Trim();
            Assert.NotEmpty(orphanId);

            var restarted = new EncryptedMafCheckpointStore(
                new EncryptedTenantStateStore(directory, key), owner, digest);
            Assert.Equal(new[] { before.CheckpointId },
                (await restarted.RetrieveIndexAsync(session, null))
                    .Select(x => x.CheckpointId).ToArray());
            await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await restarted.RetrieveCheckpointAsync(session,
                    new CheckpointInfo(session, orphanId)));
            // Two committed envelopes (index + first payload), plus orphan.
            Assert.Equal(3, Directory.GetFiles(directory, "*.enc").Length);

            await restarted.CreateCheckpointAsync(session,
                JsonSerializer.SerializeToElement(new { source = "after" }), null);
            Assert.Equal(2, (await store.RetrieveIndexAsync(session, null)).Count());
            Assert.Empty(Directory.GetFiles(directory, "*.json"));
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
                child.Dispose();
            }
            CryptographicOperations.ZeroMemory(key);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Process StartProbe(string harness, string input)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(harness);
        Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start test-only checkpoint process.");
        process.StandardInput.WriteLine(input);
        process.StandardInput.Close();
        return process;
    }

    [Fact]
    public async Task Payload_committed_without_index_is_not_exposed_and_later_commits_survive()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "ProjectNameOrphanCheckpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] key = RandomNumberGenerator.GetBytes(32);
            var owner = new VerifiedIdentity("tenant-a", "alice", "issuer-1");
            string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("WORKFLOW-ORPHAN")));
            const string session = "TS-ORPHAN";
            var vault = new EncryptedTenantStateStore(directory, key);
            var store = new EncryptedMafCheckpointStore(vault, owner, digest);
            CheckpointInfo committed = await store.CreateCheckpointAsync(session,
                JsonSerializer.SerializeToElement(new { source = "committed" }), null);

            // Model interruption after ciphertext-payload commit, before index commit.
            const string orphanId = "unindexed-payload";
            await vault.SaveAsync(owner, "workflow:" + digest,
                TestCheckpointStateKey(session, orphanId),
                JsonSerializer.SerializeToUtf8Bytes(new { source = "unindexed" }));
            var restarted = new EncryptedMafCheckpointStore(
                new EncryptedTenantStateStore(directory, key), owner, digest);

            await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await restarted.RetrieveCheckpointAsync(session,
                    new CheckpointInfo(session, orphanId)));
            Assert.Equal(new[] { committed.CheckpointId },
                (await restarted.RetrieveIndexAsync(session, null))
                    .Select(x => x.CheckpointId).ToArray());

            await restarted.CreateCheckpointAsync(session,
                JsonSerializer.SerializeToElement(new { source = "after-interruption" }), null);
            Assert.Equal(2, (await store.RetrieveIndexAsync(session, null)).Count());
            Assert.Empty(Directory.GetFiles(directory, "*.json"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string TestCheckpointStateKey(string session, string itemId)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("maf-workflow-checkpoint-v1");
            writer.Write(session);
            writer.Write(itemId);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    [Fact]
    public async Task Corrupted_checkpoint_index_is_rejected_and_workflow_digest_isolation_holds()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "ProjectNameEncryptedCheckpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var owner = new VerifiedIdentity("tenant-a", "alice", "issuer-1");
            var vault = new EncryptedTenantStateStore(directory, RandomNumberGenerator.GetBytes(32));
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("WORKFLOW-V1")));
            var store = new EncryptedMafCheckpointStore(vault, owner, digest);
            using var doc = System.Text.Json.JsonDocument.Parse("{\"kind\":\"mock-checkpoint\"}");
            CheckpointInfo created = await store.CreateCheckpointAsync("TS-TAMPER", doc.RootElement.Clone(), null);
            CheckpointInfo foreignParent = new("OTHER-SESSION", created.CheckpointId);
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.CreateCheckpointAsync("TS-TAMPER", doc.RootElement.Clone(), foreignParent));
            await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await store.RetrieveCheckpointAsync("TS-TAMPER", foreignParent));
            Assert.Empty(await store.RetrieveIndexAsync("TS-TAMPER", foreignParent));

            string otherDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("WORKFLOW-V2")));
            var wrongGraph = new EncryptedMafCheckpointStore(vault, owner, otherDigest);
            Assert.Empty(await wrongGraph.RetrieveIndexAsync("TS-TAMPER", null));

            foreach (string path in Directory.EnumerateFiles(directory, "*.enc"))
            {
                byte[] cipher = await File.ReadAllBytesAsync(path);
                cipher[^1] ^= 1;
                await File.WriteAllBytesAsync(path, cipher);
            }
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.RetrieveIndexAsync("TS-TAMPER", null));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}