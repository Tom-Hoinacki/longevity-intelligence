using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;

namespace Longevity.UnitTests;

public sealed class WorkflowProcessorContractTests
{
    [Fact]
    public void Claimed_run_carries_the_claimed_version()
    {
        var run = new ClaimedWorkflowRun(new WorkflowRunId(Guid.NewGuid()), WorkflowState.Extracting, 7);

        Assert.Equal(7, run.Version);
        Assert.Equal(WorkflowState.Extracting, run.State);
    }

    [Fact]
    public void Phase_handler_identifies_one_canonical_processing_state_and_accepts_claimed_run()
    {
        var handler = new TestHandler();

        Assert.Equal(WorkflowState.Extracting, handler.State);
        Assert.Contains(nameof(IWorkflowRunPhaseHandler.HandleAsync), typeof(IWorkflowRunPhaseHandler).GetMethods().Select(method => method.Name));
        Assert.Equal(typeof(Task), typeof(IWorkflowRunPhaseHandler).GetMethod(nameof(IWorkflowRunPhaseHandler.HandleAsync))!.ReturnType);
    }

    [Fact]
    public void Processor_outcomes_expose_all_required_statuses()
    {
        Assert.Equal(
            ["NoWork", "Completed", "RetryScheduled", "TerminalFailure", "Conflict"],
            Enum.GetNames<WorkflowRunProcessorStatus>());
        Assert.Equal(WorkflowRunProcessorStatus.NoWork, WorkflowRunProcessorResult.NoWork().Status);
    }

    private sealed class TestHandler : IWorkflowRunPhaseHandler
    {
        public WorkflowState State => WorkflowState.Extracting;

        public Task HandleAsync(ClaimedWorkflowRun claimedRun, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
