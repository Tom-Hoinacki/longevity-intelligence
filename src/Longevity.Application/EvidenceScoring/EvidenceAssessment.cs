namespace Longevity.Application.EvidenceScoring;

public sealed record EvidenceAssessment
{
    public EvidenceAssessment(
        string evidenceId,
        StudyDesign studyDesign,
        int sampleSize,
        int replicationCount,
        EvidenceDirectness directness,
        RiskOfBias riskOfBias,
        EvidenceConsistency consistency,
        PublicationStatus publicationStatus,
        bool isRetracted,
        bool hasSeriousMethodologicalLimitations,
        ConflictOfInterestSeverity? conflictOfInterest = null,
        ClaimAlignment alignment = ClaimAlignment.Supports)
    {
        EvidenceId = string.IsNullOrWhiteSpace(evidenceId)
            ? throw new ArgumentException("Evidence identity must be non-empty.", nameof(evidenceId))
            : evidenceId.Trim();
        RequireDefined(studyDesign, nameof(studyDesign));
        RequireDefined(directness, nameof(directness));
        RequireDefined(riskOfBias, nameof(riskOfBias));
        RequireDefined(consistency, nameof(consistency));
        RequireDefined(publicationStatus, nameof(publicationStatus));
        RequireDefined(alignment, nameof(alignment));
        if (conflictOfInterest.HasValue) RequireDefined(conflictOfInterest.Value, nameof(conflictOfInterest));
        if (sampleSize < 0) throw new ArgumentOutOfRangeException(nameof(sampleSize));
        if (replicationCount < 0) throw new ArgumentOutOfRangeException(nameof(replicationCount));

        StudyDesign = studyDesign;
        SampleSize = sampleSize;
        ReplicationCount = replicationCount;
        Directness = directness;
        RiskOfBias = riskOfBias;
        Consistency = consistency;
        PublicationStatus = publicationStatus;
        IsRetracted = isRetracted;
        HasSeriousMethodologicalLimitations = hasSeriousMethodologicalLimitations;
        ConflictOfInterest = conflictOfInterest;
        Alignment = alignment;
    }

    public string EvidenceId { get; }
    public StudyDesign StudyDesign { get; }
    public int SampleSize { get; }
    public int ReplicationCount { get; }
    public EvidenceDirectness Directness { get; }
    public RiskOfBias RiskOfBias { get; }
    public EvidenceConsistency Consistency { get; }
    public PublicationStatus PublicationStatus { get; }
    public bool IsRetracted { get; }
    public bool HasSeriousMethodologicalLimitations { get; }
    public ConflictOfInterestSeverity? ConflictOfInterest { get; }
    public ClaimAlignment Alignment { get; }

    internal static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName, value, "Undefined enum values are not accepted.");
    }
}
