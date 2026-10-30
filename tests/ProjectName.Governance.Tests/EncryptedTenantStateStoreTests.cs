using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ProjectName.ControlPlane;

namespace ProjectName.Governance.Tests;

public sealed class EncryptedTenantStateStoreTests
{
    private static readonly VerifiedIdentity Alice = new("tenant-a", "alice", "issuer-1");
    private static readonly VerifiedIdentity Bob = new("tenant-b", "alice", "issuer-1");

    [Fact]
    public async Task Tenant_bound_save_load_and_delete_survives_new_store_instance()
    {
        string directory = NewTemporaryDirectory();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var first = new EncryptedTenantStateStore(directory, key);
            byte[] sensitive = Encoding.UTF8.GetBytes("Private agent session for tenant A");
            await first.SaveAsync(Alice, "agent:1", "same-thread-id", sensitive);
            var restarted = new EncryptedTenantStateStore(directory, key);
            Assert.Equal(sensitive, await restarted.LoadAsync(Alice, "agent:1", "same-thread-id"));
            Assert.Null(await restarted.LoadAsync(Bob, "agent:1", "same-thread-id"));
            Assert.Null(await restarted.LoadAsync(Alice, "agent:2", "same-thread-id"));
            Assert.Null(await restarted.LoadAsync(Alice, "agent:1", "other-thread"));
            string encryptedPath = Assert.Single(Directory.GetFiles(directory, "*.enc"));
            byte[] encryptedBytes = await File.ReadAllBytesAsync(encryptedPath);
            Assert.True(encryptedBytes.AsSpan().IndexOf(sensitive) < 0,
                "Encrypted file must not contain the plaintext payload.");
            await restarted.DeleteAsync(Alice, "agent:1", "same-thread-id");
            Assert.Null(await first.LoadAsync(Alice, "agent:1", "same-thread-id"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Tampered_ciphertext_fails_authenticated_decryption()
    {
        string directory = NewTemporaryDirectory();
        try
        {
            var vault = new EncryptedTenantStateStore(directory, RandomNumberGenerator.GetBytes(32));
            await vault.SaveAsync(Alice, "agent", "thread", Encoding.UTF8.GetBytes("secret"));
            string path = Assert.Single(Directory.GetFiles(directory, "*.enc"));
            byte[] content = await File.ReadAllBytesAsync(path);
            content[^1] ^= 0x01;
            await File.WriteAllBytesAsync(path, content);
            await Assert.ThrowsAsync<InvalidDataException>(() => vault.LoadAsync(Alice, "agent", "thread"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Missing_key_or_protected_directory_fails_closed()
    {
        string directory = NewTemporaryDirectory();
        try
        {
            Assert.Throws<ArgumentException>(() =>
                new EncryptedTenantStateStore(directory, new byte[8]));
            Assert.Throws<InvalidOperationException>(() =>
                new EncryptedTenantStateStore(Path.Combine(directory, "missing"), new byte[32]));
            var vault = new EncryptedTenantStateStore(directory, RandomNumberGenerator.GetBytes(32));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                vault.SaveAsync(new VerifiedIdentity("", "actor", "issuer"),
                    "agent", "thread", Encoding.UTF8.GetBytes("x")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }


    [Fact]
    public async Task Exclusive_file_lease_times_out_and_recovers_after_owner_releases_it()
    {
        string directory = NewTemporaryDirectory();
        try
        {
            byte[] key = RandomNumberGenerator.GetBytes(32);
            var first = new EncryptedTenantStateStore(directory, key);
            var second = new EncryptedTenantStateStore(directory, key);
            await using (FileStream held = await first.AcquireExclusiveLeaseAsync(
                Alice, "workflow:test", "index:one", TimeSpan.FromSeconds(1)))
            {
                await Assert.ThrowsAsync<TimeoutException>(async () =>
                    await second.AcquireExclusiveLeaseAsync(
                        Alice, "workflow:test", "index:one", TimeSpan.FromMilliseconds(120)));
                await using FileStream separateTenant = await second.AcquireExclusiveLeaseAsync(
                    Bob, "workflow:test", "index:one", TimeSpan.FromSeconds(1));
            }
            await using FileStream recovered = await second.AcquireExclusiveLeaseAsync(
                Alice, "workflow:test", "index:one", TimeSpan.FromSeconds(1));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Windows_separate_process_cannot_take_a_held_file_lease()
    {
        if (!OperatingSystem.IsWindows()) return;

        string directory = NewTemporaryDirectory();
        try
        {
            var vault = new EncryptedTenantStateStore(directory, RandomNumberGenerator.GetBytes(32));
            string lockPath;
            await using (FileStream held = await vault.AcquireExclusiveLeaseAsync(
                Alice, "workflow:probe", "index:process", TimeSpan.FromSeconds(1)))
            {
                lockPath = Assert.Single(Directory.GetFiles(directory, "*.lock"));
                Assert.Equal(7, await ProbeExclusiveOpenInWindowsChildAsync(lockPath));
            }

            Assert.Equal(0, await ProbeExclusiveOpenInWindowsChildAsync(lockPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Windows_terminated_lock_holder_releases_lease_for_recovery()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = NewTemporaryDirectory();
        try
        {
            var vault = new EncryptedTenantStateStore(directory, RandomNumberGenerator.GetBytes(32));
            string lockPath;
            await using (FileStream initial = await vault.AcquireExclusiveLeaseAsync(
                Alice, "workflow:crash", "index:process", TimeSpan.FromSeconds(1)))
                lockPath = Assert.Single(Directory.GetFiles(directory, "*.lock"));

            string path64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(lockPath));
            string script = "$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" +
                path64 + "')); $s=[IO.File]::Open($p,[IO.FileMode]::OpenOrCreate," +
                "[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); " +
                "[Console]::Out.WriteLine('LEASE_HELD'); [Console]::Out.Flush(); " +
                "Start-Sleep -Seconds 30; $s.Dispose()";
            using var child = new Process();
            child.StartInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            child.StartInfo.ArgumentList.Add("-NoProfile");
            child.StartInfo.ArgumentList.Add("-NonInteractive");
            child.StartInfo.ArgumentList.Add("-EncodedCommand");
            child.StartInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            Assert.True(child.Start(), "Windows lease-holder process failed to start.");
            try
            {
                string? ready = await child.StandardOutput.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(8));
                Assert.Equal("LEASE_HELD", ready);
                await Assert.ThrowsAsync<TimeoutException>(async () =>
                    await vault.AcquireExclusiveLeaseAsync(
                        Alice, "workflow:crash", "index:process",
                        TimeSpan.FromMilliseconds(150)));
            }
            finally
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }

            await using FileStream recovered = await vault.AcquireExclusiveLeaseAsync(
                Alice, "workflow:crash", "index:process", TimeSpan.FromSeconds(2));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Abandoned_temp_and_cancelled_stage_preserve_committed_encrypted_state()
    {
        string directory = NewTemporaryDirectory();
        try
        {
            byte[] key = RandomNumberGenerator.GetBytes(32);
            var vault = new EncryptedTenantStateStore(directory, key);
            byte[] first = Encoding.UTF8.GetBytes("committed-before-interruption");
            byte[] second = Encoding.UTF8.GetBytes("replacement-after-interruption");
            await vault.SaveAsync(Alice, "agent", "session", first);
            string committedFile = Assert.Single(Directory.GetFiles(directory, "*.enc"));
            byte[] committedCiphertext = await File.ReadAllBytesAsync(committedFile);

            // A process stopped before atomic rename can leave an opaque temporary file.
            await File.WriteAllBytesAsync(Path.Combine(directory, "abandoned.tmp"),
                RandomNumberGenerator.GetBytes(128));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                vault.SaveAsync(Alice, "agent", "session", second, cancelled.Token));

            var restarted = new EncryptedTenantStateStore(directory, key);
            Assert.Equal(first, await restarted.LoadAsync(Alice, "agent", "session"));
            Assert.Equal(committedCiphertext, await File.ReadAllBytesAsync(committedFile));
            Assert.Single(Directory.GetFiles(directory, "*.tmp"));

            await restarted.SaveAsync(Alice, "agent", "session", second);
            Assert.Equal(second, await vault.LoadAsync(Alice, "agent", "session"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<int> ProbeExclusiveOpenInWindowsChildAsync(string lockPath)
    {
        string pathBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(lockPath));
        string script =
            "$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + pathBase64 + "')); " +
            "try { $s=[IO.File]::Open($p,[IO.FileMode]::OpenOrCreate," +
            "[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); $s.Dispose(); exit 0 } " +
            "catch [System.IO.IOException] { exit 7 } " +
            "catch [System.UnauthorizedAccessException] { exit 7 } catch { exit 9 }";
        using var child = new Process();
        child.StartInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        child.StartInfo.ArgumentList.Add("-NoProfile");
        child.StartInfo.ArgumentList.Add("-NonInteractive");
        child.StartInfo.ArgumentList.Add("-EncodedCommand");
        child.StartInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        Assert.True(child.Start(), "Windows child lock probe failed to start.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await child.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            throw;
        }
        return child.ExitCode;
    }

    private static string NewTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ProjectNameVaultTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}