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

    public static string GetCompletionTarget(WorkflowState expectedCurrentState)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrentState);

        return CompletionTransitions.TryGetValue(expectedCurrentState.DatabaseValue, out var targetState)
            ? targetState
            : throw new ArgumentException(
                $"Workflow state '{expectedCurrentState.DatabaseValue}' does not have a supported completion transition.",
                nameof(expectedCurrentState));
    }
}
