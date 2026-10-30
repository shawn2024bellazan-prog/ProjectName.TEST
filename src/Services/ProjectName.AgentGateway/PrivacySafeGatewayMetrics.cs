using System.Diagnostics.Metrics;

namespace ProjectName.AgentGateway;

/// <summary>
/// Low-cardinality counters only. Never add prompts, messages, tokens,
/// session ids, tenant ids, actor ids, URLs or credential information as tags.
/// No telemetry exporter is configured in the deny-all gateway.
/// </summary>
public sealed class PrivacySafeGatewayMetrics : IDisposable
{
    public const string MeterName = "ProjectName.AgentGateway.Security";
    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _denied;
    private readonly Counter<long> _throttled;

    public PrivacySafeGatewayMetrics()
    {
        _denied = _meter.CreateCounter<long>("agent_endpoint_denied_total");
        _throttled = _meter.CreateCounter<long>("agent_gateway_throttled_total");
    }

    public void RecordDenied() => _denied.Add(1);
    public void RecordThrottled() => _throttled.Add(1);
    public void Dispose() => _meter.Dispose();
}