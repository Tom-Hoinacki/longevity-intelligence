using System.Collections;
using Longevity.Application.EvidenceScoring;

namespace Longevity.UnitTests.EvidenceScoring;

public sealed class EvidenceScoringEngineTests
{
    private readonly EvidenceScoringEngine engine = new();

    [Fact]
    public void High_quality_direct_replicated_consistent_evidence_is_strong()
    {
        var result = engine.Evaluate(EvidenceScoringPolicyTests.Assessment(studyDesign: StudyDesign.MetaAnalysis));

        Assert.Equal(100m, result.Score);
        Assert.Equal(EvidenceVerdict.Strong, result.Verdict);
        Assert.Equal("evidence-scoring-v1", result.PolicyId);
        Assert.Contains("100.00/100", result.Explanation);
    }

    [Fact]
    public void Retraction_is_disqualified_and_cannot_be_hidden_by_raw_quality()
    {
        var result = engine.Evaluate(EvidenceScoringPolicyTests.Assessment(isRetracted: true));

        Assert.Equal(0m, result.Score);
        Assert.Equal(EvidenceVerdict.Disqualified, result.Verdict);
        Assert.Contains(result.Penalties, value => value.Code == "penalty.retraction");
        Assert.Equal("verdict.retraction_override", result.ReasonCodes[^1]);
    }

    [Fact]
    public void Strong_contradiction_overrides_an_otherwise_high_score()
    {
        var result = engine.Evaluate(EvidenceScoringPolicyTests.Assessment(consistency: EvidenceConsistency.StronglyContradictory));

        Assert.True(result.Score >= EvidenceScoringPolicy.Default.Thresholds.Strong);
        Assert.Equal(EvidenceVerdict.Contradictory, result.Verdict);
        Assert.Equal("verdict.contradiction_override", result.ReasonCodes[^1]);
    }

    [Fact]
    public void Serious_limitations_conflicts_and_non_peer_review_are_explicit_penalties()
    {
        var baseline = engine.Evaluate(EvidenceScoringPolicyTests.Assessment());
        var limited = engine.Evaluate(EvidenceScoringPolicyTests.Assessment(hasLimitations: true));
        var conflict = engine.Evaluate(EvidenceScoringPolicyTests.Assessment(conflict: ConflictOfInterestSeverity.High));
        var preprint = engine.Evaluate(EvidenceScoringPolicyTests.Assessment(publicationStatus: PublicationStatus.Preprint));
        var unpublished = engine.Evaluate(EvidenceScoringPolicyTests.Assessment(publicationStatus: PublicationStatus.Unpublished));

        Assert.Equal(20m, baseline.Score - limited.Score);
        Assert.Equal(8m, baseline.Score - conflict.Score);
        Assert.Contains(preprint.Penalties, item => item.Code == "penalty.preprint");
        Assert.Contains(unpublished.Penalties, item => item.Code == "penalty.unpublished");
        Assert.True(unpublished.Score < preprint.Score);
        Assert.True(preprint.Score < baseline.Score);
    }

    [Fact]
    public void Very_sparse_unreplicated_evidence_is_capped_at_limited()
    {
        var result = engine.Evaluate(EvidenceScoringPolicyTests.Assessment(sampleSize: 9, replicationCount: 0));

        Assert.True(result.Score >= EvidenceScoringPolicy.Default.Thresholds.Moderate);
        Assert.Equal(EvidenceVerdict.Limited, result.Verdict);
        Assert.Contains("verdict.sparse_evidence_cap", result.ReasonCodes);
    }

    [Fact]
    public void Final_score_rounds_midpoints_away_from_zero_to_two_places()
    {
        var policy = EvidenceScoringPolicyTests.Policy(weights: new(1.005m, 98.995m, 0m, 0m, 0m, 0m, 0m));
        var result = new EvidenceScoringEngine(policy).Evaluate(EvidenceScoringPolicyTests.Assessment(studyDesign: StudyDesign.Mechanistic));

        Assert.Equal(99.15m, result.Score);
    }

