using System.Text.Json;

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
    decimal? RelevanceScore,
    decimal? EvidenceScore,
    decimal? HypeScore,
    decimal? RiskScore,
    string? PlainEnglishVerdict);

public static class StructuredClaimCandidateParser
{
    public const string SchemaVersion = "claim-candidate-v1";

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
                    Required(root, "assetSlug", problems, slug: true),
                    Required(root, "assetName", problems),
                    Required(root, "assetType", problems),
                    Optional(root, "assetSummary"),
                    Optional(root, "claimType"),
                    Optional(root, "targetSystem"),
                    Optional(root, "population"),
                    Optional(root, "outcomeMeasured"),
                    Required(root, "evidenceLevel", problems),
                    Required(root, "evidenceDirection", problems),
                    Optional(root, "effectSummary"),
                    Required(root, "limitations", problems),
                    Score(root, "relevanceScore", problems),
                    Score(root, "evidenceScore", problems),
                    Score(root, "hypeScore", problems),
                    Score(root, "riskScore", problems),
                    Optional(root, "plainEnglishVerdict"));

                if (candidate.EvidenceDirection is not ("supports" or "contradicts" or "neutral"))
                    problems.Add("evidence_direction_invalid");
            }
        }
        catch (JsonException) { problems.Add("candidate_json_invalid"); }

        if (problems.Count > 0) candidate = null;
        errors = problems;
        return problems.Count == 0;
    }

    private static string Required(JsonElement root, string name, List<string> errors, bool slug = false)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            errors.Add($"{name}_required");
            return string.Empty;
        }
        var text = value.GetString()!.Trim();
        if (slug && !System.Text.RegularExpressions.Regex.IsMatch(text, "^[a-z0-9]+(?:-[a-z0-9]+)*$")) errors.Add("asset_slug_invalid");
        return text;
    }

    private static string? Optional(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static decimal? Score(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (!value.TryGetDecimal(out var score) || score is < 0 or > 5)
        {
            errors.Add($"{name}_range");
            return null;
        }
        return score;
    }
}
