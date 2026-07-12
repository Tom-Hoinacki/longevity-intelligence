using Longevity.Application.EvidenceScoring;

namespace Longevity.UnitTests.EvidenceScoring;

public sealed class EvidenceScoringPolicyTests
{
    [Fact]
    public void Default_policy_is_valid_and_documented_totals_are_stable()
    {
        var policy = EvidenceScoringPolicy.Default;

        Assert.Equal("evidence-scoring-v1", policy.PolicyId);
        Assert.Equal(100m, Sum(policy.Weights));
        Assert.True(policy.Thresholds.Limited < policy.Thresholds.Moderate);
        Assert.True(policy.Thresholds.Moderate < policy.Thresholds.Strong);
    }

    [Fact]
    public void Weight_total_must_be_exactly_one_hundred()
    {
        Assert.Throws<ArgumentException>(() => Policy(weights: new(99.9m, 0m, 0m, 0m, 0m, 0m, 0m)));
        Assert.Throws<ArgumentException>(() => Policy(weights: new(50.1m, 50m, 0m, 0m, 0m, 0m, 0m)));
    }

    [Fact]
    public void Negative_or_oversized_weights_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Policy(weights: new(-1m, 101m, 0m, 0m, 0m, 0m, 0m)));
    }

    [Theory]
    [InlineData(-1, 50, 75)]
    [InlineData(25, 25, 75)]
    [InlineData(25, 76, 75)]
    [InlineData(25, 50, 101)]
    public void Thresholds_must_be_bounded_and_strictly_ordered(double limited, double moderate, double strong)
    {
        Assert.Throws<ArgumentException>(() => Policy(thresholds: new((decimal)limited, (decimal)moderate, (decimal)strong)));
    }

    [Fact]
    public void Contradictory_penalty_configurations_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Policy(penalties: new(-1m, 3m, 7m, 1m, 4m, 8m, 100m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Policy(penalties: new(20m, 3m, 7m, 1m, 4m, 101m, 100m)));
        Assert.Throws<ArgumentException>(() => Policy(penalties: new(20m, 8m, 7m, 1m, 4m, 8m, 100m)));
        Assert.Throws<ArgumentException>(() => Policy(penalties: new(20m, 3m, 7m, 5m, 4m, 8m, 100m)));
        Assert.Throws<ArgumentException>(() => Policy(penalties: new(20m, 3m, 7m, 1m, 4m, 8m, 50m)));
    }

    [Fact]
    public void Empty_identity_and_invalid_validation_ranges_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Policy(policyId: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Policy(maximumSampleSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Policy(maximumReplicationCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Policy(sparseSampleSizeExclusive: 0));
        Assert.Throws<ArgumentException>(() => Policy(minimumDirectionalItemsForModerate: 3, minimumDirectionalItemsForStrong: 2));
    }

    [Fact]
    public void Assessment_rejects_empty_identity_and_negative_counts()
    {
        Assert.Throws<ArgumentException>(() => Assessment(evidenceId: ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(sampleSize: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(replicationCount: -1));
    }

    [Fact]
    public void Every_valid_enum_category_is_accepted()
    {
        var engine = new EvidenceScoringEngine();

        foreach (var value in Enum.GetValues<StudyDesign>()) engine.Evaluate(Assessment(studyDesign: value));
        foreach (var value in Enum.GetValues<EvidenceDirectness>()) engine.Evaluate(Assessment(directness: value));
        foreach (var value in Enum.GetValues<RiskOfBias>()) engine.Evaluate(Assessment(riskOfBias: value));
        foreach (var value in Enum.GetValues<EvidenceConsistency>()) engine.Evaluate(Assessment(consistency: value));
        foreach (var value in Enum.GetValues<PublicationStatus>()) engine.Evaluate(Assessment(publicationStatus: value));
        foreach (var value in Enum.GetValues<ConflictOfInterestSeverity>()) engine.Evaluate(Assessment(conflict: value));
        foreach (var value in Enum.GetValues<ClaimAlignment>()) engine.Evaluate(Assessment(alignment: value));
    }

    [Fact]
    public void Undefined_enum_values_are_explicitly_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(studyDesign: (StudyDesign)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(directness: (EvidenceDirectness)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(riskOfBias: (RiskOfBias)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(consistency: (EvidenceConsistency)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(publicationStatus: (PublicationStatus)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(conflict: (ConflictOfInterestSeverity)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(alignment: (ClaimAlignment)999));
    }

    [Fact]
    public void Policy_input_maxima_are_enforced_without_clamping()
    {
        var engine = new EvidenceScoringEngine(Policy(maximumSampleSize: 100, maximumReplicationCount: 5));

        engine.Evaluate(Assessment(sampleSize: 100, replicationCount: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Evaluate(Assessment(sampleSize: 101)));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Evaluate(Assessment(replicationCount: 6)));
    }

    private static decimal Sum(DimensionWeights value) =>
        value.StudyDesign + value.SampleSize + value.Replication + value.Directness + value.RiskOfBias + value.Consistency + value.PublicationStatus;

    internal static EvidenceScoringPolicy Policy(
        string policyId = "test-v1",
        DimensionWeights? weights = null,
        VerdictThresholds? thresholds = null,
        ScoringPenalties? penalties = null,
        int maximumSampleSize = 10_000_000,
        int maximumReplicationCount = 10_000,
        int sparseSampleSizeExclusive = 10,
        int minimumDirectionalItemsForModerate = 2,
        int minimumDirectionalItemsForStrong = 3) => new(
            policyId,
            weights ?? new(20m, 15m, 15m, 15m, 15m, 15m, 5m),
            thresholds ?? new(25m, 50m, 75m),
            penalties ?? new(20m, 3m, 7m, 1m, 4m, 8m, 100m),
            maximumSampleSize,
            maximumReplicationCount,
            sparseSampleSizeExclusive,
            minimumDirectionalItemsForModerate,
            minimumDirectionalItemsForStrong);

    internal static EvidenceAssessment Assessment(
        string evidenceId = "evidence-1",
        StudyDesign studyDesign = StudyDesign.RandomizedControlledTrial,
        int sampleSize = 1_000,
        int replicationCount = 5,
        EvidenceDirectness directness = EvidenceDirectness.Direct,
        RiskOfBias riskOfBias = RiskOfBias.Low,
        EvidenceConsistency consistency = EvidenceConsistency.HighlyConsistent,
        PublicationStatus publicationStatus = PublicationStatus.PeerReviewed,
        bool isRetracted = false,
        bool hasLimitations = false,
        ConflictOfInterestSeverity? conflict = null,
        ClaimAlignment alignment = ClaimAlignment.Supports) => new(
            evidenceId, studyDesign, sampleSize, replicationCount, directness, riskOfBias,
            consistency, publicationStatus, isRetracted, hasLimitations, conflict, alignment);
}
