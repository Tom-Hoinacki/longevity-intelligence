using Longevity.Domain.Workflow;

namespace Longevity.Application.Contracts;

public sealed record ClaimedWorkflowRun(WorkflowRunId WorkflowRunId, WorkflowState State, int Version);

public enum WorkflowRunCompletionStatus
{
    Completed,
    Conflict
}

public sealed record WorkflowRunCompletionResult(
    WorkflowRunCompletionStatus Status,
    WorkflowRunId WorkflowRunId,
    WorkflowState? State,
    int? Version)
{
    public bool Succeeded => Status == WorkflowRunCompletionStatus.Completed;

    public static WorkflowRunCompletionResult Conflict(WorkflowRunId workflowRunId) =>
        new(WorkflowRunCompletionStatus.Conflict, workflowRunId, null, null);
}

public enum WorkflowRunFailureStatus
{
    RetryScheduled,
    TerminalFailure,
    Conflict
}

public sealed record WorkflowRunFailureResult(
    WorkflowRunFailureStatus Status,
    WorkflowRunId WorkflowRunId,
    WorkflowState? State,
    int? Version,
    int? RetryCount)
{
    public bool Succeeded => Status is WorkflowRunFailureStatus.RetryScheduled or WorkflowRunFailureStatus.TerminalFailure;

    public static WorkflowRunFailureResult Conflict(WorkflowRunId workflowRunId) =>
        new(WorkflowRunFailureStatus.Conflict, workflowRunId, null, null, null);
}

public interface IWorkflowRunRepository
{
    Task<ClaimedWorkflowRun?> TryClaimNextRunnableAsync(CancellationToken cancellationToken);

    Task<WorkflowRunCompletionResult> CompleteClaimedPhaseAsync(
        WorkflowRunId workflowRunId,
        WorkflowState expectedCurrentState,
        int expectedVersion,
        CancellationToken cancellationToken);

    Task<WorkflowRunFailureResult> FailClaimedPhaseAsync(
        WorkflowRunId workflowRunId,
        WorkflowState expectedCurrentState,
        int expectedVersion,
        DateTimeOffset retryAt,
        string? sanitizedFailureSummary,
        CancellationToken cancellationToken);
}
