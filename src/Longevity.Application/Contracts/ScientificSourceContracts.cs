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
