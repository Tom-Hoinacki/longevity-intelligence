using Longevity.Domain.Workflow;

namespace Longevity.Infrastructure.Persistence;

public static class WorkflowRunClaimPolicy
{
    public static IReadOnlyDictionary<string, string> StateTransitions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WorkflowState.SourceNormalized.DatabaseValue] = WorkflowState.Extracting.DatabaseValue,
            [WorkflowState.CandidateExtracted.DatabaseValue] = WorkflowState.Validating.DatabaseValue,
            [WorkflowState.Approved.DatabaseValue] = WorkflowState.Publishing.DatabaseValue
        };

    public const string ClaimNextRunnableSql = """
        WITH next_run AS (
            SELECT id
            FROM workflow.runs
            WHERE state IN ('source_normalized', 'candidate_extracted', 'approved')
              AND available_at <= now()
              AND retry_count < max_retries
            ORDER BY available_at, created_at, id
            FOR UPDATE SKIP LOCKED
            LIMIT 1
        )
        UPDATE workflow.runs AS run
        SET state = CASE run.state
                WHEN 'source_normalized' THEN 'extracting'
                WHEN 'candidate_extracted' THEN 'validating'
                WHEN 'approved' THEN 'publishing'
            END,
            started_at = COALESCE(run.started_at, now()),
            updated_at = now(),
            version = run.version + 1
        FROM next_run
        WHERE run.id = next_run.id
        RETURNING run.id, run.state;
        """;

    public static IReadOnlyDictionary<string, string> CompletionTransitions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WorkflowState.Extracting.DatabaseValue] = WorkflowState.CandidateExtracted.DatabaseValue,
            [WorkflowState.Validating.DatabaseValue] = WorkflowState.AwaitingHumanApproval.DatabaseValue,
            [WorkflowState.Publishing.DatabaseValue] = WorkflowState.Published.DatabaseValue
        };

    public static IReadOnlyDictionary<string, string> FailureRetryTransitions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WorkflowState.Extracting.DatabaseValue] = WorkflowState.SourceNormalized.DatabaseValue,
            [WorkflowState.Validating.DatabaseValue] = WorkflowState.CandidateExtracted.DatabaseValue,
            [WorkflowState.Publishing.DatabaseValue] = WorkflowState.Approved.DatabaseValue
        };

    public static IReadOnlyDictionary<string, string> FailureTerminalTransitions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WorkflowState.Extracting.DatabaseValue] = WorkflowState.NoCandidateExtracted.DatabaseValue,
            [WorkflowState.Validating.DatabaseValue] = WorkflowState.ValidationFailed.DatabaseValue,
            [WorkflowState.Publishing.DatabaseValue] = WorkflowState.PublicationFailed.DatabaseValue
        };

    public const string CompleteClaimedPhaseSql = """
        UPDATE workflow.runs
        SET state = $4,
            updated_at = now(),
            completed_at = CASE
                WHEN $2 = 'publishing' AND $4 = 'published' THEN now()
                ELSE completed_at
            END,
            version = version + 1
        WHERE id = $1
          AND state = $2
          AND version = $3
        RETURNING id, state, version;
        """;

    public const string FailClaimedPhaseSql = """
        UPDATE workflow.runs
        SET state = CASE
                WHEN retry_count + 1 < max_retries THEN $4
                ELSE $5
            END,
            retry_count = CASE
                WHEN retry_count + 1 < max_retries THEN retry_count + 1
                ELSE max_retries
            END,
            available_at = CASE
                WHEN retry_count + 1 < max_retries THEN $6
                ELSE available_at
            END,
            last_error_summary = CASE
                WHEN $7 IS NULL THEN last_error_summary
                ELSE NULLIF(left(btrim($7), 200), '')
            END,
            completed_at = CASE
                WHEN retry_count + 1 < max_retries THEN completed_at
                ELSE now()
            END,
            updated_at = now(),
            version = version + 1
        WHERE id = $1
          AND state = $2
          AND version = $3
        RETURNING id, state, version, retry_count;
        """;

    public static string GetCompletionTarget(WorkflowState expectedCurrentState)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrentState);

        return CompletionTransitions.TryGetValue(expectedCurrentState.DatabaseValue, out var targetState)
            ? targetState
            : throw new ArgumentException(
                $"Workflow state '{expectedCurrentState.DatabaseValue}' does not have a supported completion transition.",
                nameof(expectedCurrentState));
    }

    public static string GetFailureRetryTarget(WorkflowState expectedCurrentState)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrentState);

        return FailureRetryTransitions.TryGetValue(expectedCurrentState.DatabaseValue, out var targetState)
            ? targetState
            : throw new ArgumentException(
                $"Workflow state '{expectedCurrentState.DatabaseValue}' does not have a supported failure retry transition.",
                nameof(expectedCurrentState));
    }

    public static string GetFailureTerminalTarget(WorkflowState expectedCurrentState)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrentState);

        return FailureTerminalTransitions.TryGetValue(expectedCurrentState.DatabaseValue, out var targetState)
            ? targetState
            : throw new ArgumentException(
                $"Workflow state '{expectedCurrentState.DatabaseValue}' does not have a supported failure terminal transition.",
                nameof(expectedCurrentState));
    }
}
