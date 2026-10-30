using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ProjectName.Contracts;

namespace ProjectName.ControlPlane;

// Declarative development candidate only. Neither a charter nor a Trail frame is a grant.
public sealed record CandidateOrchestratorCharter(
    string Agent, string Domain, ImmutableHashSet<string> Tools,
    int Depth, int MaxDepth, bool CanDelegate)
{
    private static readonly Regex NamePattern = new(
        @"\A[a-z][a-z0-9-]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex DomainPattern = new(
        @"\A[a-z][a-z0-9-]*(/[a-z][a-z0-9-]*)*\z", RegexOptions.CultureInvariant);

    public bool ProductionExecutionAuthorized => false;

    public static CandidateOrchestratorCharter DevelopmentRoot(
        string agent, string domain, IEnumerable<string> candidateTools, int maxDepth = 1)
    {
        if (!ValidName(agent) || !ValidDomain(domain) || maxDepth < 1 || maxDepth > 4)
            throw new ArgumentException("A valid role, domain and bounded depth are required.");
        var tools = CandidateToolSet(candidateTools);
        return new(agent, domain, tools, 0, maxDepth, true);
    }

    public CandidateOrchestratorCharter Specialize(
        string childAgent, string childDomain, IEnumerable<string> requestedTools,
        bool canDelegate = false)
    {
        if (!CanDelegate || Depth >= MaxDepth)
            throw new InvalidOperationException("Delegation depth or authority exhausted.");
        if (!ValidName(childAgent) || !ValidDomain(childDomain) ||
            !childDomain.StartsWith(Domain + "/", StringComparison.Ordinal))
            throw new InvalidOperationException("Child domain must be strictly narrower.");
        var childTools = CandidateToolSet(requestedTools);
        if (!childTools.IsSubsetOf(Tools))
            throw new InvalidOperationException("A child cannot gain additional tools.");
        return new(childAgent, childDomain, childTools, Depth + 1, MaxDepth, canDelegate);
    }

    internal static bool ValidName(string? name) =>
        name is not null && NamePattern.IsMatch(name);

    private static bool ValidDomain(string? domain) =>
        domain is not null && DomainPattern.IsMatch(domain);

    private static ImmutableHashSet<string> CandidateToolSet(IEnumerable<string>? tools)
    {
        if (tools is null)
            throw new ArgumentException("Candidate tool set is required.");
        var result = ImmutableHashSet.CreateRange(StringComparer.Ordinal, tools);
        if (result.Any(tool => !ValidName(tool)))
            throw new ArgumentException("Tool identifiers must be explicit candidate names.");
        return result;
    }
}

public sealed record CandidateCognitiveFrame(
    string Agent, string Domain, CyclePhase Phase, string Command,
    string EvidenceReference, bool Simulated, bool IndependentCheckerPass,
    bool ProductionExecutionAuthorized);

// Never invokes an MCP tool, model, worker, policy engine, or Checker principal.
public sealed class CandidateCascadingCycle(CandidateOrchestratorCharter charter)
{
    private static readonly CyclePhase[] OrderedPhases =
    [
        CyclePhase.Orchestrate, CyclePhase.Plan, CyclePhase.Make,
        CyclePhase.Check, CyclePhase.Reflect
    ];

    private static readonly Regex CommandPattern = new(
        @"\A@(?<agent>[a-z][a-z0-9-]*) /(?<plugin>[a-z][a-z0-9-]*):(?<skill>[a-z]+) ""(?<intent>[^""\r\n]+)""\z",
        RegexOptions.CultureInvariant);

    private readonly List<CandidateCognitiveFrame> _frames = [];

    public IReadOnlyList<CandidateCognitiveFrame> Frames => _frames.ToArray();
    public bool Complete => _frames.Count == OrderedPhases.Length;
    public bool IndependentCheckerPass => false;
    public bool ProductionExecutionAuthorized => false;

    public static string FormatCommand(string agent, CyclePhase phase, string intent)
    {
        if (!CandidateOrchestratorCharter.ValidName(agent) ||
            string.IsNullOrWhiteSpace(intent) || intent.Contains('"') ||
            intent.Contains('\r') || intent.Contains('\n'))
            throw new ArgumentException("Invalid structured command.");
        return $"@{agent} /pmcro:{phase.ToString().ToLowerInvariant()} \"{intent}\"";
    }

    public CandidateCognitiveFrame Record(string command, string evidenceReference)
    {
        if (Complete)
            throw new InvalidOperationException("Cycle is complete; further frames require a new cycle.");
        if (string.IsNullOrWhiteSpace(evidenceReference))
            throw new ArgumentException("An evidence reference is required.");

        ArgumentNullException.ThrowIfNull(command);
        var match = CommandPattern.Match(command);
        if (!match.Success ||
            !string.Equals(match.Groups["agent"].Value, charter.Agent, StringComparison.Ordinal) ||
            !string.Equals(match.Groups["plugin"].Value, "pmcro", StringComparison.Ordinal) ||
            !string.Equals(match.Groups["skill"].Value,
                OrderedPhases[_frames.Count].ToString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Wrong actor, plugin or PMCR-O phase order.");

        var frame = new CandidateCognitiveFrame(
            charter.Agent, charter.Domain, OrderedPhases[_frames.Count],
            command, evidenceReference, Simulated: true,
            IndependentCheckerPass: false, ProductionExecutionAuthorized: false);
        _frames.Add(frame);
        return frame;
    }
}
