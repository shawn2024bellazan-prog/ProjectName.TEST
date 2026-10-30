using System.Text.Json;
using ProjectName.Contracts;
using ProjectName.Evidence;

namespace ProjectName.Trails;

// An append-only development Trail frame; production needs signed immutable storage.
public sealed record TrailFrame(
    string TrailId, CyclePhase Phase, string EvidenceReference,
    string PreviousDigest, DateTimeOffset RecordedAt, string Digest)
{
    public static TrailFrame Create(
        string trailId, CyclePhase phase, string evidenceReference,
        string previousDigest, DateTimeOffset recordedAt)
    {
        if (string.IsNullOrWhiteSpace(trailId) ||
            string.IsNullOrWhiteSpace(evidenceReference))
            throw new ArgumentException("Trail and evidence are required.");
        string canonical = Canonical(trailId, phase, evidenceReference, previousDigest, recordedAt);
        return new(trailId, phase, evidenceReference, previousDigest,
            recordedAt, EvidenceHash.Sha256(canonical));
    }

    public bool VerifyDigest() =>
        string.Equals(Digest,
            EvidenceHash.Sha256(Canonical(TrailId, Phase, EvidenceReference, PreviousDigest, RecordedAt)),
            StringComparison.Ordinal);

    private static string Canonical(string trail, CyclePhase phase,
        string evidence, string previous, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new {
            trail, phase = phase.ToString(), evidence, previous,
            recordedAt = timestamp.ToUniversalTime().ToString("O")
        });
}
