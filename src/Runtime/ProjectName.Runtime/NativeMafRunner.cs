using Microsoft.Agents.AI;
using ProjectName.Contracts;
using ProjectName.ControlPlane;
using ProjectName.Governance;

namespace ProjectName.Runtime;

// The only public agent invocation entrypoint in ProjectName.Runtime.
// Production authority and Checker validation are external verified inputs;
// no public overload accepts a caller-supplied "allowed" flag.
public sealed class GovernedMafRunner(
    AdmissionGate gate,
    AIAgent agent)
{
    public async Task<string> RunAsync(
        CycleSubmission request,
        CheckerReceipt? receipt,
        CancellationToken cancellationToken = default)
    {
        AdmissionResult decision = gate.Evaluate(request, receipt, DateTimeOffset.UtcNow);
        if (!decision.Allowed)
            throw new UnauthorizedAccessException(decision.Reason);
        if (string.IsNullOrWhiteSpace(request.Payload))
            throw new ArgumentException("Prompt required.", nameof(request));

        AgentResponse response = await agent.RunAsync(
            request.Payload, cancellationToken: cancellationToken);
        return response.Text;
    }
}
