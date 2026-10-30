using ProjectName.Contracts;
using ProjectName.Trails;
using ProjectName.Runtime;

namespace ProjectName.Governance.Tests;

public sealed class TrailFrameTests
{
    [Fact]
    public void Hash_bound_to_phase_and_evidence()
    {
        var frame = TrailFrame.Create("TS-0001", CyclePhase.Make,
            "EV-0001", "genesis", DateTimeOffset.UnixEpoch);
        Assert.True(frame.VerifyDigest());
        Assert.False((frame with { EvidenceReference = "EV-other" }).VerifyDigest());
    }

    [Fact]
    public void Native_workflow_needs_multiple_agents()
    {
        Assert.Throws<ArgumentException>(() =>
            NativeWorkflowFactory.CreateSequential([]));
    }
}
