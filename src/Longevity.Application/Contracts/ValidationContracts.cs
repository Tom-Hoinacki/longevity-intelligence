using System.Text.Json;
using Longevity.Domain.Workflow;

namespace Longevity.Application.Contracts;

public sealed record ClaimCandidateForValidation
{
    public ClaimCandidateForValidation(
        ClaimCandidateId candidateId,
        WorkflowRunId workflowRunId,
        SourceRecordId sourceRecordId,
        int candidateVersion,
        int candidateOrdinal,
        string claimText,
        string structuredCandidateJson,
        string? normalizedSourceText = null)
    {
        if (candidateVersion < 1) throw new ArgumentOutOfRangeException(nameof(candidateVersion));
        if (candidateOrdinal < 1) throw new ArgumentOutOfRangeException(nameof(candidateOrdinal));
        if (candidateId.Value == Guid.Empty) throw new ArgumentException("Candidate identity must be non-empty.", nameof(candidateId));
        if (workflowRunId.Value == Guid.Empty) throw new ArgumentException("Workflow-run identity must be non-empty.", nameof(workflowRunId));
        if (sourceRecordId.Value == Guid.Empty) throw new ArgumentException("Source-record identity must be non-empty.", nameof(sourceRecordId));
        CandidateId = candidateId;
        WorkflowRunId = workflowRunId;
        SourceRecordId = sourceRecordId;
        CandidateVersion = candidateVersion;
        CandidateOrdinal = candidateOrdinal;
        ClaimText = RequireNonEmpty(claimText, nameof(claimText));
        StructuredCandidateJson = RequireObjectJson(structuredCandidateJson, nameof(structuredCandidateJson));
        NormalizedSourceText = normalizedSourceText?.Trim() ?? string.Empty;
    }

    public ClaimCandidateId CandidateId { get; }
    public WorkflowRunId WorkflowRunId { get; }
    public SourceRecordId SourceRecordId { get; }
    public int CandidateVersion { get; }
    public int CandidateOrdinal { get; }
    public string ClaimText { get; }
    public string StructuredCandidateJson { get; }
    public string NormalizedSourceText { get; }

    private static string RequireNonEmpty(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A required validation value must be non-empty.", parameterName)
            : value.Trim();

    private static string RequireObjectJson(string value, string parameterName)
    {
        var json = RequireNonEmpty(value, parameterName);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Validation candidate JSON must have an object root.", parameterName);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Validation candidate JSON must be valid JSON.", parameterName, exception);
        }

        return json;
    }
}

public sealed record DeterministicValidationResult
{
    public DeterministicValidationResult(bool passed, string validationResultJson)
    {
        Passed = passed;
        ValidationResultJson = RequireObjectJson(validationResultJson);
    }

    public bool Passed { get; }
    public string ValidationResultJson { get; }

    private static string RequireObjectJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Validation result JSON must be non-empty.", nameof(value));
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Validation result JSON must have an object root.", nameof(value));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Validation result JSON must be valid JSON.", nameof(value), exception);
        }

        return value.Trim();
    }
}

public interface IClaimCandidateValidator
{
    Task<DeterministicValidationResult> ValidateAsync(
        ClaimCandidateForValidation candidate,
        CancellationToken cancellationToken);
}

public sealed record ClaimCandidateValidationUpdate(
    ClaimCandidateForValidation Candidate,
    DeterministicValidationResult Result);

public interface IClaimCandidateValidationPersistence
{
    Task<IReadOnlyList<ClaimCandidateForValidation>> LoadLatestCandidateBatchAsync(
        WorkflowRunId workflowRunId,
        CancellationToken cancellationToken);

    Task PersistValidationResultsAsync(
        WorkflowRunId workflowRunId,
        IReadOnlyList<ClaimCandidateValidationUpdate> updates,
        CancellationToken cancellationToken);
}
