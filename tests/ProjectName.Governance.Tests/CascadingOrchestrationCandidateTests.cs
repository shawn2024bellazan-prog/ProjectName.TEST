using ProjectName.Contracts;
using ProjectName.ControlPlane;

namespace ProjectName.Governance.Tests;

public sealed class CascadingOrchestrationCandidateTests
{
    private static readonly CyclePhase[] Phases =
    [
        CyclePhase.Orchestrate, CyclePhase.Plan, CyclePhase.Make,
        CyclePhase.Check, CyclePhase.Reflect
    ];

    [Theory]
    [InlineData("orchestrator")]
    [InlineData("planner")]
    [InlineData("maker")]
    [InlineData("checker")]
    [InlineData("reflector")]
    public void Every_role_can_self_apply_all_five_phases_as_a_denied_candidate(string agent)
    {
        var root = CandidateOrchestratorCharter.DevelopmentRoot(
            "orchestrator", "pmcro", ["inspect", "draft"]);
        var role = agent == "orchestrator"
            ? root
            : root.Specialize(agent, $"pmcro/roles/{agent}", ["inspect"]);
        var cycle = new CandidateCascadingCycle(role);

        foreach (var phase in Phases)
        {
            var command = CandidateCascadingCycle.FormatCommand(
                role.Agent, phase, $"I AM {agent}, performing {phase} within {role.Domain}.");
            var frame = cycle.Record(command, $"test-only:{agent}:{phase}");
            Assert.Equal(phase, frame.Phase);
            Assert.Equal(role.Agent, frame.Agent);
            Assert.Equal(role.Domain, frame.Domain);
            Assert.StartsWith($"@{agent} /pmcro:", frame.Command);
            Assert.True(frame.Simulated);
            Assert.False(frame.IndependentCheckerPass);
            Assert.False(frame.ProductionExecutionAuthorized);
        }

        Assert.True(cycle.Complete);
        Assert.Equal(Phases, cycle.Frames.Select(frame => frame.Phase));
        Assert.False(cycle.IndependentCheckerPass);
        Assert.False(cycle.ProductionExecutionAuthorized);
    }

    [Fact]
    public void A_child_inherits_less_scope_and_cannot_expand_tools()
    {
        var root = CandidateOrchestratorCharter.DevelopmentRoot(
            "orchestrator", "company/marketing", ["inspect", "draft"], maxDepth: 2);
        var social = root.Specialize("social-media", "company/marketing/social", ["inspect"], true);
        var facebook = social.Specialize("facebook", "company/marketing/social/facebook", []);

        Assert.True(facebook.Tools.IsSubsetOf(social.Tools));
        Assert.Equal(2, facebook.Depth);
        Assert.False(facebook.CanDelegate);
        Assert.False(facebook.ProductionExecutionAuthorized);
        Assert.Throws<InvalidOperationException>(() =>
            facebook.Specialize("post", "company/marketing/social/facebook/post", []));
        Assert.Throws<InvalidOperationException>(() =>
            social.Specialize("facebook", "company/marketing/social/facebook", ["draft"]));
    }

    [Fact]
    public void A_sibling_domain_or_path_traversal_cannot_be_delegated()
    {
        var root = CandidateOrchestratorCharter.DevelopmentRoot(
            "orchestrator", "company/social", ["inspect"]);
        Assert.Throws<InvalidOperationException>(() =>
            root.Specialize("instagram", "company/marketing", []));
        Assert.Throws<InvalidOperationException>(() =>
            root.Specialize("escape", "company/social/../secrets", []));
        Assert.Throws<InvalidOperationException>(() =>
            root.Specialize("same", "company/social", []));
    }

    [Fact]
    public void Exhausted_depth_and_nonpositive_maximum_are_denied()
    {
        Assert.Throws<ArgumentException>(() =>
            CandidateOrchestratorCharter.DevelopmentRoot("orchestrator", "pmcro", [], 0));
        Assert.Throws<ArgumentException>(() =>
            CandidateOrchestratorCharter.DevelopmentRoot("orchestrator", "pmcro", [], 5));
        var root = CandidateOrchestratorCharter.DevelopmentRoot("orchestrator", "pmcro", []);
        var child = root.Specialize("planner", "pmcro/planning", []);
        Assert.Throws<InvalidOperationException>(() =>
            child.Specialize("maker", "pmcro/planning/making", []));
    }

    [Fact]
    public void Phase_order_cannot_skip_or_repeat()
    {
        var cycle = new CandidateCascadingCycle(
            CandidateOrchestratorCharter.DevelopmentRoot("orchestrator", "pmcro", []));
        Assert.Throws<InvalidOperationException>(() =>
            cycle.Record("@orchestrator /pmcro:plan \"plan\"", "test-only:1"));
        cycle.Record("@orchestrator /pmcro:orchestrate \"orchestrate\"", "test-only:2");
        Assert.Throws<InvalidOperationException>(() =>
            cycle.Record("@orchestrator /pmcro:make \"make\"", "test-only:3"));
        Assert.Single(cycle.Frames);
    }

    [Fact]
    public void Actor_plugin_and_skill_spoofing_are_denied()
    {
        var cycle = new CandidateCascadingCycle(
            CandidateOrchestratorCharter.DevelopmentRoot("maker", "pmcro/making", []));
        Assert.Throws<InvalidOperationException>(() =>
            cycle.Record("@checker /pmcro:orchestrate \"imposter\"", "test-only:1"));
        Assert.Throws<InvalidOperationException>(() =>
            cycle.Record("@maker /other:orchestrate \"wrong plugin\"", "test-only:1"));
        Assert.Throws<InvalidOperationException>(() =>
            cycle.Record("@maker /pmcro:approve \"wrong skill\"", "test-only:1"));
        Assert.Empty(cycle.Frames);
    }

    [Fact]
    public void Structured_messages_reject_ambiguous_or_missing_evidence()
    {
        Assert.Throws<ArgumentException>(() =>
            CandidateCascadingCycle.FormatCommand("maker", CyclePhase.Make, "quote \" spoof"));
        Assert.Throws<ArgumentException>(() =>
            CandidateCascadingCycle.FormatCommand("maker", CyclePhase.Make, "newline\nspoof"));
        var cycle = new CandidateCascadingCycle(
            CandidateOrchestratorCharter.DevelopmentRoot("maker", "pmcro/making", []));
        Assert.Throws<ArgumentException>(() =>
            cycle.Record("@maker /pmcro:orchestrate \"start\"", ""));
        Assert.Empty(cycle.Frames);
    }

    [Fact]
    public void Completed_cycle_never_automatically_executes_the_next_seed()
    {
        var cycle = new CandidateCascadingCycle(
            CandidateOrchestratorCharter.DevelopmentRoot("reflector", "pmcro/reflection", []));
        foreach (var phase in Phases)
            cycle.Record(
                CandidateCascadingCycle.FormatCommand("reflector", phase, "candidate only"),
                $"test-only:{phase}");
        Assert.Throws<InvalidOperationException>(() =>
            cycle.Record("@reflector /pmcro:orchestrate \"next seed\"", "test-only:next"));
        Assert.Equal(5, cycle.Frames.Count);
        Assert.False(cycle.ProductionExecutionAuthorized);
    }
}
