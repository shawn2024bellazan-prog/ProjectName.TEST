using System.Collections.Concurrent;
using ProjectName.ControlPlane;

namespace ProjectName.Governance.Tests;

public sealed class FileMcpApprovalUseStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);

    private static McpToolInvocation Request() => new(
        "TS-TEST", "windows-principal", "mcp-filesystem", "read_file",
        "G:/ProjectName/docs", "sha256:" + new string('a', 64),
        "sha256:" + new string('b', 64), "invocation-once", true);

    private static HostVerifiedMcpHumanApproval Approval() => new(
        "grant-once", "invocation-once", "sha256:" + new string('b', 64),
        "distinct-human", "EV-HUMAN", Now.AddMinutes(5));

    [Fact]
    public void Ledger_requires_pre_existing_host_managed_absolute_directory()
    {
        Assert.Throws<ArgumentException>(() => new FileMcpApprovalUseStore("relative-ledger"));
        string missing = Path.Combine(Path.GetTempPath(), "pmcro-never-created-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<DirectoryNotFoundException>(() => new FileMcpApprovalUseStore(missing));
    }

    [Fact]
    public void Single_use_claim_survives_new_store_instance()
    {
        string directory = NewTestLedger();
        try
        {
            Assert.True(new FileMcpApprovalUseStore(directory).TryConsume(Approval(), Request(), Now));
            Assert.False(new FileMcpApprovalUseStore(directory).TryConsume(Approval(), Request(), Now));
            Assert.Single(Directory.GetFiles(directory, "*.used"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Parallel_claims_have_exactly_one_winner()
    {
        string directory = NewTestLedger();
        try
        {
            var results = new ConcurrentBag<bool>();
            Parallel.For(0, 12, _ =>
            {
                var ledger = new FileMcpApprovalUseStore(directory);
                results.Add(ledger.TryConsume(Approval(), Request(), Now));
            });
            Assert.Equal(1, results.Count(accepted => accepted));
            Assert.Equal(11, results.Count(accepted => !accepted));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Expiry_argument_binding_and_separate_approver_fail_closed()
    {
        string directory = NewTestLedger();
        try
        {
            var store = new FileMcpApprovalUseStore(directory);
            Assert.False(store.TryConsume(Approval() with { ExpiresAt = Now }, Request(), Now));
            Assert.False(store.TryConsume(Approval() with { ArgumentsDigest = "sha256:wrong" }, Request(), Now));
            Assert.False(store.TryConsume(Approval() with { ApproverActorId = Request().ActorId }, Request(), Now));
            Assert.Empty(Directory.GetFiles(directory, "*.used"));
            Assert.True(store.TryConsume(Approval(), Request(), Now));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewTestLedger()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pmcro-mcp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
