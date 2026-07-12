using Longevity.Application.HumanReview;
using Longevity.Domain.Workflow;

namespace Longevity.UnitTests.HumanReview;

public sealed class HumanReviewServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Load_forwards_identity_and_token_and_returns_result()
    {
        var run = Run(); var batch = Batch(run); var persistence = new FakePersistence { Batch = batch };
        using var cancellation = new CancellationTokenSource();

        var result = await Service(persistence).LoadAsync(run, cancellation.Token);

        Assert.Same(batch, result); Assert.Equal(run, persistence.LoadedRun); Assert.Equal(cancellation.Token, persistence.LoadToken); Assert.Equal(1, persistence.LoadCalls);
    }

    [Fact] public async Task Load_returns_null_when_no_batch_exists() { var persistence = new FakePersistence(); Assert.Null(await Service(persistence).LoadAsync(Run(), default)); }
    [Fact] public async Task Load_exception_propagates() { var expected = new InvalidOperationException("load failure"); var persistence = new FakePersistence { LoadException = expected }; Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => Service(persistence).LoadAsync(Run(), default))); }

    [Fact]
    public async Task Approval_targets_approved_state()
    {
        var run = Run(); var persistence = new FakePersistence { Batch = Batch(run), Result = Result(run) };
        await Service(persistence).DecideAsync(Request(run), default);
        Assert.Equal(WorkflowState.Approved, persistence.Appended!.TargetState);
    }

    [Fact]
    public async Task Rejection_targets_rejected_state()
    {
        var run = Run(); var persistence = new FakePersistence { Batch = Batch(run), Result = Result(run, HumanReviewDecision.Reject, WorkflowState.Rejected) };
        await Service(persistence).DecideAsync(Request(run, HumanReviewDecision.Reject, reason: "because"), default);
        Assert.Equal(WorkflowState.Rejected, persistence.Appended!.TargetState);
    }

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task Rejection_requires_reason(string? reason)
    {
        var persistence = new FakePersistence();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(persistence).DecideAsync(Request(Run(), HumanReviewDecision.Reject, reason: reason), default));
        AssertNoCalls(persistence);
    }

    [Fact] public async Task Empty_workflow_identity_fails_before_persistence() => await AssertInvalidBeforeCalls(Request(new(Guid.Empty)), new FakePersistence());
    [Theory] [InlineData("")] [InlineData("   ")] public async Task Empty_reviewer_fails_before_persistence(string reviewer) => await AssertInvalidBeforeCalls(Request(Run(), reviewer: reviewer), new FakePersistence());
    [Theory] [InlineData("")] [InlineData("   ")] public async Task Empty_decision_id_fails_before_persistence(string decisionId) => await AssertInvalidBeforeCalls(Request(Run(), decisionId: decisionId), new FakePersistence());

    [Fact]
    public async Task Missing_batch_is_reported_without_sensitive_input()
    {
        var persistence = new FakePersistence();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(persistence).DecideAsync(Request(Run(), note: "private reviewer note"), default));
        Assert.DoesNotContain("private reviewer note", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, persistence.LoadCalls); Assert.Equal(0, persistence.AppendCalls);
    }

    [Fact]
    public async Task Decision_uses_batch_version_trims_values_and_uses_exact_time()
    {
        var run = Run(); var persistence = new FakePersistence { Batch = Batch(run, version: 17), Result = Result(run) };
        await Service(persistence).DecideAsync(Request(run, HumanReviewDecision.Reject, " reviewer ", " decision ", " reason ", " note "), default);
        var request = persistence.Appended!;
        Assert.Equal(17, request.ExpectedWorkflowVersion); Assert.Equal("reviewer", request.ReviewerIdentity); Assert.Equal("decision", request.DecisionId);
        Assert.Equal("reason", request.Reason); Assert.Equal("note", request.Note); Assert.Equal(Now, request.DecisionAt);
        Assert.Equal(1, persistence.LoadCalls); Assert.Equal(1, persistence.AppendCalls);
    }

    [Fact]
    public async Task Persistence_result_is_returned_unchanged()
    {
        var run = Run(); var expected = Result(run); var persistence = new FakePersistence { Batch = Batch(run), Result = expected };
        Assert.Same(expected, await Service(persistence).DecideAsync(Request(run), default));
    }

    [Fact]
    public async Task Cancellation_propagates_to_load()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var persistence = new FakePersistence { LoadException = new OperationCanceledException(cancellation.Token) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(persistence).DecideAsync(Request(Run()), cancellation.Token));
        Assert.Equal(cancellation.Token, persistence.LoadToken); Assert.Equal(0, persistence.AppendCalls);
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_append()
    {
        var run = Run(); using var cancellation = new CancellationTokenSource();
        var persistence = new FakePersistence { Batch = Batch(run), Result = Result(run) };
        await Service(persistence).DecideAsync(Request(run), cancellation.Token);
        Assert.Equal(cancellation.Token, persistence.AppendToken);
    }

    [Fact] public async Task Load_failure_during_decision_propagates() { var expected = new InvalidOperationException("load"); var persistence = new FakePersistence { LoadException = expected }; Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => Service(persistence).DecideAsync(Request(Run()), default))); Assert.Equal(0, persistence.AppendCalls); }
    [Fact] public async Task Append_failure_propagates_and_is_called_once() { var run = Run(); var expected = new InvalidOperationException("append"); var persistence = new FakePersistence { Batch = Batch(run), AppendException = expected }; Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => Service(persistence).DecideAsync(Request(run), default))); Assert.Equal(1, persistence.AppendCalls); }

    [Fact]
    public async Task Validation_exceptions_do_not_expose_sensitive_values()
    {
        const string sensitive = "private claim json and reviewer note";
        var persistence = new FakePersistence();
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => Service(persistence).DecideAsync(Request(Run(), HumanReviewDecision.Reject, reviewer: sensitive, decisionId: sensitive, reason: " ", note: sensitive), default));
        Assert.DoesNotContain(sensitive, exception.ToString(), StringComparison.Ordinal); AssertNoCalls(persistence);
    }

    private static async Task AssertInvalidBeforeCalls(HumanReviewDecisionRequest request, FakePersistence persistence) { await Assert.ThrowsAsync<ArgumentException>(() => Service(persistence).DecideAsync(request, default)); AssertNoCalls(persistence); }
    private static void AssertNoCalls(FakePersistence persistence) { Assert.Equal(0, persistence.LoadCalls); Assert.Equal(0, persistence.AppendCalls); }
    private static HumanReviewService Service(FakePersistence persistence) => new(persistence, new FixedTimeProvider(Now));
    private static WorkflowRunId Run() => new(Guid.NewGuid());
    private static PendingHumanReviewBatch Batch(WorkflowRunId run, int version = 9) => new(run, version, WorkflowState.AwaitingHumanApproval, [new(new(Guid.NewGuid()), run, new(Guid.NewGuid()), 1, 1, "sensitive claim text", "{\"private\":true}", new(true, "{}"))]);
    private static HumanReviewDecisionRequest Request(WorkflowRunId run, HumanReviewDecision decision = HumanReviewDecision.Approve, string reviewer = "reviewer", string decisionId = "decision", string? reason = null, string? note = null) => new(run, decision, reviewer, decisionId, reason, note);
    private static HumanReviewDecisionResult Result(WorkflowRunId run, HumanReviewDecision decision = HumanReviewDecision.Approve, WorkflowState? target = null) => new(run, "decision", decision, target ?? WorkflowState.Approved, Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class FakePersistence : IHumanReviewPersistence
    {
        public PendingHumanReviewBatch? Batch { get; init; } public HumanReviewDecisionResult? Result { get; init; }
        public Exception? LoadException { get; init; } public Exception? AppendException { get; init; }
        public int LoadCalls { get; private set; } public int AppendCalls { get; private set; }
        public WorkflowRunId LoadedRun { get; private set; } public CancellationToken LoadToken { get; private set; } public CancellationToken AppendToken { get; private set; }
        public HumanReviewPersistenceRequest? Appended { get; private set; }
        public Task<PendingHumanReviewBatch?> LoadPendingAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken) { LoadCalls++; LoadedRun = workflowRunId; LoadToken = cancellationToken; if (LoadException is not null) throw LoadException; return Task.FromResult(Batch); }
        public Task<HumanReviewDecisionResult> AppendDecisionAsync(HumanReviewPersistenceRequest request, CancellationToken cancellationToken) { AppendCalls++; Appended = request; AppendToken = cancellationToken; if (AppendException is not null) throw AppendException; return Task.FromResult(Result!); }
    }
}
