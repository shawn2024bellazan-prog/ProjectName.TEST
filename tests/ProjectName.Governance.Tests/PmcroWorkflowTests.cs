using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using ProjectName.Runtime;

namespace ProjectName.Governance.Tests;

public sealed class PmcroWorkflowTests
{
    [Fact]
    public async Task Five_native_phases_emit_only_a_halt_simulation_and_checkpoints()
    {
        var checkpointManager = CheckpointManager.CreateInMemory();
        const string secret = "PRIVATE_MAKER_CONTEXT_SHOULD_NOT_LEAK";
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            PmcroDryRunWorkflow.Create(),
            new PmcroDryRunInput("TS-DRY-0001", secret),
            checkpointManager);

        var outputs = new List<PmcroDryRunOutcome>();
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent output && output.Data is PmcroDryRunOutcome result)
                outputs.Add(result);
        }

        PmcroDryRunOutcome outcome = Assert.Single(outputs);
        Assert.Equal("TS-DRY-0001", outcome.TrailId);
        Assert.Equal("HALT", outcome.Verdict);
        Assert.False(outcome.IndependentCheckerPass);
        Assert.False(outcome.ProductionExecutionAuthorized);
        Assert.Equal(64, outcome.CandidateDigest.Length);
        Assert.DoesNotContain(secret, System.Text.Json.JsonSerializer.Serialize(outcome));
        Assert.DoesNotContain(
            typeof(CheckerEvidenceProjection).GetProperties(),
            p => p.Name.Contains("Intent", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("Private", StringComparison.OrdinalIgnoreCase));
        Assert.True(run.Checkpoints.Count >= 4);
    }

    [Fact]
    public async Task Native_json_checkpoints_are_written_and_can_resume_same_role_graph()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "test-checkpoints", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manager = CheckpointManager.CreateJson(new FileSystemJsonCheckpointStore(new DirectoryInfo(directory)));
        await using StreamingRun first = await InProcessExecution.RunStreamingAsync(
            PmcroDryRunWorkflow.Create(),
            new PmcroDryRunInput("TS-DRY-0002", "No external tool call."),
            manager);

        await foreach (WorkflowEvent evt in first.WatchStreamAsync()) { }
        Assert.True(first.Checkpoints.Count >= 4);
        Assert.NotEmpty(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));

        CheckpointInfo checkpoint = first.Checkpoints[1];
        await using StreamingRun resumed = await InProcessExecution.ResumeStreamingAsync(
            PmcroDryRunWorkflow.Create(), checkpoint, manager);
        var resumedOutputs = new List<PmcroDryRunOutcome>();
        await foreach (WorkflowEvent evt in resumed.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent output && output.Data is PmcroDryRunOutcome result)
                resumedOutputs.Add(result);
        }
        Assert.Contains(resumedOutputs, o => o.Verdict == "HALT" && !o.ProductionExecutionAuthorized);
    }
}
