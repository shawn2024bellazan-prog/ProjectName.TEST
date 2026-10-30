using System.Net.Http.Json;
using ProjectName.Contracts;

namespace ProjectName.Sdk.DotNet;

public sealed class ProjectNameClient(HttpClient httpClient)
{
    public async Task<AdmissionResult> SubmitCycleAsync(
        CycleSubmission cycle, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "v1/cycles", cycle, cancellationToken);
        AdmissionResult? decision = await response.Content.ReadFromJsonAsync<AdmissionResult>(
            cancellationToken: cancellationToken);
        return decision ?? AdmissionResult.Deny("INVALID_SERVER_RESPONSE");
    }
}
