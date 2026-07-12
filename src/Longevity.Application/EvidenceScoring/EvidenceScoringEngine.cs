namespace Longevity.Application.EvidenceScoring;

public sealed class EvidenceScoringEngine
{
    private readonly EvidenceScoringPolicy policy;

    public EvidenceScoringEngine(EvidenceScoringPolicy? policy = null) =>
        this.policy = policy ?? EvidenceScoringPolicy.Default;

    public EvidenceScoreResult Evaluate(EvidenceAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (assessment.SampleSize > policy.MaximumSampleSize)
            throw new ArgumentOutOfRangeException(nameof(assessment), "Sample size exceeds the policy maximum.");
        if (assessment.ReplicationCount > policy.MaximumReplicationCount)
            throw new ArgumentOutOfRangeException(nameof(assessment), "Replication count exceeds the policy maximum.");

        var contributions = new[]
        {
            Contribution("study_design", policy.Weights.StudyDesign, StudyDesignFactor(assessment.StudyDesign)),
            Contribution("sample_size", policy.Weights.SampleSize, SampleSizeFactor(assessment.SampleSize)),
            Contribution("replication", policy.Weights.Replication, ReplicationFactor(assessment.ReplicationCount)),
            Contribution("directness", policy.Weights.Directness, DirectnessFactor(assessment.Directness)),
            Contribution("risk_of_bias", policy.Weights.RiskOfBias, RiskOfBiasFactor(assessment.RiskOfBias)),
            Contribution("consistency", policy.Weights.Consistency, ConsistencyFactor(assessment.Consistency)),
            Contribution("publication_status", policy.Weights.PublicationStatus, PublicationFactor(assessment.PublicationStatus))
        };

        var penalties = BuildPenalties(assessment);
        var unrounded = contributions.Sum(item => item.Points) - penalties.Sum(item => item.Points);
        var score = decimal.Round(Math.Max(0m, unrounded), 2, MidpointRounding.AwayFromZero);
        var verdict = ThresholdVerdict(score);
        var reasons = new List<string>
        {
            StudyReason(assessment.StudyDesign),
            SampleReason(assessment.SampleSize),
            ReplicationReason(assessment.ReplicationCount),
            $"directness.{Snake(assessment.Directness)}",
            $"bias.{Snake(assessment.RiskOfBias)}",
            $"consistency.{Snake(assessment.Consistency)}",
            $"publication.{Snake(assessment.PublicationStatus)}"
        };
        reasons.AddRange(penalties.Select(item => item.Code));

        if (assessment.IsRetracted)
        {
            verdict = EvidenceVerdict.Disqualified;
            reasons.Add("verdict.retraction_override");
        }
        else if (assessment.Consistency == EvidenceConsistency.StronglyContradictory)
        {
            verdict = EvidenceVerdict.Contradictory;
            reasons.Add("verdict.contradiction_override");
        }
        else if (assessment.SampleSize < policy.SparseSampleSizeExclusive && assessment.ReplicationCount == 0 &&
                 verdict is EvidenceVerdict.Strong or EvidenceVerdict.Moderate)
        {
            verdict = EvidenceVerdict.Limited;
            reasons.Add("verdict.sparse_evidence_cap");
        }

        var explanation = $"Evidence {assessment.EvidenceId} scored {score:0.00}/100 under {policy.PolicyId} and was classified {verdict}.";
        return new EvidenceScoreResult(
            assessment.EvidenceId,
            policy.PolicyId,
            score,
            verdict,
            assessment.Alignment,
            contributions,
            penalties,
            reasons,
            explanation);
    }

    public IReadOnlyList<EvidenceScoreResult> EvaluateBatch(IEnumerable<EvidenceAssessment> assessments)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        var materialized = assessments.ToArray();
        if (materialized.Any(item => item is null))
            throw new ArgumentException("An assessment batch cannot contain null entries.", nameof(assessments));
        var duplicate = materialized.GroupBy(item => item.EvidenceId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate evidence identity: {duplicate.Key}", nameof(assessments));

        return Array.AsReadOnly(materialized
            .OrderBy(item => item.EvidenceId, StringComparer.Ordinal)
            .Select(Evaluate)
            .ToArray());
    }

    private EvidenceVerdict ThresholdVerdict(decimal score) => score >= policy.Thresholds.Strong
        ? EvidenceVerdict.Strong
        : score >= policy.Thresholds.Moderate
            ? EvidenceVerdict.Moderate
            : score >= policy.Thresholds.Limited
                ? EvidenceVerdict.Limited
                : EvidenceVerdict.Insufficient;

