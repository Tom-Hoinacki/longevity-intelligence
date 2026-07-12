namespace Longevity.Application.EvidenceScoring;

public sealed class ClaimEvidenceAggregator
{
    private readonly EvidenceScoringPolicy policy;

    public ClaimEvidenceAggregator(EvidenceScoringPolicy? policy = null) =>
        this.policy = policy ?? EvidenceScoringPolicy.Default;

    public ClaimEvidenceResult Aggregate(IEnumerable<EvidenceScoreResult> evidenceResults)
    {
        ArgumentNullException.ThrowIfNull(evidenceResults);
        var ordered = evidenceResults.OrderBy(item => item?.EvidenceId, StringComparer.Ordinal).ToArray();
        if (ordered.Any(item => item is null))
            throw new ArgumentException("An aggregate cannot contain null evidence results.", nameof(evidenceResults));
        var duplicate = ordered.GroupBy(item => item.EvidenceId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate evidence identity: {duplicate.Key}", nameof(evidenceResults));
        if (ordered.Any(item => !string.Equals(item.PolicyId, policy.PolicyId, StringComparison.Ordinal)))
            throw new ArgumentException("All evidence results must use the aggregator policy identity.", nameof(evidenceResults));

        var usable = ordered.Where(item => item.Verdict != EvidenceVerdict.Disqualified).ToArray();
        var supporting = usable.Where(item => item.Alignment == ClaimAlignment.Supports).ToArray();
        var contradicting = usable.Where(item => item.Alignment == ClaimAlignment.Contradicts).ToArray();
        var disqualifiedCount = ordered.Length - usable.Length;
        var reasons = new List<string>();

        if (supporting.Length > 0) reasons.Add("aggregate.supporting_evidence_present");
        if (contradicting.Length > 0) reasons.Add("aggregate.contradicting_evidence_present");
        if (disqualifiedCount > 0) reasons.Add("aggregate.disqualified_evidence_excluded");

        if (usable.Length == 0)
        {
            reasons.Add("aggregate.no_usable_evidence");
            return Result(0m, EvidenceVerdict.Insufficient, supporting.Length, contradicting.Length, disqualifiedCount, reasons, ordered);
        }

        var supportStrength = supporting.Sum(item => item.Score);
        var contradictionStrength = contradicting.Sum(item => item.Score);
        var directionalStrength = supportStrength + contradictionStrength;
        var supportShare = directionalStrength == 0m ? 0m : supportStrength / directionalStrength;
        var averageQuality = usable.Average(item => item.Score);
        var directionalCount = supporting.Length + contradicting.Length;
        var breadthFactor = Math.Min(1m, (decimal)directionalCount / policy.MinimumDirectionalItemsForStrong);
        var score = decimal.Round(averageQuality * supportShare * breadthFactor, 2, MidpointRounding.AwayFromZero);

        if (breadthFactor < 1m) reasons.Add("aggregate.breadth_limited");
        if (directionalStrength == 0m) reasons.Add("aggregate.no_directional_evidence");
        var contradictionDominates = contradictionStrength > supportStrength && contradicting.Length > 0;
        if (contradictionDominates) reasons.Add("aggregate.contradiction_dominates");

        var verdict = contradictionDominates ? EvidenceVerdict.Contradictory : ThresholdVerdict(score);
        if (verdict == EvidenceVerdict.Strong && directionalCount < policy.MinimumDirectionalItemsForStrong)
        {
            verdict = directionalCount >= policy.MinimumDirectionalItemsForModerate ? EvidenceVerdict.Moderate : EvidenceVerdict.Limited;
            reasons.Add("aggregate.minimum_count_cap");
        }
        else if (verdict == EvidenceVerdict.Moderate && directionalCount < policy.MinimumDirectionalItemsForModerate)
        {
            verdict = EvidenceVerdict.Limited;
            reasons.Add("aggregate.minimum_count_cap");
        }

        return Result(score, verdict, supporting.Length, contradicting.Length, disqualifiedCount, reasons, ordered);
    }

    private EvidenceVerdict ThresholdVerdict(decimal score) => score >= policy.Thresholds.Strong
        ? EvidenceVerdict.Strong
        : score >= policy.Thresholds.Moderate
            ? EvidenceVerdict.Moderate
            : score >= policy.Thresholds.Limited
                ? EvidenceVerdict.Limited
                : EvidenceVerdict.Insufficient;

    private static ClaimEvidenceResult Result(
        decimal score,
        EvidenceVerdict verdict,
        int supporting,
        int contradicting,
        int disqualified,
        IEnumerable<string> reasons,
        IEnumerable<EvidenceScoreResult> ordered)
    {
        var explanation = $"{supporting} supporting, {contradicting} contradicting, and {disqualified} disqualified evidence items produced {score:0.00}/100 and {verdict}.";
        return new(score, verdict, supporting, contradicting, disqualified, reasons, explanation, ordered.Select(item => item.EvidenceId));
    }
}
