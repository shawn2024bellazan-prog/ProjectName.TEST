using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace ProjectName.Runtime;

// MAF owns native graph/executor semantics. PMCR-O owns admission and Checker isolation.
// Sequential agent orchestration forwards conversation history; do not use it for PMCR-O Checker.
public static class NativeWorkflowFactory
{
    public static Workflow CreatePmcroSimulation() => PmcroDryRunWorkflow.Create();

    public static Workflow CreateSequential(IReadOnlyList<AIAgent> agents)
    {
        if (agents is null || agents.Count < 2)
            throw new ArgumentException("At least two native agents are required.", nameof(agents));
        return AgentWorkflowBuilder.BuildSequential(agents);
    }
}