    private AppliedPenalty[] BuildPenalties(EvidenceAssessment assessment)
    {
        var values = new List<AppliedPenalty>();
        if (assessment.HasSeriousMethodologicalLimitations)
            values.Add(new("penalty.serious_methodological_limitations", policy.Penalties.SeriousMethodologicalLimitations));
        if (assessment.PublicationStatus == PublicationStatus.Preprint)
            values.Add(new("penalty.preprint", policy.Penalties.Preprint));
        else if (assessment.PublicationStatus == PublicationStatus.Unpublished)
            values.Add(new("penalty.unpublished", policy.Penalties.Unpublished));
        if (assessment.ConflictOfInterest is { } conflict && conflict != ConflictOfInterestSeverity.None)
            values.Add(new($"penalty.conflict_{Snake(conflict)}", ConflictPenalty(conflict)));
        if (assessment.IsRetracted)
            values.Add(new("penalty.retraction", policy.Penalties.Retraction));
        return values.ToArray();
    }

    private decimal ConflictPenalty(ConflictOfInterestSeverity value) => value switch
    {
        ConflictOfInterestSeverity.None => 0m,
        ConflictOfInterestSeverity.Low => policy.Penalties.ConflictLow,
        ConflictOfInterestSeverity.Moderate => policy.Penalties.ConflictModerate,
        ConflictOfInterestSeverity.High => policy.Penalties.ConflictHigh,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static ScoreContribution Contribution(string dimension, decimal weight, decimal factor) =>
        new(dimension, decimal.Round(weight * factor, 4, MidpointRounding.AwayFromZero));

    private static decimal StudyDesignFactor(StudyDesign value) => value switch
    {
        StudyDesign.Mechanistic => 0.15m,
        StudyDesign.Animal => 0.25m,
        StudyDesign.CaseReport => 0.30m,
        StudyDesign.CrossSectional => 0.45m,
        StudyDesign.Observational => 0.60m,
        StudyDesign.RandomizedControlledTrial => 0.85m,
        StudyDesign.SystematicReview => 0.90m,
        StudyDesign.MetaAnalysis => 1.00m,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static decimal SampleSizeFactor(int value) => value switch
    {
        0 => 0m,
        < 10 => 0.10m,
        < 50 => 0.30m,
        < 200 => 0.55m,
        < 1_000 => 0.75m,
        _ => 1m
    };

    private static decimal ReplicationFactor(int value) => value switch
    {
        0 => 0m,
        1 => 0.35m,
        2 => 0.60m,
        < 5 => 0.80m,
        _ => 1m
    };

    private static decimal DirectnessFactor(EvidenceDirectness value) => value switch
    {
        EvidenceDirectness.Indirect => 0.25m,
        EvidenceDirectness.PartiallyDirect => 0.65m,
        EvidenceDirectness.Direct => 1m,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static decimal RiskOfBiasFactor(RiskOfBias value) => value switch
    {
        RiskOfBias.Critical => 0m,
        RiskOfBias.High => 0.25m,
        RiskOfBias.Moderate => 0.65m,
        RiskOfBias.Low => 1m,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static decimal ConsistencyFactor(EvidenceConsistency value) => value switch
    {
        EvidenceConsistency.StronglyContradictory => 0m,
        EvidenceConsistency.Mixed => 0.35m,
        EvidenceConsistency.Unknown => 0.50m,
        EvidenceConsistency.Consistent => 0.80m,
        EvidenceConsistency.HighlyConsistent => 1m,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static decimal PublicationFactor(PublicationStatus value) => value switch
    {
        PublicationStatus.Unpublished => 0.40m,
        PublicationStatus.Preprint => 0.75m,
        PublicationStatus.PeerReviewed => 1m,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string StudyReason(StudyDesign value) => $"study_design.{Snake(value)}";
    private static string SampleReason(int value) => value switch { 0 => "sample.none", < 10 => "sample.very_small", < 50 => "sample.small", < 200 => "sample.medium", < 1_000 => "sample.large", _ => "sample.very_large" };
    private static string ReplicationReason(int value) => value switch { 0 => "replication.none", 1 => "replication.single", 2 => "replication.two", < 5 => "replication.several", _ => "replication.extensive" };

    private static string Snake<T>(T value) where T : struct, Enum =>
        string.Concat(value.ToString().Select((character, index) => char.IsUpper(character) && index > 0 ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));
}
