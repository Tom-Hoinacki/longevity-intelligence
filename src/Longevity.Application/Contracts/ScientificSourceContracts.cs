using Longevity.Domain.Workflow;

namespace Longevity.Application.Contracts;

public sealed record SubmittedAuthoritativeSource(
    string SourceType,
    string Title,
    string RawContent,
    string? CanonicalUrl);

public sealed record NormalizedScientificSource(
    SourceRecordId SourceRecordId,
    WorkflowRunId WorkflowRunId,
    string SourceIdentityKey,
    string Title,
    string NormalizedText);
