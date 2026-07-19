using Longevity.Domain.Workflow;

namespace Longevity.Application.Contracts;

public sealed record SubmittedAuthoritativeSource(
    string SourceType,
    string Title,
    string RawContent,
    string? CanonicalUrl,
    string? Doi = null,
    string? Pmid = null,
    string? ClinicalTrialsGovIdentifier = null);

public static class ScientificSourcePolicy
{
    public const int MaximumTitleLength = 500;
    public const int MaximumContentLength = 1_000_000;
    public const int MaximumUrlLength = 2_048;

    public static IReadOnlySet<string> SupportedSourceTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "journal_article",
        "preprint",
        "clinical_trial",
        "systematic_review",
        "meta_analysis"
    };

    public static string RequireSourceType(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        if (!SupportedSourceTypes.Contains(normalized))
            throw new ArgumentException("The source type is unsupported.", nameof(value));
        return normalized;
    }
}

public sealed record NormalizedScientificSource(
    SourceRecordId SourceRecordId,
    WorkflowRunId WorkflowRunId,
    string SourceIdentityKey,
    string Title,
    string NormalizedText,
    string SourceType = "unknown",
    string? CanonicalUrl = null,
    string? ContentHash = null,
    string NormalizationVersion = "unknown");
