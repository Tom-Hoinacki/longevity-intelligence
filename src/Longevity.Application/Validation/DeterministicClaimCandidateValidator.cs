using System.Text.Json;
using Longevity.Application.Contracts;
using Longevity.Application.EvidenceScoring;

namespace Longevity.Application.Validation;

public sealed record DeterministicScoringArtifact(
    string PolicyId,
    decimal Score,
    decimal PublicScore,
    string Verdict,
    string Alignment,
    IReadOnlyList<string> ReasonCodes);

public sealed class DeterministicClaimCandidateValidator
    : IClaimCandidateValidator
{
    private readonly EvidenceScoringEngine scoringEngine;

    public DeterministicClaimCandidateValidator() : this(new EvidenceScoringEngine()) { }
    public DeterministicClaimCandidateValidator(EvidenceScoringEngine scoringEngine) => this.scoringEngine = scoringEngine;

    public Task<DeterministicValidationResult> ValidateAsync(ClaimCandidateForValidation candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        if (candidate.ClaimText.Length > StructuredClaimCandidateParser.MaximumClaimTextLength) errors.Add("claim_text_too_long");

        StructuredClaimCandidate? structured = null;
        if (!StructuredClaimCandidateParser.TryParse(candidate.StructuredCandidateJson, out structured, out var parserErrors))
            errors.AddRange(parserErrors);

        if (string.IsNullOrWhiteSpace(candidate.NormalizedSourceText)) errors.Add("normalized_source_missing");
        if (structured is not null && !candidate.NormalizedSourceText.Contains(structured.SupportingExcerpt, StringComparison.Ordinal))
            errors.Add("supporting_excerpt_not_found");

        DeterministicScoringArtifact? scoring = null;
        if (errors.Count == 0 && structured is not null)
        {
            var result = scoringEngine.Evaluate(new EvidenceAssessment(
                candidate.CandidateId.Value.ToString("N"),
                StudyDesignValue(structured.EvidenceLevel),
                structured.SampleSize,
                structured.ReplicationCount,
                DirectnessValue(structured.Directness),
                RiskOfBiasValue(structured.RiskOfBias),
                ConsistencyValue(structured.Consistency),
                PublicationStatusValue(structured.PublicationStatus),
                structured.IsRetracted,
                structured.HasSeriousMethodologicalLimitations,
                ConflictValue(structured.ConflictOfInterest),
                AlignmentValue(structured.EvidenceDirection)));
            scoring = new(
                result.PolicyId,
                result.Score,
                decimal.Round(result.Score / 20m, 1, MidpointRounding.AwayFromZero),
                Snake(result.Verdict),
                Snake(result.Alignment),
                result.ReasonCodes);
        }

        var passed = errors.Count == 0;
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = StructuredClaimCandidateParser.SchemaVersion,
            passed,
            errors,
            scoring
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Task.FromResult(new DeterministicValidationResult(passed, json));
    }

    private static StudyDesign StudyDesignValue(string value) => value switch
    {
        "mechanistic" => StudyDesign.Mechanistic,
        "animal" => StudyDesign.Animal,
        "case_report" => StudyDesign.CaseReport,
        "cross_sectional" => StudyDesign.CrossSectional,
        "observational" => StudyDesign.Observational,
        "randomized_controlled_trial" => StudyDesign.RandomizedControlledTrial,
        "systematic_review" => StudyDesign.SystematicReview,
        "meta_analysis" => StudyDesign.MetaAnalysis,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static EvidenceDirectness DirectnessValue(string value) => value switch
    { "indirect" => EvidenceDirectness.Indirect, "partially_direct" => EvidenceDirectness.PartiallyDirect, "direct" => EvidenceDirectness.Direct, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static RiskOfBias RiskOfBiasValue(string value) => value switch
    { "critical" => RiskOfBias.Critical, "high" => RiskOfBias.High, "moderate" => RiskOfBias.Moderate, "low" => RiskOfBias.Low, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static EvidenceConsistency ConsistencyValue(string value) => value switch
    { "strongly_contradictory" => EvidenceConsistency.StronglyContradictory, "mixed" => EvidenceConsistency.Mixed, "unknown" => EvidenceConsistency.Unknown, "consistent" => EvidenceConsistency.Consistent, "highly_consistent" => EvidenceConsistency.HighlyConsistent, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static PublicationStatus PublicationStatusValue(string value) => value switch
    { "unpublished" => PublicationStatus.Unpublished, "preprint" => PublicationStatus.Preprint, "peer_reviewed" => PublicationStatus.PeerReviewed, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static ConflictOfInterestSeverity? ConflictValue(string? value) => value switch
    { null => null, "none" => ConflictOfInterestSeverity.None, "low" => ConflictOfInterestSeverity.Low, "moderate" => ConflictOfInterestSeverity.Moderate, "high" => ConflictOfInterestSeverity.High, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static ClaimAlignment AlignmentValue(string value) => value switch
    { "supports" => ClaimAlignment.Supports, "contradicts" => ClaimAlignment.Contradicts, "neutral" => ClaimAlignment.Neutral, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string Snake<T>(T value) where T : struct, Enum =>
        string.Concat(value.ToString().Select((character, index) => char.IsUpper(character) && index > 0 ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));
}

public static class DeterministicValidationArtifactParser
{
    private static readonly IReadOnlySet<string> Verdicts = new HashSet<string>(["strong", "moderate", "limited", "insufficient", "disqualified", "contradictory"], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> Alignments = new HashSet<string>(["supports", "contradicts", "neutral"], StringComparer.Ordinal);

    public static bool TryReadScoring(string json, out DeterministicScoringArtifact? scoring)
    {
        scoring = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schemaVersion) ||
                !string.Equals(schemaVersion.GetString(), StructuredClaimCandidateParser.SchemaVersion, StringComparison.Ordinal) ||
                !root.TryGetProperty("passed", out var passed) || passed.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("scoring", out var value) || value.ValueKind != JsonValueKind.Object)
                return false;
            var policyId = value.GetProperty("policyId").GetString();
            var verdict = value.GetProperty("verdict").GetString();
            var alignment = value.GetProperty("alignment").GetString();
            if (string.IsNullOrWhiteSpace(policyId) || verdict is null || !Verdicts.Contains(verdict) ||
                alignment is null || !Alignments.Contains(alignment) ||
                !value.GetProperty("score").TryGetDecimal(out var score) || score is < 0 or > 100 ||
                !value.GetProperty("publicScore").TryGetDecimal(out var publicScore) || publicScore is < 0 or > 5 ||
                publicScore != decimal.Round(score / 20m, 1, MidpointRounding.AwayFromZero))
                return false;
            var reasons = value.GetProperty("reasonCodes").EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray();
            if (reasons.Length == 0) return false;
            scoring = new(policyId, score, publicScore, verdict, alignment, reasons);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }
}
