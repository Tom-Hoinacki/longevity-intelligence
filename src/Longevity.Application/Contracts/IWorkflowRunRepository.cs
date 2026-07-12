using Longevity.Domain.Workflow;

namespace Longevity.Application.Contracts;

public sealed record ClaimedWorkflowRun(WorkflowRunId WorkflowRunId, WorkflowState State);

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

public interface IWorkflowRunRepository
{
    Task<ClaimedWorkflowRun?> TryClaimNextRunnableAsync(CancellationToken cancellationToken);

    Task<WorkflowRunCompletionResult> CompleteClaimedPhaseAsync(
        WorkflowRunId workflowRunId,
        WorkflowState expectedCurrentState,
        int expectedVersion,
        CancellationToken cancellationToken);
}
