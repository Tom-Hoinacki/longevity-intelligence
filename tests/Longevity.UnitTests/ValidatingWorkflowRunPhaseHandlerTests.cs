using Longevity.Application.Contracts;
using Longevity.Application.Orchestration;
using Longevity.Domain.Workflow;

namespace Longevity.UnitTests;

public sealed class ValidatingWorkflowRunPhaseHandlerTests
{
    [Fact]
    public void Advertises_validating_state() =>
        Assert.Equal(WorkflowState.Validating, new ValidatingWorkflowRunPhaseHandler(new FakeValidator(), new FakePersistence()).State);

    [Fact]
    public async Task Wrong_state_is_rejected_before_dependency_calls()
    {
        var persistence = new FakePersistence();
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            new ValidatingWorkflowRunPhaseHandler(new FakeValidator(), persistence).HandleAsync(Claim(WorkflowState.Extracting), CancellationToken.None));

        Assert.Contains("unsupported state", exception.Message);
        Assert.Equal(0, persistence.LoadCalls);
    }

    [Fact]
    public async Task Missing_or_empty_batch_is_an_operational_error()
    {
        var persistence = new FakePersistence { Candidates = [] };
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ValidatingWorkflowRunPhaseHandler(new FakeValidator(), persistence).HandleAsync(Claim(), CancellationToken.None));

        Assert.Contains("No complete candidate batch", exception.Message);
    }

    [Fact]
    public async Task Candidates_are_validated_in_version_then_ordinal_order()
    {
        var run = Claim();
        var candidates = new[] { Candidate(run, 2, 2), Candidate(run, 1, 2), Candidate(run, 1, 1) };
        var validator = new FakeValidator();
        var persistence = new FakePersistence { Candidates = candidates };

        await new ValidatingWorkflowRunPhaseHandler(validator, persistence).HandleAsync(run, CancellationToken.None);

        Assert.Equal([1, 1, 2], validator.Seen.Select(candidate => candidate.CandidateVersion));
        Assert.Equal([1, 2, 2], validator.Seen.Select(candidate => candidate.CandidateOrdinal));
        Assert.Equal([1, 1, 2], persistence.Updates!.Select(update => update.Candidate.CandidateVersion));
    }

    [Fact]
    public async Task All_pass_returns_human_approval_and_persists_once()
    {
        var run = Claim();
        var persistence = new FakePersistence { Candidates = [Candidate(run, 1, 1), Candidate(run, 1, 2)] };

        var result = await new ValidatingWorkflowRunPhaseHandler(new FakeValidator(), persistence).HandleAsync(run, CancellationToken.None);

        Assert.Equal(WorkflowState.AwaitingHumanApproval, result.TargetState);
        Assert.Equal(1, persistence.PersistCalls);
        Assert.Equal(2, persistence.Updates!.Count);
    }

    [Fact]
    public async Task One_or_more_failures_return_validation_failed()
    {
        var run = Claim();
        var validator = new FakeValidator { Result = new(false, "{}") };
        var persistence = new FakePersistence { Candidates = [Candidate(run, 1, 1), Candidate(run, 1, 2)] };

        var result = await new ValidatingWorkflowRunPhaseHandler(validator, persistence).HandleAsync(run, CancellationToken.None);

        Assert.Equal(WorkflowState.ValidationFailed, result.TargetState);
        Assert.Equal(2, validator.Calls);
    }

    [Fact]
    public async Task Identity_is_preserved_in_every_update()
    {
        var run = Claim();
        var candidate = Candidate(run, 5, 3);
        var persistence = new FakePersistence { Candidates = [candidate] };

        await new ValidatingWorkflowRunPhaseHandler(new FakeValidator(), persistence).HandleAsync(run, CancellationToken.None);

        Assert.Equal(run.WorkflowRunId, persistence.Updates![0].Candidate.WorkflowRunId);
        Assert.Equal(candidate.SourceRecordId, persistence.Updates[0].Candidate.SourceRecordId);
        Assert.Equal(5, persistence.Updates[0].Candidate.CandidateVersion);
        Assert.Equal(3, persistence.Updates[0].Candidate.CandidateOrdinal);
    }

    [Fact]
    public async Task Persistence_is_skipped_after_validator_failure()
    {
        var run = Claim();
        var persistence = new FakePersistence { Candidates = [Candidate(run, 1, 1)] };
        var validator = new FakeValidator { Exception = new InvalidOperationException("validator details") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ValidatingWorkflowRunPhaseHandler(validator, persistence).HandleAsync(run, CancellationToken.None));
        Assert.Equal(0, persistence.PersistCalls);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var run = Claim();
        var persistence = new FakePersistence { Candidates = [Candidate(run, 1, 1)] };
        var validator = new FakeValidator { Exception = new OperationCanceledException(cancellation.Token) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ValidatingWorkflowRunPhaseHandler(validator, persistence).HandleAsync(run, cancellation.Token));
    }

    [Fact]
    public void Validation_contract_requires_json_objects()
    {
        Assert.Throws<ArgumentException>(() => new DeterministicValidationResult(true, "[]"));
        Assert.Throws<ArgumentException>(() => new ClaimCandidateForValidation(new ClaimCandidateId(Guid.NewGuid()), new WorkflowRunId(Guid.NewGuid()), new SourceRecordId(Guid.NewGuid()), 1, 1, "claim", "not-json"));
    }

    private static ClaimedWorkflowRun Claim(WorkflowState? state = null) => new(new WorkflowRunId(Guid.NewGuid()), state ?? WorkflowState.Validating, 8);

    private static ClaimCandidateForValidation Candidate(ClaimedWorkflowRun run, int version, int ordinal) =>
        new(new ClaimCandidateId(Guid.NewGuid()), run.WorkflowRunId, new SourceRecordId(Guid.NewGuid()), version, ordinal, $"claim-{ordinal}", "{}");

    private sealed class FakeValidator : IClaimCandidateValidator
    {
        public DeterministicValidationResult Result { get; set; } = new(true, "{}");
        public Exception? Exception { get; set; }
        public int Calls { get; private set; }
        public List<ClaimCandidateForValidation> Seen { get; } = [];
        public Task<DeterministicValidationResult> ValidateAsync(ClaimCandidateForValidation candidate, CancellationToken cancellationToken)
        {
            Calls++;
            Seen.Add(candidate);
            if (Exception is not null) throw Exception;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakePersistence : IClaimCandidateValidationPersistence
    {
        public IReadOnlyList<ClaimCandidateForValidation> Candidates { get; set; } = [];
        public int LoadCalls { get; private set; }
        public int PersistCalls { get; private set; }
        public IReadOnlyList<ClaimCandidateValidationUpdate>? Updates { get; private set; }
        public Task<IReadOnlyList<ClaimCandidateForValidation>> LoadLatestCandidateBatchAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken)
        {
            LoadCalls++;
            return Task.FromResult(Candidates);
        }
        public Task PersistValidationResultsAsync(WorkflowRunId workflowRunId, IReadOnlyList<ClaimCandidateValidationUpdate> updates, CancellationToken cancellationToken)
        {
            PersistCalls++;
            Updates = updates;
            return Task.CompletedTask;
        }
    }
}
