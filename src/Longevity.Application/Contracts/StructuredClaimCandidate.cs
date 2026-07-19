using System.Text.Json;
using System.Text.RegularExpressions;

namespace Longevity.Application.Contracts;

public sealed record StructuredClaimCandidate(
    string AssetSlug,
    string AssetName,
    string AssetType,
    string? AssetSummary,
    string? ClaimType,
    string? TargetSystem,
    string? Population,
    string? OutcomeMeasured,
    string EvidenceLevel,
    string EvidenceDirection,
    string? EffectSummary,
    string Limitations,
    string SupportingExcerpt,
    int SampleSize,
    int ReplicationCount,
    string Directness,
    string RiskOfBias,
    string Consistency,
    string PublicationStatus,
    bool IsRetracted,
    bool HasSeriousMethodologicalLimitations,
    string? ConflictOfInterest);

public static class StructuredClaimCandidateParser
{
    public const string SchemaVersion = "claim-candidate-v2";
    public const int MaximumClaimTextLength = 5_000;
    public const int MaximumExcerptLength = 2_000;

    private static readonly Regex AssetSlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly IReadOnlySet<string> AssetTypes = Set("compound", "drug_category", "intervention", "behavior", "device", "diagnostic", "biomarker", "technology");
    private static readonly IReadOnlySet<string> EvidenceLevels = Set("mechanistic", "animal", "case_report", "cross_sectional", "observational", "randomized_controlled_trial", "systematic_review", "meta_analysis");
    private static readonly IReadOnlySet<string> EvidenceDirections = Set("supports", "contradicts", "neutral");
    private static readonly IReadOnlySet<string> DirectnessValues = Set("indirect", "partially_direct", "direct");
    private static readonly IReadOnlySet<string> RiskOfBiasValues = Set("critical", "high", "moderate", "low");
    private static readonly IReadOnlySet<string> ConsistencyValues = Set("strongly_contradictory", "mixed", "unknown", "consistent", "highly_consistent");
    private static readonly IReadOnlySet<string> PublicationStatuses = Set("unpublished", "preprint", "peer_reviewed");
    private static readonly IReadOnlySet<string> ConflictValues = Set("none", "low", "moderate", "high");

    public static bool TryParse(string json, out StructuredClaimCandidate? candidate, out IReadOnlyList<string> errors)
    {
        candidate = null;
        var problems = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                problems.Add("candidate_not_object");
            }
            else
            {
                var root = document.RootElement;
                candidate = new StructuredClaimCandidate(
                    Required(root, "assetSlug", 100, problems),
                    Required(root, "assetName", 200, problems),
                    Required(root, "assetType", 50, problems),
                    Optional(root, "assetSummary", 1_000, problems),
                    Optional(root, "claimType", 100, problems),
                    Optional(root, "targetSystem", 200, problems),
                    Optional(root, "population", 1_000, problems),
                    Optional(root, "outcomeMeasured", 1_000, problems),
                    Required(root, "evidenceLevel", 100, problems),
                    Required(root, "evidenceDirection", 20, problems),
                    Optional(root, "effectSummary", 2_000, problems),
                    Required(root, "limitations", 2_000, problems),
                    Required(root, "supportingExcerpt", MaximumExcerptLength, problems),
                    Integer(root, "sampleSize", 0, 10_000_000, problems),
                    Integer(root, "replicationCount", 0, 10_000, problems),
                    Required(root, "directness", 30, problems),
                    Required(root, "riskOfBias", 20, problems),
                    Required(root, "consistency", 30, problems),
                    Required(root, "publicationStatus", 30, problems),
                    Boolean(root, "isRetracted", problems),
                    Boolean(root, "hasSeriousMethodologicalLimitations", problems),
                    Optional(root, "conflictOfInterest", 20, problems));

                Validate(candidate, problems);
            }
        }
        catch (JsonException) { problems.Add("candidate_json_invalid"); }

        if (problems.Count > 0) candidate = null;
        errors = problems.Distinct(StringComparer.Ordinal).ToArray();
        return errors.Count == 0;
    }

    private static void Validate(StructuredClaimCandidate value, List<string> errors)
    {
        if (!AssetSlugPattern.IsMatch(value.AssetSlug)) errors.Add("asset_slug_invalid");
        In(value.AssetType, AssetTypes, "asset_type_unsupported", errors);
        In(value.EvidenceLevel, EvidenceLevels, "evidence_level_unsupported", errors);
        In(value.EvidenceDirection, EvidenceDirections, "evidence_direction_invalid", errors);
        In(value.Directness, DirectnessValues, "directness_unsupported", errors);
        In(value.RiskOfBias, RiskOfBiasValues, "risk_of_bias_unsupported", errors);
        In(value.Consistency, ConsistencyValues, "consistency_unsupported", errors);
        In(value.PublicationStatus, PublicationStatuses, "publication_status_unsupported", errors);
        if (value.ConflictOfInterest is not null) In(value.ConflictOfInterest, ConflictValues, "conflict_of_interest_unsupported", errors);
    }

    private static string Required(JsonElement root, string name, int maximumLength, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            errors.Add($"{name}_required");
            return string.Empty;
        }
        var text = value.GetString()!.Trim();
        if (text.Length > maximumLength) errors.Add($"{name}_too_long");
        return text;
    }

    private static string? Optional(JsonElement root, string name, int maximumLength, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) { errors.Add($"{name}_invalid"); return null; }
        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > maximumLength) errors.Add($"{name}_too_long");
        return text;
    }

    private static int Integer(JsonElement root, string name, int minimum, int maximum, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt32(out var number) || number < minimum || number > maximum)
        {
            errors.Add($"{name}_range");
            return minimum;
        }
        return number;
    }

    private static bool Boolean(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add($"{name}_required");
            return false;
        }
        return value.GetBoolean();
    }

    private static void In(string value, IReadOnlySet<string> supported, string error, List<string> errors)
    { if (!supported.Contains(value)) errors.Add(error); }

    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.Ordinal);
}
