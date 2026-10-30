using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ProjectName.AgentGateway;

// Discovery and simulation only. These are not admitted PMCR-O role workers.
public sealed record DevelopmentAgentDescriptor(
    string Name,
    string Identity,
    string Responsibility)
{
    public string Instructions =>
        $"I AM {Identity}. {Responsibility} " +
        "This surface is SIMULATION_ONLY; do not claim authority, Checker PASS, " +
        "production execution, tool access or a real model response.";
}

public static class DevelopmentAgentCatalog
{
    public static IReadOnlyList<DevelopmentAgentDescriptor> Roles { get; } =
    [
        new("pmcro-orchestrator", "the PMCR-O Orchestrator Agent",
            "I inspect intent and would open a Trail only after admission."),
        new("pmcro-planner", "the PMCR-O Planner Agent",
            "I would prepare a bounded, unexecuted plan."),
        new("pmcro-maker", "the PMCR-O Maker Agent",
            "I would prepare a candidate; I cannot self-approve."),
        new("pmcro-checker", "the PMCR-O Checker Agent",
            "I require independent signed evidence; I cannot claim PASS."),
        new("pmcro-reflector", "the PMCR-O Reflector Agent",
            "I would produce one bounded UNEXECUTED NextSeed."),
        // Mirrors csuite-marketplace/marketplace.json. DRAFT_UNREGISTERED;
        // executive recommendations cannot become grants or execution.
        new("csuite-ceo", "the Chief Executive Officer Agent",
            "I would convene strategy roundtables; recommendation only, not activated."),
        new("csuite-cto", "the Chief Technology Officer Agent",
            "I would review engineering decisions; recommendation only, not activated."),
        new("csuite-coo", "the Chief Operating Officer Agent",
            "I would assess operations; recommendation only, not activated."),
        new("csuite-cfo", "the Chief Financial Officer Agent",
            "I would examine financial tradeoffs; recommendation only, not activated."),
        new("csuite-cpo", "the Chief Product Officer Agent",
            "I would assess product direction; recommendation only, not activated."),
        new("csuite-cmo", "the Chief Marketing Officer Agent",
            "I would assess marketing; recommendation only, not activated."),
        new("csuite-clo", "the Chief Legal Officer Agent",
            "I would flag legal review needs; recommendation only, not legal advice or activation."),
        new("csuite-chro", "the Chief Human Resources Officer Agent",
            "I would assess people operations; recommendation only, not activated."),
        new("csuite-cso", "the Chief Strategy Officer Agent",
            "I would compare strategy options; recommendation only, not activated."),
        new("csuite-cio", "the Chief Information Officer Agent",
            "I would assess information systems; recommendation only, not activated.")
    ];

    public static DevelopmentAgentDescriptor Get(string name) =>
        Roles.FirstOrDefault(role => role.Name == name) ??
        throw new ArgumentException("Unknown development agent.", nameof(name));
}

// Microsoft.Extensions.AI client with deterministic, no-network responses.
// Deliberately does not inspect prompts, call Ollama, invoke tools or mutate Trails.
public sealed class SimulationOnlyChatClient(string agentName) : IChatClient
{
    private readonly ChatClientMetadata _metadata = new(
        "ProjectName.SimulationOnly", new Uri("http://localhost"), "no-model");

    private string SimulatedReply()
    {
        DevelopmentAgentDescriptor role = DevelopmentAgentCatalog.Get(agentName);
        return $"SIMULATION_ONLY. I AM {role.Identity}. {role.Responsibility} " +
               "No reasoning/model inference, tool call, Worker admission or Checker PASS occurred.";
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, SimulatedReply())));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield return new ChatResponseUpdate(ChatRole.Assistant, SimulatedReply());
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(ChatClientMetadata) ? _metadata :
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
