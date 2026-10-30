using System.Security.Cryptography;
using System.Text;
using Microsoft.Agents.AI.Workflows;

namespace ProjectName.Runtime;

// Candidate-only PMCR-O graph. No LLM, marketplace tool, worker, or grant is used.
// All messages passed to the Checker are typed, deliberately reduced evidence.
public static class PmcroDryRunWorkflow
{
    public static Workflow Create()
    {
        var orchestrator = new OrchestratePhaseExecutor();
        var planner = new PlanPhaseExecutor();
        var maker = new SimulateMakePhaseExecutor();
        var checker = new ProjectCheckerEvidenceExecutor();
        var reflector = new ReflectPhaseExecutor();

        return new WorkflowBuilder(orchestrator)
            .AddEdge(orchestrator, planner)
            .AddEdge(planner, maker)
            .AddEdge(maker, checker)
            .AddEdge(checker, reflector)
            .WithOutputFrom(reflector)
            .Build();
    }

    internal static string Digest(string data) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
}

public sealed record PmcroDryRunInput(string TrailId, string RawIntent);
public sealed record OrchestratedIntent(string TrailId, string IntentDigest);
public sealed record PlannedCandidate(string TrailId, string IntentDigest);
public sealed record CheckerEvidenceProjection(string TrailId, string CandidateDigest);
public sealed record UnverifiedCheckerProjection(string TrailId, string CandidateDigest);
public sealed record PmcroDryRunOutcome(
    string TrailId,
    string CandidateDigest,
    string Verdict,
    bool IndependentCheckerPass,
    bool ProductionExecutionAuthorized,
    string NextSeed);

internal sealed partial class OrchestratePhaseExecutor() : Executor("pmcro.orchestrate")
{
    [MessageHandler]
    private ValueTask<OrchestratedIntent> HandleAsync(PmcroDryRunInput input, IWorkflowContext context)
    {
        if (string.IsNullOrWhiteSpace(input.TrailId) || string.IsNullOrWhiteSpace(input.RawIntent))
            throw new ArgumentException("A bounded Trail and intent are required.");
        return ValueTask.FromResult(new OrchestratedIntent(input.TrailId, PmcroDryRunWorkflow.Digest(input.RawIntent)));
    }
}

internal sealed partial class PlanPhaseExecutor() : Executor("pmcro.plan")
{
    [MessageHandler]
    private ValueTask<PlannedCandidate> HandleAsync(OrchestratedIntent input, IWorkflowContext context) =>
        ValueTask.FromResult(new PlannedCandidate(input.TrailId, input.IntentDigest));
}

internal sealed partial class SimulateMakePhaseExecutor() : Executor("pmcro.make.simulation")
{
    [MessageHandler]
    private ValueTask<CheckerEvidenceProjection> HandleAsync(PlannedCandidate input, IWorkflowContext context)
    {
        // Private Maker deliberation is not passed as an edge message.
        string candidateDigest = PmcroDryRunWorkflow.Digest("SIMULATION_ONLY:" + input.IntentDigest);
        return ValueTask.FromResult(new CheckerEvidenceProjection(input.TrailId, candidateDigest));
    }
}

internal sealed partial class ProjectCheckerEvidenceExecutor() : Executor("pmcro.check.evidence-projection")
{
    [MessageHandler]
    private ValueTask<UnverifiedCheckerProjection> HandleAsync(CheckerEvidenceProjection evidence, IWorkflowContext context)
    {
        // This is a projection, not an independently authenticated Checker verdict.
        return ValueTask.FromResult(new UnverifiedCheckerProjection(evidence.TrailId, evidence.CandidateDigest));
    }
}

internal sealed partial class ReflectPhaseExecutor() : Executor("pmcro.reflect")
{
    [MessageHandler]
    private ValueTask<PmcroDryRunOutcome> HandleAsync(UnverifiedCheckerProjection projection, IWorkflowContext context) =>
        ValueTask.FromResult(new PmcroDryRunOutcome(
            projection.TrailId,
            projection.CandidateDigest,
            "HALT",
            IndependentCheckerPass: false,
            ProductionExecutionAuthorized: false,
            NextSeed: "Obtain a host-authenticated independent Checker identity before admission."));
}
