namespace ProjectName.Contracts;

public enum CyclePhase { Orchestrate, Plan, Make, Check, Reflect }
public enum CycleVerdict { Accept, Reject, Revise, Loop, Halt }

public sealed record CycleSubmission(
    string TrailId,
    string CapabilityId,
    string ActorId,
    string Payload);

public sealed record AdmissionResult(bool Allowed, string Reason, string? BoundDigest = null)
{
    public static AdmissionResult Deny(string reason) => new(false, reason);
}

public sealed record PhaseResult(
    string TrailId, CyclePhase Phase, CycleVerdict Verdict,
    string EvidenceReference, string? NextSeed = null);
