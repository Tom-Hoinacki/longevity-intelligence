using Longevity.Domain.Workflow;

namespace Longevity.Application.Contracts;

public interface IWorkflowRunProcessor
{
    Task<WorkflowRunProcessorResult> ProcessNextAsync(CancellationToken cancellationToken);
}

public interface IWorkflowRunPhaseHandler
{
    WorkflowState State { get; }

    Task<WorkflowRunPhaseHandlingResult> HandleAsync(ClaimedWorkflowRun claimedRun, CancellationToken cancellationToken);
}

public sealed record WorkflowRunPhaseHandlingResult(WorkflowState TargetState);

public enum WorkflowRunProcessorStatus
{
    NoWork,
    Completed,
    TerminalOutcome,
    RetryScheduled,
    TerminalFailure,
    Conflict
}

public sealed record WorkflowRunProcessorResult(
    WorkflowRunProcessorStatus Status,
    WorkflowRunId? WorkflowRunId,
    WorkflowState? State,
    int? Version)
{
    public static WorkflowRunProcessorResult NoWork() =>
        new(WorkflowRunProcessorStatus.NoWork, null, null, null);
}
