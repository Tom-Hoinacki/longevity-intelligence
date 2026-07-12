using System.Text.Json;
using Longevity.Domain.Workflow;

namespace Longevity.Application.Contracts;

public sealed record ExtractedClaimCandidate
{
    public ExtractedClaimCandidate(string claimText, string structuredCandidateJson)
    {
        ClaimText = RequireNonEmpty(claimText, nameof(claimText));
        StructuredCandidateJson = RequireObjectJson(structuredCandidateJson, nameof(structuredCandidateJson));
    }

    public string ClaimText { get; }
    public string StructuredCandidateJson { get; }

    private static string RequireNonEmpty(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A required extraction value must be non-empty.", parameterName)
            : value.Trim();

    private static string RequireObjectJson(string value, string parameterName)
    {
        var json = RequireNonEmpty(value, parameterName);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Structured candidate JSON must have an object root.", parameterName);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Structured candidate JSON must be valid JSON.", parameterName, exception);
        }

        return json;
    }
}

public sealed record ClaimExtractionExecutionMetadata
{
    public ClaimExtractionExecutionMetadata(
        string schemaVersion,
        string modelProvider,
        string modelName,
        string promptVersion,
        int? inputTokenCount = null,
        int? outputTokenCount = null,
        decimal? estimatedCost = null,
        int? latencyMilliseconds = null,
        string? traceIdentifier = null)
    {
        SchemaVersion = RequireNonEmpty(schemaVersion, nameof(schemaVersion));
        ModelProvider = RequireNonEmpty(modelProvider, nameof(modelProvider));
        ModelName = RequireNonEmpty(modelName, nameof(modelName));
        PromptVersion = RequireNonEmpty(promptVersion, nameof(promptVersion));
        InputTokenCount = RequireNonNegative(inputTokenCount, nameof(inputTokenCount));
        OutputTokenCount = RequireNonNegative(outputTokenCount, nameof(outputTokenCount));
        EstimatedCost = RequireNonNegative(estimatedCost, nameof(estimatedCost));
        LatencyMilliseconds = RequireNonNegative(latencyMilliseconds, nameof(latencyMilliseconds));
        TraceIdentifier = string.IsNullOrWhiteSpace(traceIdentifier) ? null : traceIdentifier.Trim();
    }

    public string SchemaVersion { get; }
    public string ModelProvider { get; }
    public string ModelName { get; }
    public string PromptVersion { get; }
    public int? InputTokenCount { get; }
    public int? OutputTokenCount { get; }
    public decimal? EstimatedCost { get; }
    public int? LatencyMilliseconds { get; }
    public string? TraceIdentifier { get; }

    private static string RequireNonEmpty(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A required extraction value must be non-empty.", parameterName)
            : value.Trim();

    private static T? RequireNonNegative<T>(T? value, string parameterName) where T : struct, IComparable<T> =>
        value.HasValue && value.Value.CompareTo(default) < 0
            ? throw new ArgumentOutOfRangeException(parameterName, "Extraction execution values cannot be negative.")
            : value;
}

public sealed record ClaimExtractionResult
{
    public ClaimExtractionResult(
        IReadOnlyList<ExtractedClaimCandidate> candidates,
        ClaimExtractionExecutionMetadata metadata)
    {
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        if (Candidates.Any(candidate => candidate is null))
        {
            throw new ArgumentException("An extraction candidate collection cannot contain null entries.", nameof(candidates));
        }
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public IReadOnlyList<ExtractedClaimCandidate> Candidates { get; }
    public ClaimExtractionExecutionMetadata Metadata { get; }
}

public sealed record ClaimExtractionPersistenceRequest
{
    public ClaimExtractionPersistenceRequest(
        ClaimedWorkflowRun claimedRun,
        NormalizedScientificSource source,
        ClaimExtractionResult extraction)
    {
        if (claimedRun.WorkflowRunId != source.WorkflowRunId)
        {
            throw new ArgumentException("The claimed workflow run and normalized source identities must match.", nameof(source));
        }

        ClaimedRun = claimedRun;
        Source = source;
        Extraction = extraction ?? throw new ArgumentNullException(nameof(extraction));
        if (extraction.Candidates.Any(candidate => candidate is null))
        {
            throw new ArgumentException("An extraction candidate collection cannot contain null entries.", nameof(extraction));
        }
        Candidates = extraction.Candidates
            .Select((candidate, index) => new ClaimExtractionCandidateRow(
                claimedRun.WorkflowRunId,
                source.SourceRecordId,
                claimedRun.Version,
                index + 1,
                candidate,
                extraction.Metadata))
            .ToArray();
    }

    public ClaimedWorkflowRun ClaimedRun { get; }
    public NormalizedScientificSource Source { get; }
    public ClaimExtractionResult Extraction { get; }
    public IReadOnlyList<ClaimExtractionCandidateRow> Candidates { get; }
}

public sealed record ClaimExtractionCandidateRow(
    WorkflowRunId WorkflowRunId,
    SourceRecordId SourceRecordId,
    int CandidateVersion,
    int CandidateOrdinal,
    ExtractedClaimCandidate Candidate,
    ClaimExtractionExecutionMetadata Metadata);

public interface IClaimExtractionPersistence
{
    Task<NormalizedScientificSource?> LoadNormalizedSourceAsync(
        WorkflowRunId workflowRunId,
        CancellationToken cancellationToken);

    Task PersistExtractionAsync(
        ClaimExtractionPersistenceRequest request,
        CancellationToken cancellationToken);
}
