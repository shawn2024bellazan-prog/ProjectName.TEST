using Microsoft.Extensions.AI;
using ProjectName.AgentGateway;

namespace ProjectName.Governance.Tests;

public sealed class DevelopmentAgentsTests
{
    [Fact]
    public void CatalogHasFiveCoreAndTenUnregisteredExecutiveRoles()
    {
        var roles = DevelopmentAgentCatalog.Roles;
        Assert.Equal(15, roles.Count);
        Assert.Equal(15, roles.Select(r => r.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(5, roles.Count(role => role.Name.StartsWith("pmcro-", StringComparison.Ordinal)));
        Assert.Equal(10, roles.Count(role => role.Name.StartsWith("csuite-", StringComparison.Ordinal)));
        Assert.All(roles, role => Assert.Contains("SIMULATION_ONLY", role.Instructions));
        Assert.Contains(roles, role => role.Name == "pmcro-checker");
    }

    [Theory]
    [InlineData("pmcro-orchestrator")]
    [InlineData("pmcro-planner")]
    [InlineData("pmcro-maker")]
    [InlineData("pmcro-checker")]
    [InlineData("pmcro-reflector")]
    [InlineData("csuite-ceo")]
    [InlineData("csuite-cto")]
    [InlineData("csuite-coo")]
    [InlineData("csuite-cfo")]
    [InlineData("csuite-cpo")]
    [InlineData("csuite-cmo")]
    [InlineData("csuite-clo")]
    [InlineData("csuite-chro")]
    [InlineData("csuite-cso")]
    [InlineData("csuite-cio")]
    public async Task RoleClientOnlyReturnsUnverifiedSimulation(string role)
    {
        using var client = new SimulationOnlyChatClient(role);
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Never execute anything")]);
        string text = response.Messages[0].Text;
        Assert.Contains("SIMULATION_ONLY", text);
        Assert.Contains("No reasoning/model inference", text);
        Assert.NotNull(client.GetService(typeof(ChatClientMetadata)));
    }

    [Fact]
    public async Task CheckerStreamingCannotClaimIndependentPass()
    {
        using var client = new SimulationOnlyChatClient("pmcro-checker");
        var parts = new List<string>();
        await foreach (var delta in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Please approve")]))
        {
            parts.Add(delta.Text);
        }
        Assert.Single(parts);
        Assert.Contains("SIMULATION_ONLY", parts[0]);
        Assert.Contains("cannot", DevelopmentAgentCatalog.Get("pmcro-checker").Responsibility);
    }
}
