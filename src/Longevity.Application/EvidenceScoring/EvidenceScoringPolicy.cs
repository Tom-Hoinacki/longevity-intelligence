namespace Longevity.Application.EvidenceScoring;

public sealed record DimensionWeights(
    decimal StudyDesign,
    decimal SampleSize,
    decimal Replication,
    decimal Directness,
    decimal RiskOfBias,
    decimal Consistency,
    decimal PublicationStatus);

public sealed record VerdictThresholds(decimal Limited, decimal Moderate, decimal Strong);

public sealed record ScoringPenalties(
    decimal SeriousMethodologicalLimitations,
    decimal Preprint,
    decimal Unpublished,
    decimal ConflictLow,
    decimal ConflictModerate,
    decimal ConflictHigh,
    decimal Retraction);

public sealed record EvidenceScoringPolicy
{
    public const decimal RequiredWeightTotal = 100m;

    public EvidenceScoringPolicy(
        string policyId,
        DimensionWeights weights,
        VerdictThresholds thresholds,
        ScoringPenalties penalties,
        int maximumSampleSize = 10_000_000,
        int maximumReplicationCount = 10_000,
        int sparseSampleSizeExclusive = 10,
        int minimumDirectionalItemsForModerate = 2,
        int minimumDirectionalItemsForStrong = 3)
    {
        PolicyId = string.IsNullOrWhiteSpace(policyId)
            ? throw new ArgumentException("Policy identity must be non-empty.", nameof(policyId))
            : policyId.Trim();
        Weights = weights ?? throw new ArgumentNullException(nameof(weights));
        Thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
        Penalties = penalties ?? throw new ArgumentNullException(nameof(penalties));

        var allWeights = new[] { weights.StudyDesign, weights.SampleSize, weights.Replication, weights.Directness, weights.RiskOfBias, weights.Consistency, weights.PublicationStatus };
        if (allWeights.Any(value => value < 0m || value > RequiredWeightTotal))
            throw new ArgumentOutOfRangeException(nameof(weights), "Every weight must be between 0 and 100.");
        if (allWeights.Sum() != RequiredWeightTotal)
            throw new ArgumentException("Dimension weights must total exactly 100.", nameof(weights));

        if (thresholds.Limited < 0m || thresholds.Strong > 100m ||
            thresholds.Limited >= thresholds.Moderate || thresholds.Moderate >= thresholds.Strong)
            throw new ArgumentException("Verdict thresholds must be strictly ordered within 0 through 100.", nameof(thresholds));

        var allPenalties = new[] { penalties.SeriousMethodologicalLimitations, penalties.Preprint, penalties.Unpublished, penalties.ConflictLow, penalties.ConflictModerate, penalties.ConflictHigh, penalties.Retraction };
        if (allPenalties.Any(value => value < 0m || value > 100m))
            throw new ArgumentOutOfRangeException(nameof(penalties), "Every penalty must be between 0 and 100.");
        if (penalties.ConflictLow > penalties.ConflictModerate || penalties.ConflictModerate > penalties.ConflictHigh)
            throw new ArgumentException("Conflict penalties must be nondecreasing by severity.", nameof(penalties));
        if (penalties.Preprint > penalties.Unpublished)
            throw new ArgumentException("Unpublished evidence cannot have a smaller penalty than preprint evidence.", nameof(penalties));
        if (penalties.Retraction < thresholds.Strong)
            throw new ArgumentException("The retraction penalty must be at least the strong threshold.", nameof(penalties));

        if (maximumSampleSize < 1) throw new ArgumentOutOfRangeException(nameof(maximumSampleSize));
        if (maximumReplicationCount < 1) throw new ArgumentOutOfRangeException(nameof(maximumReplicationCount));
        if (sparseSampleSizeExclusive < 1 || sparseSampleSizeExclusive > maximumSampleSize)
            throw new ArgumentOutOfRangeException(nameof(sparseSampleSizeExclusive));
        if (minimumDirectionalItemsForModerate < 1) throw new ArgumentOutOfRangeException(nameof(minimumDirectionalItemsForModerate));
        if (minimumDirectionalItemsForStrong < minimumDirectionalItemsForModerate)
            throw new ArgumentException("Strong aggregation requires at least as many directional items as moderate aggregation.", nameof(minimumDirectionalItemsForStrong));

        MaximumSampleSize = maximumSampleSize;
        MaximumReplicationCount = maximumReplicationCount;
        SparseSampleSizeExclusive = sparseSampleSizeExclusive;
        MinimumDirectionalItemsForModerate = minimumDirectionalItemsForModerate;
        MinimumDirectionalItemsForStrong = minimumDirectionalItemsForStrong;
    }

    public string PolicyId { get; }
    public DimensionWeights Weights { get; }
    public VerdictThresholds Thresholds { get; }
    public ScoringPenalties Penalties { get; }
    public int MaximumSampleSize { get; }
    public int MaximumReplicationCount { get; }
    public int SparseSampleSizeExclusive { get; }
    public int MinimumDirectionalItemsForModerate { get; }
    public int MinimumDirectionalItemsForStrong { get; }

    public static EvidenceScoringPolicy Default { get; } = new(
        "evidence-scoring-v1",
        new(20m, 15m, 15m, 15m, 15m, 15m, 5m),
        new(25m, 50m, 75m),
        new(20m, 3m, 7m, 1m, 4m, 8m, 100m));
}
