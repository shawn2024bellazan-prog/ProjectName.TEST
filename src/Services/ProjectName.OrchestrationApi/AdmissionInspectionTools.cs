using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ProjectName.OrchestrationApi;

// Read-only MCP inspection surface. It never registers, admits, or runs agents.
[McpServerToolType]
public static class AdmissionInspectionTools
{
    [McpServerTool]
    [Description("Inspect the PMCR-O development admission posture. Read-only; no grants or activation.")]
    public static object GetAdmissionStatus() => new
    {
        project = "ProjectName",
        surface = "pmcro:inspection",
        mode = "inspect",
        registration = "not activated",
        grantVerified = false,
        independentCheckerPass = false,
        workerAdmission = "deny-all",
        executionAuthorized = false
    };
}
