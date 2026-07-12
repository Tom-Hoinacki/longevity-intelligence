using Longevity.Application.Contracts;
using Longevity.Application.Orchestration;
using Longevity.Domain.Workflow;

namespace Longevity.UnitTests;

public sealed class WorkflowRunProcessorTests
{
    [Fact]
    public async Task No_work_returns_without_completion()
    {
        var repository = new FakeRepository(null);
        var processor = CreateProcessor(repository);

        var result = await processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(WorkflowRunProcessorStatus.NoWork, result.Status);
        Assert.Equal(0, repository.CompletionCalls);
    }

    [Theory]
    [InlineData("extracting")]
    [InlineData("validating")]
    [InlineData("publishing")]
    public async Task Dispatches_each_supported_state(string state)
    {
        var workflowState = WorkflowState.FromDatabaseValue(state);
        var handler = new RecordingHandler(workflowState);
        var repository = new FakeRepository(Claim(workflowState));
        var processor = CreateProcessor(repository, handler);

        await processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(workflowState, handler.ReceivedRun!.State);
    }

    [Fact]
    public async Task Successful_completion_preserves_claim_identity_state_and_version()
    {
        var run = Claim(WorkflowState.Extracting);
        var repository = new FakeRepository(run)
        {
            Completion = new(WorkflowRunCompletionStatus.Completed, run.WorkflowRunId, WorkflowState.CandidateExtracted, 9)
        };
        var processor = CreateProcessor(repository);

        var result = await processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(WorkflowRunProcessorStatus.Completed, result.Status);
        Assert.Equal(run, repository.CompletedRun);
    }

