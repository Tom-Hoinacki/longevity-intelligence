using Longevity.Domain.Workflow;

namespace Longevity.Application.Contracts;

public sealed record ClaimedWorkflowRun(WorkflowRunId WorkflowRunId, WorkflowState State);

public interface IWorkflowRunRepository
{
    Task<ClaimedWorkflowRun?> TryClaimNextRunnableAsync(CancellationToken cancellationToken);
}
