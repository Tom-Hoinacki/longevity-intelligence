using System.Collections.ObjectModel;

namespace Longevity.Application.EvidenceScoring;

public enum StudyDesign
{
    Mechanistic = 0,
    Animal = 1,
    CaseReport = 2,
    CrossSectional = 3,
    Observational = 4,
    RandomizedControlledTrial = 5,
    SystematicReview = 6,
    MetaAnalysis = 7
}

public enum EvidenceDirectness { Indirect = 0, PartiallyDirect = 1, Direct = 2 }
public enum RiskOfBias { Critical = 0, High = 1, Moderate = 2, Low = 3 }
public enum EvidenceConsistency { StronglyContradictory = 0, Mixed = 1, Unknown = 2, Consistent = 3, HighlyConsistent = 4 }
public enum PublicationStatus { Unpublished = 0, Preprint = 1, PeerReviewed = 2 }
public enum ConflictOfInterestSeverity { None = 0, Low = 1, Moderate = 2, High = 3 }
public enum ClaimAlignment { Neutral = 0, Supports = 1, Contradicts = 2 }
public enum EvidenceVerdict { Insufficient = 0, Limited = 1, Moderate = 2, Strong = 3, Contradictory = 4, Disqualified = 5 }

public sealed record ScoreContribution(string Dimension, decimal Points);
public sealed record AppliedPenalty(string Code, decimal Points);

public sealed record EvidenceScoreResult
{
    internal EvidenceScoreResult(
        string evidenceId,
        string policyId,
        decimal score,
        EvidenceVerdict verdict,
        ClaimAlignment alignment,
        IEnumerable<ScoreContribution> contributions,
        IEnumerable<AppliedPenalty> penalties,
        IEnumerable<string> reasonCodes,
        string explanation)
    {
        EvidenceId = evidenceId;
        PolicyId = policyId;
        Score = score;
        Verdict = verdict;
        Alignment = alignment;
        Contributions = ReadOnly(contributions);
        Penalties = ReadOnly(penalties);
        ReasonCodes = ReadOnly(reasonCodes);
        Explanation = explanation;
    }

    public string EvidenceId { get; }
    public string PolicyId { get; }
    public decimal Score { get; }
    public EvidenceVerdict Verdict { get; }
    public ClaimAlignment Alignment { get; }
    public IReadOnlyList<ScoreContribution> Contributions { get; }
    public IReadOnlyList<AppliedPenalty> Penalties { get; }
    public IReadOnlyList<string> ReasonCodes { get; }
    public string Explanation { get; }

    private static ReadOnlyCollection<T> ReadOnly<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}

public sealed record ClaimEvidenceResult
{
    internal ClaimEvidenceResult(
        decimal score,
        EvidenceVerdict verdict,
        int supportingCount,
        int contradictingCount,
        int disqualifiedCount,
        IEnumerable<string> reasonCodes,
        string explanation,
        IEnumerable<string> evidenceIds)
    {
        Score = score;
        Verdict = verdict;
        SupportingCount = supportingCount;
        ContradictingCount = contradictingCount;
        DisqualifiedCount = disqualifiedCount;
        ReasonCodes = Array.AsReadOnly(reasonCodes.ToArray());
        Explanation = explanation;
        EvidenceIds = Array.AsReadOnly(evidenceIds.ToArray());
    }

    public decimal Score { get; }
    public EvidenceVerdict Verdict { get; }
    public int SupportingCount { get; }
    public int ContradictingCount { get; }
    public int DisqualifiedCount { get; }
    public IReadOnlyList<string> ReasonCodes { get; }
    public string Explanation { get; }
    public IReadOnlyList<string> EvidenceIds { get; }
}
