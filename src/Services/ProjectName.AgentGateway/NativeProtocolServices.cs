using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;

namespace ProjectName.AgentGateway;

/// <summary>
/// Compiled native MAF adapter composition. The default host intentionally
/// does not call this method: there is no approved AIAgent to register.
/// Endpoint mapping needs a separate, request-authenticated promotion cycle.
/// </summary>
internal static class NativeProtocolServices
{
    internal static void RegisterForAdmittedAgent(
        WebApplicationBuilder builder, IHostedAgentBuilder admittedAgent)
    {
        ArgumentNullException.ThrowIfNull(admittedAgent);
        builder.Services.AddOpenAIResponses();
        builder.Services.AddOpenAIConversations();
        builder.Services.AddAGUIServer();
        admittedAgent.AddA2AServer();
    }
}