using Longevity.Application.Contracts;
using Longevity.Application.Orchestration;
using Longevity.Domain.Workflow;

namespace Longevity.UnitTests;

public sealed class ExtractingWorkflowRunPhaseHandlerTests
{
    [Fact]
    public void Advertises_extracting_state()
    {
        Assert.Equal(WorkflowState.Extracting, new ExtractingWorkflowRunPhaseHandler(new FakeModel(), new FakePersistence()).State);
    }

    [Fact]
    public async Task Wrong_state_is_rejected_before_dependency_calls()
    {
        var model = new FakeModel();
        var persistence = new FakePersistence();
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new ExtractingWorkflowRunPhaseHandler(model, persistence)
                .HandleAsync(Claim(WorkflowState.Validating), CancellationToken.None));

        Assert.Contains("unsupported state", exception.Message);
        Assert.Equal(0, persistence.LoadCalls);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Missing_source_is_operational_error_without_model_call()
    {
        var model = new FakeModel();
        var persistence = new FakePersistence();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExtractingWorkflowRunPhaseHandler(model, persistence).HandleAsync(Claim(), CancellationToken.None));

        Assert.Contains("No normalized source", exception.Message);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Mismatched_source_identity_is_rejected_before_model_call()
    {
        var claim = Claim();
        var model = new FakeModel();
        var persistence = new FakePersistence { Source = Source(new WorkflowRunId(Guid.NewGuid())) };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExtractingWorkflowRunPhaseHandler(model, persistence).HandleAsync(claim, CancellationToken.None));

        Assert.Contains("does not belong", exception.Message);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Exact_normalized_source_is_passed_to_model_once()
    {
        var claim = Claim();
        var source = Source(claim.WorkflowRunId);
        var model = new FakeModel { Result = ResultWithCandidate() };
        var persistence = new FakePersistence { Source = source };

        await new ExtractingWorkflowRunPhaseHandler(model, persistence).HandleAsync(claim, CancellationToken.None);

        Assert.Equal(1, model.Calls);
        Assert.Same(source, model.ReceivedSource);
    }

    [Fact]
    public async Task Zero_candidates_return_terminal_outcome_and_skip_persistence()
    {
        var persistence = new FakePersistence { Source = null };
        var claim = Claim();
        persistence.Source = Source(claim.WorkflowRunId);
        var model = new FakeModel { Result = new ClaimExtractionResult([], Metadata()) };

        var result = await new ExtractingWorkflowRunPhaseHandler(model, persistence).HandleAsync(claim, CancellationToken.None);

        Assert.Equal(WorkflowState.NoCandidateExtracted, result.TargetState);
        Assert.Equal(0, persistence.PersistCalls);
    }

    [Fact]
    public async Task Candidates_are_persisted_with_claimed_version_and_candidate_target()
    {
        var claim = Claim(version: 11);
        var persistence = new FakePersistence { Source = Source(claim.WorkflowRunId) };
        var model = new FakeModel { Result = ResultWithCandidate() };

        var result = await new ExtractingWorkflowRunPhaseHandler(model, persistence).HandleAsync(claim, CancellationToken.None);

        Assert.Equal(WorkflowState.CandidateExtracted, result.TargetState);
        Assert.Equal(1, persistence.PersistCalls);
        Assert.Equal(11, persistence.Request!.ClaimedRun.Version);
    }

    [Fact]
    public async Task Persistence_is_not_called_after_model_failure_and_failure_propagates()
    {
        var claim = Claim();
        var persistence = new FakePersistence { Source = Source(claim.WorkflowRunId) };
        var model = new FakeModel { Exception = new InvalidOperationException("model details") };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExtractingWorkflowRunPhaseHandler(model, persistence).HandleAsync(claim, CancellationToken.None));

        Assert.Equal("model details", exception.Message);
        Assert.Equal(0, persistence.PersistCalls);
    }

    [Fact]
    public async Task Persistence_failure_propagates_without_translation()
    {
        var claim = Claim();
        var persistence = new FakePersistence { Source = Source(claim.WorkflowRunId), Exception = new InvalidOperationException("persistence details") };
        var model = new FakeModel { Result = ResultWithCandidate() };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExtractingWorkflowRunPhaseHandler(model, persistence).HandleAsync(claim, CancellationToken.None));

        Assert.Equal("persistence details", exception.Message);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var claim = Claim();
        var persistence = new FakePersistence { Source = Source(claim.WorkflowRunId) };
        var model = new FakeModel { Exception = new OperationCanceledException(cancellation.Token) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ExtractingWorkflowRunPhaseHandler(model, persistence).HandleAsync(claim, cancellation.Token));
        Assert.Equal(0, persistence.PersistCalls);
    }

    [Fact]
    public async Task Null_model_result_is_rejected_without_sensitive_content()
    {
        var claim = Claim();
        var persistence = new FakePersistence { Source = Source(claim.WorkflowRunId) };
        var model = new FakeModel { Result = null! };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExtractingWorkflowRunPhaseHandler(model, persistence).HandleAsync(claim, CancellationToken.None));

        Assert.DoesNotContain("normalized source text", exception.Message);
        Assert.DoesNotContain("structured", exception.Message);
    }

    private static ClaimExtractionResult ResultWithCandidate() =>
        new([new ExtractedClaimCandidate("claim", "{}")], Metadata());

    private static ClaimExtractionExecutionMetadata Metadata() => new("schema", "provider", "model", "prompt");
    private static ClaimedWorkflowRun Claim(WorkflowState? state = null, int version = 3) => new(new WorkflowRunId(Guid.NewGuid()), state ?? WorkflowState.Extracting, version);
    private static NormalizedScientificSource Source(WorkflowRunId runId) => new(new SourceRecordId(Guid.NewGuid()), runId, "doi:test", "title", "normalized source text");

    private sealed class FakeModel : IClaimExtractionModel
    {
        public ClaimExtractionResult Result { get; set; } = ResultWithCandidate();
        public Exception? Exception { get; set; }
        public int Calls { get; private set; }
        public NormalizedScientificSource? ReceivedSource { get; private set; }
        public Task<ClaimExtractionResult> ExtractAsync(NormalizedScientificSource source, CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedSource = source;
            if (Exception is not null) throw Exception;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakePersistence : IClaimExtractionPersistence
    {
        public NormalizedScientificSource? Source { get; set; }
        public Exception? Exception { get; set; }
        public int LoadCalls { get; private set; }
        public int PersistCalls { get; private set; }
        public ClaimExtractionPersistenceRequest? Request { get; private set; }
        public Task<NormalizedScientificSource?> LoadNormalizedSourceAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken)
        {
            LoadCalls++;
            return Task.FromResult(Source);
        }
        public Task PersistExtractionAsync(ClaimExtractionPersistenceRequest request, CancellationToken cancellationToken)
        {
            PersistCalls++;
            Request = request;
            if (Exception is not null) throw Exception;
            return Task.CompletedTask;
        }
    }
}
