using Grpc.Core;

namespace ProjectName.Grpc;

// This is a real gRPC endpoint, but it has no activation or grant source.
public sealed class DenyAllPhaseWorker : PhaseWorker.PhaseWorkerBase
{
    public override Task<PhaseReply> Execute(PhaseRequest request, ServerCallContext context)
        => throw new RpcException(new Status(StatusCode.PermissionDenied, "NO_WORKER_ADMISSION"));
}
