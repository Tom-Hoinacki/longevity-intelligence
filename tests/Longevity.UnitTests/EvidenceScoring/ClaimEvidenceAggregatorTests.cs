using System.Collections;
using Longevity.Application.EvidenceScoring;

namespace Longevity.UnitTests.EvidenceScoring;

public sealed class ClaimEvidenceAggregatorTests
{
    private readonly EvidenceScoringEngine engine = new();
    private readonly ClaimEvidenceAggregator aggregator = new();

    [Fact]
    public void Three_strong_supporting_items_produce_a_strong_claim_result()
    {
        var result = aggregator.Aggregate([Score("a"), Score("b"), Score("c")]);

        Assert.Equal(100m, result.Score);
        Assert.Equal(EvidenceVerdict.Strong, result.Verdict);
        Assert.Equal(3, result.SupportingCount);
        Assert.Equal(0, result.ContradictingCount);
    }

    [Fact]
    public void Mostly_strong_contradicting_evidence_produces_contradictory_verdict()
    {
        var result = aggregator.Aggregate([
            Score("support"),
            Score("against-1", ClaimAlignment.Contradicts),
            Score("against-2", ClaimAlignment.Contradicts)]);

        Assert.Equal(EvidenceVerdict.Contradictory, result.Verdict);
        Assert.Equal(1, result.SupportingCount);
        Assert.Equal(2, result.ContradictingCount);
        Assert.Contains("aggregate.contradiction_dominates", result.ReasonCodes);
    }

    [Fact]
    public void One_excellent_study_cannot_overpower_numerous_strong_contradictions()
    {
        var evidence = new[] { Score("support") }
            .Concat(Enumerable.Range(1, 5).Select(index => Score($"against-{index}", ClaimAlignment.Contradicts)));

        var result = aggregator.Aggregate(evidence);

        Assert.Equal(EvidenceVerdict.Contradictory, result.Verdict);
        Assert.True(result.Score < EvidenceScoringPolicy.Default.Thresholds.Limited);
    }

    [Fact]
    public void Mixed_high_quality_evidence_is_not_misleadingly_strong()
    {
        var result = aggregator.Aggregate([Score("support"), Score("against", ClaimAlignment.Contradicts)]);

        Assert.Equal(EvidenceVerdict.Limited, result.Verdict);
        Assert.InRange(result.Score, 25m, 49.99m);
    }

    [Fact]
    public void Disqualified_evidence_is_counted_but_excluded_from_strength()
    {
        var result = aggregator.Aggregate([
            Score("a"), Score("b"), Score("c"), Score("retracted", retracted: true)]);

        Assert.Equal(EvidenceVerdict.Strong, result.Verdict);
        Assert.Equal(1, result.DisqualifiedCount);
        Assert.Contains("aggregate.disqualified_evidence_excluded", result.ReasonCodes);
    }

    [Fact]
    public void Empty_or_all_disqualified_aggregate_is_insufficient()
    {
        var empty = aggregator.Aggregate([]);
        var disqualified = aggregator.Aggregate([Score("retracted", retracted: true)]);

        Assert.Equal(EvidenceVerdict.Insufficient, empty.Verdict);
        Assert.Equal(EvidenceVerdict.Insufficient, disqualified.Verdict);
        Assert.Equal(1, disqualified.DisqualifiedCount);
    }

    [Fact]
    public void Aggregate_is_input_order_independent_and_ids_are_sorted()
    {
        var first = aggregator.Aggregate([Score("z"), Score("a"), Score("m", ClaimAlignment.Contradicts)]);
        var second = aggregator.Aggregate([Score("m", ClaimAlignment.Contradicts), Score("z"), Score("a")]);

        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Verdict, second.Verdict);
        Assert.Equal(first.ReasonCodes, second.ReasonCodes);
        Assert.Equal(new[] { "a", "m", "z" }, first.EvidenceIds);
    }

    [Fact]
    public void Aggregate_collections_are_immutable_and_reason_order_is_stable()
    {
        var result = aggregator.Aggregate([Score("support"), Score("against", ClaimAlignment.Contradicts), Score("retracted", retracted: true)]);

        Assert.Equal(new[] { "aggregate.supporting_evidence_present", "aggregate.contradicting_evidence_present", "aggregate.disqualified_evidence_excluded", "aggregate.breadth_limited" }, result.ReasonCodes);
        Assert.Throws<NotSupportedException>(() => ((IList)result.ReasonCodes).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)result.EvidenceIds).Clear());
    }

    [Fact]
    public void Duplicate_aggregate_identity_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => aggregator.Aggregate([Score("same"), Score("same")]));
    }

    [Fact]
    public void Results_from_a_different_policy_are_rejected()
    {
        var otherPolicy = EvidenceScoringPolicyTests.Policy(policyId: "other-policy");
        var otherResult = new EvidenceScoringEngine(otherPolicy).Evaluate(EvidenceScoringPolicyTests.Assessment());

        Assert.Throws<ArgumentException>(() => aggregator.Aggregate([otherResult]));
    }

    [Fact]
    public void Neutral_items_do_not_inflate_directional_breadth()
    {
        var result = aggregator.Aggregate([
            Score("support"),
            Score("neutral-1", ClaimAlignment.Neutral),
            Score("neutral-2", ClaimAlignment.Neutral)]);

        Assert.Equal(33.33m, result.Score);
        Assert.Equal(EvidenceVerdict.Limited, result.Verdict);
        Assert.Contains("aggregate.breadth_limited", result.ReasonCodes);
    }

    [Fact]
    public void A_single_supporting_item_is_breadth_limited()
    {
        var result = aggregator.Aggregate([Score("only")]);

        Assert.NotEqual(EvidenceVerdict.Strong, result.Verdict);
        Assert.Contains("aggregate.breadth_limited", result.ReasonCodes);
    }

    private EvidenceScoreResult Score(
        string id,
        ClaimAlignment alignment = ClaimAlignment.Supports,
        bool retracted = false) =>
        engine.Evaluate(EvidenceScoringPolicyTests.Assessment(
            evidenceId: id,
            studyDesign: StudyDesign.MetaAnalysis,
            alignment: alignment,
            isRetracted: retracted));
}
