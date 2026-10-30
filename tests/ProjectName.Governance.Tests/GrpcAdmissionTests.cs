using Grpc.Core;
using ProjectName.Grpc;

namespace ProjectName.Governance.Tests;

public sealed class GrpcAdmissionTests
{
    [Fact]
    public async Task Unadmitted_worker_rpc_is_permission_denied()
    {
        var worker = new DenyAllPhaseWorker();
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await worker.Execute(new PhaseRequest { TrailId = "TS-0003", Phase = "MAKE" }, null!));
        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
    }
}