    [Fact]
    public void Contributions_penalties_and_reasons_have_stable_order_and_are_immutable()
    {
        var input = EvidenceScoringPolicyTests.Assessment(
            hasLimitations: true,
            publicationStatus: PublicationStatus.Preprint,
            conflict: ConflictOfInterestSeverity.Moderate,
            isRetracted: true);
        var first = engine.Evaluate(input);
        var second = engine.Evaluate(input);

        Assert.Equal(new[] { "study_design", "sample_size", "replication", "directness", "risk_of_bias", "consistency", "publication_status" }, first.Contributions.Select(value => value.Dimension));
        Assert.Equal(new[] { "penalty.serious_methodological_limitations", "penalty.preprint", "penalty.conflict_moderate", "penalty.retraction" }, first.Penalties.Select(value => value.Code));
        Assert.Equal(first.ReasonCodes, second.ReasonCodes);
        Assert.Throws<NotSupportedException>(() => ((IList)first.Contributions).Add(new ScoreContribution("x", 1m)));
        Assert.Throws<NotSupportedException>(() => ((IList)first.Penalties).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)first.ReasonCodes).Clear());
    }

    [Fact]
    public void Same_input_always_produces_equivalent_values()
    {
        var assessment = EvidenceScoringPolicyTests.Assessment(conflict: ConflictOfInterestSeverity.Low);

        var first = engine.Evaluate(assessment);
        var second = engine.Evaluate(assessment);

        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Verdict, second.Verdict);
        Assert.Equal(first.Contributions, second.Contributions);
        Assert.Equal(first.Penalties, second.Penalties);
        Assert.Equal(first.ReasonCodes, second.ReasonCodes);
        Assert.Equal(first.Explanation, second.Explanation);
    }

    [Fact]
    public void Batch_is_sorted_rejects_duplicates_and_empty_is_valid()
    {
        var batch = engine.EvaluateBatch([
            EvidenceScoringPolicyTests.Assessment(evidenceId: "z"),
            EvidenceScoringPolicyTests.Assessment(evidenceId: "a")]);

        Assert.Equal(new[] { "a", "z" }, batch.Select(value => value.EvidenceId));
        Assert.Empty(engine.EvaluateBatch([]));
        Assert.Throws<ArgumentException>(() => engine.EvaluateBatch([
            EvidenceScoringPolicyTests.Assessment(evidenceId: "same"),
            EvidenceScoringPolicyTests.Assessment(evidenceId: "same")]));
    }

    [Fact]
    public void Batch_results_equal_individual_results_and_ignore_input_order()
    {
        var a = EvidenceScoringPolicyTests.Assessment(evidenceId: "a", sampleSize: 49);
        var b = EvidenceScoringPolicyTests.Assessment(evidenceId: "b", replicationCount: 2);

        var forward = engine.EvaluateBatch([a, b]);
        var reverse = engine.EvaluateBatch([b, a]);

        Assert.Equal(forward.Select(value => value.Score), reverse.Select(value => value.Score));
        Assert.Equal(engine.Evaluate(a).Score, forward[0].Score);
        Assert.Equal(engine.Evaluate(b).Verdict, forward[1].Verdict);
    }

    [Fact]
    public void Sample_size_and_replication_boundaries_are_monotonic()
    {
        AssertMonotonic(new[] { 0, 1, 9, 10, 49, 50, 199, 200, 999, 1_000 }, value => engine.Evaluate(EvidenceScoringPolicyTests.Assessment(sampleSize: value)).Score);
        AssertMonotonic(new[] { 0, 1, 2, 3, 4, 5 }, value => engine.Evaluate(EvidenceScoringPolicyTests.Assessment(replicationCount: value)).Score);
    }

    [Fact]
    public void Study_directness_bias_consistency_and_publication_are_monotonic()
    {
        AssertMonotonic(Enum.GetValues<StudyDesign>(), value => engine.Evaluate(EvidenceScoringPolicyTests.Assessment(studyDesign: value)).Score);
        AssertMonotonic(Enum.GetValues<EvidenceDirectness>(), value => engine.Evaluate(EvidenceScoringPolicyTests.Assessment(directness: value)).Score);
        AssertMonotonic(Enum.GetValues<RiskOfBias>(), value => engine.Evaluate(EvidenceScoringPolicyTests.Assessment(riskOfBias: value)).Score);
        AssertMonotonic(Enum.GetValues<EvidenceConsistency>(), value => engine.Evaluate(EvidenceScoringPolicyTests.Assessment(consistency: value)).Score);
        AssertMonotonic(Enum.GetValues<PublicationStatus>(), value => engine.Evaluate(EvidenceScoringPolicyTests.Assessment(publicationStatus: value)).Score);
    }

    [Fact]
    public void Increasing_conflict_severity_never_increases_score()
    {
        var scores = Enum.GetValues<ConflictOfInterestSeverity>()
            .Select(value => engine.Evaluate(EvidenceScoringPolicyTests.Assessment(conflict: value)).Score)
            .ToArray();

        Assert.True(scores.Zip(scores.Skip(1), (left, right) => left >= right).All(value => value));
    }

    private static void AssertMonotonic<T>(IEnumerable<T> values, Func<T, decimal> score)
    {
        var scores = values.Select(score).ToArray();
        Assert.True(scores.Zip(scores.Skip(1), (left, right) => left <= right).All(value => value), string.Join(", ", scores));
    }
}
