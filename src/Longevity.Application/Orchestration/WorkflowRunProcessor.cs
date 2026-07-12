using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;

namespace Longevity.Application.Orchestration;

public sealed class WorkflowRunProcessor : IWorkflowRunProcessor
{
    private static readonly IReadOnlySet<WorkflowState> SupportedStates = new HashSet<WorkflowState>
    {
        WorkflowState.Extracting,
        WorkflowState.Validating,
        WorkflowState.Publishing
    };

    private static readonly IReadOnlySet<WorkflowState> TerminalCompletionStates = new HashSet<WorkflowState>
    {
        WorkflowState.NoCandidateExtracted,
        WorkflowState.ValidationFailed,
        WorkflowState.Published
    };

    private readonly IWorkflowRunRepository repository;
    private readonly IReadOnlyDictionary<WorkflowState, IWorkflowRunPhaseHandler> handlers;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan retryDelay;

    public WorkflowRunProcessor(
        IWorkflowRunRepository repository,
        IEnumerable<IWorkflowRunPhaseHandler> handlers,
        TimeProvider timeProvider,
        TimeSpan retryDelay)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (retryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay), "Retry delay must be positive.");
        }

        var suppliedHandlers = (handlers ?? throw new ArgumentNullException(nameof(handlers))).ToArray();
        if (suppliedHandlers.Any(handler => handler is null || !SupportedStates.Contains(handler.State)))
        {
            throw new ArgumentException("Handlers must target exactly one supported processing state.", nameof(handlers));
        }

        var grouped = suppliedHandlers.GroupBy(handler => handler.State).ToArray();
        if (grouped.Any(group => group.Count() != 1) || grouped.Length != SupportedStates.Count)
        {
            throw new ArgumentException("Exactly one handler is required for each supported processing state.", nameof(handlers));
        }

        this.handlers = grouped.ToDictionary(group => group.Key, group => group.Single());
        this.retryDelay = retryDelay;
    }

    public async Task<WorkflowRunProcessorResult> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var claimedRun = await repository.TryClaimNextRunnableAsync(cancellationToken);
        if (claimedRun is null)
        {
            return WorkflowRunProcessorResult.NoWork();
        }

        var handler = handlers[claimedRun.State];
        WorkflowRunPhaseHandlingResult handlingResult;
        try
        {
            handlingResult = await handler.HandleAsync(claimedRun, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = await repository.FailClaimedPhaseAsync(
                claimedRun.WorkflowRunId,
                claimedRun.State,
                claimedRun.Version,
                timeProvider.GetUtcNow().Add(retryDelay),
                exception.GetType().Name,
                cancellationToken);

            return new WorkflowRunProcessorResult(
                failure.Status switch
                {
                    WorkflowRunFailureStatus.RetryScheduled => WorkflowRunProcessorStatus.RetryScheduled,
                    WorkflowRunFailureStatus.TerminalFailure => WorkflowRunProcessorStatus.TerminalFailure,
                    WorkflowRunFailureStatus.Conflict => WorkflowRunProcessorStatus.Conflict,
                    _ => throw new InvalidOperationException($"Unknown workflow failure status '{failure.Status}'.")
                },
                failure.WorkflowRunId,
                failure.State,
                failure.Version);
        }

        var completion = await repository.CompleteClaimedPhaseAsync(
            claimedRun.WorkflowRunId,
            claimedRun.State,
            handlingResult.TargetState,
            claimedRun.Version,
            cancellationToken);

        return new WorkflowRunProcessorResult(
            completion.Status == WorkflowRunCompletionStatus.Conflict
                ? WorkflowRunProcessorStatus.Conflict
                : TerminalCompletionStates.Contains(handlingResult.TargetState)
                    ? WorkflowRunProcessorStatus.TerminalOutcome
                    : WorkflowRunProcessorStatus.Completed,
            completion.WorkflowRunId,
            completion.State,
            completion.Version);
    }
}