    [Fact]
    public async Task Completion_conflict_is_returned()
    {
        var repository = new FakeRepository(Claim(WorkflowState.Validating))
        {
            Completion = WorkflowRunCompletionResult.Conflict(new WorkflowRunId(Guid.NewGuid()))
        };

        var result = await CreateProcessor(repository).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(WorkflowRunProcessorStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Handler_failure_schedules_retry_with_fake_time_and_sanitized_summary()
    {
        var run = Claim(WorkflowState.Publishing);
        var repository = new FakeRepository(run)
        {
            Failure = new(WorkflowRunFailureStatus.RetryScheduled, run.WorkflowRunId, WorkflowState.Approved, 10, 1)
        };
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero));
        var processor = CreateProcessor(repository, [new ThrowingHandler(run.State)], clock, TimeSpan.FromMinutes(5));

        var result = await processor.ProcessNextAsync(CancellationToken.None);

        Assert.Equal(WorkflowRunProcessorStatus.RetryScheduled, result.Status);
        Assert.Equal(clock.GetUtcNow().AddMinutes(5), repository.RetryAt);
        Assert.Equal(nameof(InvalidOperationException), repository.FailureSummary);
        Assert.DoesNotContain("secret", repository.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(run, repository.FailedRun);
    }

    [Fact]
    public async Task Terminal_failure_is_returned()
    {
        var run = Claim(WorkflowState.Extracting);
        var repository = new FakeRepository(run)
        {
            Failure = new(WorkflowRunFailureStatus.TerminalFailure, run.WorkflowRunId, WorkflowState.NoCandidateExtracted, 10, 3)
        };

        var result = await CreateProcessor(repository, new ThrowingHandler(run.State)).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(WorkflowRunProcessorStatus.TerminalFailure, result.Status);
    }

    [Fact]
    public async Task Failure_conflict_is_returned()
    {
        var run = Claim(WorkflowState.Validating);
        var repository = new FakeRepository(run)
        {
            Failure = WorkflowRunFailureResult.Conflict(run.WorkflowRunId)
        };

        var result = await CreateProcessor(repository, new ThrowingHandler(run.State)).ProcessNextAsync(CancellationToken.None);

        Assert.Equal(WorkflowRunProcessorStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Cancellation_is_rethrown_and_not_recorded()
    {
        using var source = new CancellationTokenSource();
        var run = Claim(WorkflowState.Extracting);
        var handler = new CancellingHandler(run.State, source);
        var repository = new FakeRepository(run);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateProcessor(repository, handler).ProcessNextAsync(source.Token));
        Assert.Null(repository.FailedRun);
    }

    [Fact]
    public void Constructor_rejects_missing_duplicate_unsupported_and_invalid_delay()
    {
        var repository = new FakeRepository(null);
        Assert.Throws<ArgumentException>(() => new WorkflowRunProcessor(repository, [new RecordingHandler(WorkflowState.Extracting)], TimeProvider.System, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => new WorkflowRunProcessor(repository, [
            new RecordingHandler(WorkflowState.Extracting), new RecordingHandler(WorkflowState.Extracting),
            new RecordingHandler(WorkflowState.Validating), new RecordingHandler(WorkflowState.Publishing)], TimeProvider.System, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => new WorkflowRunProcessor(repository, [
            new RecordingHandler(WorkflowState.Extracting), new RecordingHandler(WorkflowState.Validating),
            new RecordingHandler(WorkflowState.Publishing), new RecordingHandler(WorkflowState.Received)], TimeProvider.System, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateProcessor(repository, [], TimeProvider.System, TimeSpan.Zero));
    }

    private static WorkflowRunProcessor CreateProcessor(
        FakeRepository repository,
        params IWorkflowRunPhaseHandler[] handlers) =>
        CreateProcessor(repository, handlers, TimeProvider.System, TimeSpan.FromSeconds(1));

    private static WorkflowRunProcessor CreateProcessor(
        FakeRepository repository,
        IWorkflowRunPhaseHandler[] handlers,
        TimeProvider? timeProvider = null,
        TimeSpan? delay = null) =>
        new(repository, CompleteHandlers(handlers), timeProvider ?? TimeProvider.System, delay ?? TimeSpan.FromSeconds(1));

    private static IEnumerable<IWorkflowRunPhaseHandler> CompleteHandlers(IWorkflowRunPhaseHandler[] handlers)
    {
        var byState = handlers.ToDictionary(handler => handler.State);
        foreach (var state in new[] { WorkflowState.Extracting, WorkflowState.Validating, WorkflowState.Publishing })
        {
            if (!byState.ContainsKey(state))
            {
                byState[state] = new RecordingHandler(state);
            }
        }

        return byState.Values;
    }

    private static ClaimedWorkflowRun Claim(WorkflowState state) => new(new WorkflowRunId(Guid.NewGuid()), state, 8);

    private sealed class RecordingHandler(WorkflowState state) : IWorkflowRunPhaseHandler
    {
        public WorkflowState State => state;
        public int Calls { get; private set; }
        public ClaimedWorkflowRun? ReceivedRun { get; private set; }
        public Task HandleAsync(ClaimedWorkflowRun claimedRun, CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedRun = claimedRun;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler(WorkflowState state) : IWorkflowRunPhaseHandler
    {
        public WorkflowState State => state;
        public Task HandleAsync(ClaimedWorkflowRun claimedRun, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("secret details");
    }

    private sealed class CancellingHandler(WorkflowState state, CancellationTokenSource source) : IWorkflowRunPhaseHandler
    {
        public WorkflowState State => state;
        public Task HandleAsync(ClaimedWorkflowRun claimedRun, CancellationToken cancellationToken)
        {
            source.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeRepository(ClaimedWorkflowRun? claimed) : IWorkflowRunRepository
    {
        public WorkflowRunCompletionResult Completion { get; set; } = new(WorkflowRunCompletionStatus.Completed, claimed?.WorkflowRunId ?? new WorkflowRunId(Guid.Empty), WorkflowState.CandidateExtracted, 9);
        public WorkflowRunFailureResult Failure { get; set; } = new(WorkflowRunFailureStatus.RetryScheduled, claimed?.WorkflowRunId ?? new WorkflowRunId(Guid.Empty), WorkflowState.SourceNormalized, 9, 1);
        public ClaimedWorkflowRun? CompletedRun { get; private set; }
        public ClaimedWorkflowRun? FailedRun { get; private set; }
        public DateTimeOffset RetryAt { get; private set; }
        public string? FailureSummary { get; private set; }
        public int CompletionCalls { get; private set; }
        public Task<ClaimedWorkflowRun?> TryClaimNextRunnableAsync(CancellationToken cancellationToken) => Task.FromResult(claimed);
        public Task<WorkflowRunCompletionResult> CompleteClaimedPhaseAsync(WorkflowRunId id, WorkflowState state, int version, CancellationToken cancellationToken)
        {
            CompletionCalls++;
            CompletedRun = new(id, state, version);
            return Task.FromResult(Completion);
        }
        public Task<WorkflowRunFailureResult> FailClaimedPhaseAsync(WorkflowRunId id, WorkflowState state, int version, DateTimeOffset retryAt, string? summary, CancellationToken cancellationToken)
        {
            FailedRun = new(id, state, version);
            RetryAt = retryAt;
            FailureSummary = summary;
            return Task.FromResult(Failure);
        }
    }
}
