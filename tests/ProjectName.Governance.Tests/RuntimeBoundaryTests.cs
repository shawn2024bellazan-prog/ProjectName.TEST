using ProjectName.Governance;
using ProjectName.ControlPlane;
using ProjectName.Contracts;

namespace ProjectName.Governance.Tests;

public sealed class RuntimeBoundaryTests
{
    [Fact]
    public void No_receipt_or_grant_never_admits_an_agent_request()
    {
        var gate = new AdmissionGate(new DenyAllAuthorityRepository(), new DenyAllReceiptVerifier());
        var request = new CycleSubmission("TS-0002", "ai:chat", "agent-1", "Hello");
        var decision = gate.Evaluate(request, null, DateTimeOffset.UtcNow);
        Assert.False(decision.Allowed);
    }
}
