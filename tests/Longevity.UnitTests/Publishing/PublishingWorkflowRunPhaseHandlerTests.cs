using Longevity.Application.Contracts;
using Longevity.Application.Publishing;
using Longevity.Domain.Workflow;

namespace Longevity.UnitTests.Publishing;

public sealed class PublishingWorkflowRunPhaseHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Advertises_publishing_state() =>
        Assert.Equal(WorkflowState.Publishing, Handler(new FakePersistence()).State);

    [Fact]
    public async Task Wrong_state_is_rejected_before_dependency_calls()
    {
        var persistence = new FakePersistence();
        await Assert.ThrowsAsync<PublicationInvariantException>(() => Handler(persistence).HandleAsync(Run(WorkflowState.Validating), default));
        Assert.Equal(0, persistence.LoadCalls);
        Assert.Equal(0, persistence.PublishCalls);
    }

    [Fact]
    public async Task Missing_batch_is_rejected()
    {
        var persistence = new FakePersistence { Batch = null };
        await Assert.ThrowsAsync<PublicationInvariantException>(() => Handler(persistence).HandleAsync(Run(), default));
        Assert.Equal(0, persistence.PublishCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Workflow_identity_and_version_must_match(bool differentRun, bool differentVersion)
    {
        var run = Run();
        var batch = Batch(run, workflowRunId: differentRun ? new(Guid.NewGuid()) : null, version: differentVersion ? run.Version + 1 : null);
        await RejectsBeforePublish(run, batch);
    }

    [Fact]
    public void Approval_and_reviewer_are_required()
    {
        var run = Run();
        Assert.Throws<ArgumentException>(() => Batch(run, approvalIdentity: " "));
        Assert.Throws<ArgumentException>(() => Batch(run, reviewerIdentity: " "));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Approval_timestamp_must_be_present_and_not_future(int mode)
    {
        var run = Run();
        var timestamp = mode == 0 ? default : Now.AddSeconds(1);
        await RejectsBeforePublish(run, Batch(run, approvedAt: timestamp));
    }

    [Fact]
    public void Null_claim_collection_is_rejected()
    {
        var run = Run();
        Assert.Throws<ArgumentNullException>(() => new ApprovedPublicationBatch(
            run.WorkflowRunId, run.Version, WorkflowState.Publishing, "approval-1", Now.AddMinutes(-1),
            "reviewer-1", Source(run), null!, []));
    }

    [Fact]
    public async Task Empty_and_null_claim_entries_are_rejected()
    {
        var run = Run();
        await RejectsBeforePublish(run, Batch(run, claims: []));
        await RejectsBeforePublish(run, Batch(run, claims: new PublicationClaim[] { null! }, evidenceLinks: []));
    }

    [Fact]
    public async Task Duplicate_candidate_ids_are_rejected()
    {
        var run = Run(); var source = Source(run); var id = new ClaimCandidateId(Guid.NewGuid());
        await RejectsBeforePublish(run, Batch(run, source: source, claims: [Claim(run, source, 1, id), Claim(run, source, 2, id)]));
    }

    [Fact]
    public async Task Duplicate_and_noncontiguous_ordinals_are_rejected()
    {
        var run = Run(); var source = Source(run);
        await RejectsBeforePublish(run, Batch(run, source: source, claims: [Claim(run, source, 1), Claim(run, source, 1)]));
        await RejectsBeforePublish(run, Batch(run, source: source, claims: [Claim(run, source, 1), Claim(run, source, 3)]));
    }

    [Fact]
    public void Nonpositive_ordinals_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Claim(Run(), Source(Run()), 0));

    [Fact]
    public async Task Claims_must_share_workflow_and_source()
    {
        var run = Run(); var source = Source(run);
        await RejectsBeforePublish(run, Batch(run, source: source, claims: [Claim(Run(), source, 1)]));
        await RejectsBeforePublish(run, Batch(run, source: source, claims: [Claim(run, Source(run), 1)]));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Claims_require_validation_and_human_approval(bool validated, bool approved)
    {
        var run = Run(); var source = Source(run);
        await RejectsBeforePublish(run, Batch(run, source: source, claims: [Claim(run, source, 1, validated: validated, approved: approved)]));
    }

    [Fact]
    public async Task Every_claim_requires_authoritative_source_evidence()
    {
        var run = Run();
        await RejectsBeforePublish(run, Batch(run, evidenceLinks: []));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Structured_candidate_requires_object_json(string json)
    {
        var run = Run(); var source = Source(run);
        Assert.Throws<ArgumentException>(() => Claim(run, source, 1, json: json));
    }

    [Fact]
    public void Command_identity_and_fingerprint_are_stable_and_payload_sensitive()
    {
        var run = Run(); var batch = Batch(run);
        var first = PublicationCommandFactory.Create(batch);
        var second = PublicationCommandFactory.Create(batch);
        var changed = PublicationCommandFactory.Create(Batch(run, claims: [Claim(run, batch.Source, 1, text: "changed claim")]));
        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal(first.ContentFingerprint, second.ContentFingerprint);
        Assert.NotEqual(first.ContentFingerprint, changed.ContentFingerprint);
    }

    [Fact]
    public async Task Command_claims_are_deterministically_ordered_and_persistence_runs_once()
    {
        var run = Run(); var source = Source(run);
        var claims = new[] { Claim(run, source, 2), Claim(run, source, 1) };
        var persistence = new FakePersistence { Batch = Batch(run, source: source, claims: claims) };
        var result = await Handler(persistence).HandleAsync(run, default);
        Assert.Equal(WorkflowState.Published, result.TargetState);
        Assert.Equal([1, 2], persistence.Command!.Batch.Claims.Select(c => c.Ordinal));
        Assert.Equal(1, persistence.PublishCalls);
    }

    [Theory]
    [InlineData(AtomicPublicationResult.NewlyPublished)]
    [InlineData(AtomicPublicationResult.AlreadyPublishedIdentically)]
    public async Task Successful_atomic_results_publish(AtomicPublicationResult atomicResult)
    {
        var run = Run(); var persistence = new FakePersistence { Batch = Batch(run), Result = atomicResult };
        Assert.Equal(WorkflowState.Published, (await Handler(persistence).HandleAsync(run, default)).TargetState);
    }

    [Theory]
    [InlineData("publication conflict")]
    [InlineData("persistence failure")]
    public async Task Persistence_failures_propagate(string message)
    {
        var run = Run(); var expected = new InvalidOperationException(message);
        var persistence = new FakePersistence { Batch = Batch(run), PublishException = expected };
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(persistence).HandleAsync(run, default)));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var persistence = new FakePersistence { LoadException = new OperationCanceledException(cts.Token) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handler(persistence).HandleAsync(Run(), cts.Token));
    }

    [Fact]
    public async Task Generated_invariant_errors_do_not_expose_sensitive_values()
    {
        var run = Run(); var source = Source(run, "source-secret");
        var claim = Claim(run, source, 1, text: "claim-secret", json: "{\"private\":\"json-secret\"}", validated: false);
        var exception = await Assert.ThrowsAsync<PublicationInvariantException>(() => Handler(new FakePersistence { Batch = Batch(run, source: source, claims: [claim], reviewerIdentity: "reviewer-secret") }).HandleAsync(run, default));
        foreach (var secret in new[] { "claim-secret", "json-secret", "source-secret", "reviewer-secret" }) Assert.DoesNotContain(secret, exception.ToString());
    }

    [Fact]
    public void Batch_and_command_take_immutable_snapshots()
    {
        var run = Run(); var source = Source(run); var claims = new List<PublicationClaim> { Claim(run, source, 1) }; var links = Links(claims, source).ToList();
        var batch = Batch(run, source: source, claims: claims, evidenceLinks: links);
        var command = PublicationCommandFactory.Create(batch);
        claims.Clear(); links.Clear();
        Assert.Single(batch.Claims); Assert.Single(batch.EvidenceLinks); Assert.Single(command.Batch.Claims); Assert.Single(command.Batch.EvidenceLinks);
        Assert.Throws<NotSupportedException>(() => ((IList<PublicationClaim>)batch.Claims).Clear());
    }

    private static async Task RejectsBeforePublish(ClaimedWorkflowRun run, ApprovedPublicationBatch batch)
    {
        var persistence = new FakePersistence { Batch = batch };
        await Assert.ThrowsAsync<PublicationInvariantException>(() => Handler(persistence).HandleAsync(run, default));
        Assert.Equal(0, persistence.PublishCalls);
    }

    private static PublishingWorkflowRunPhaseHandler Handler(FakePersistence persistence) => new(persistence, new FixedTimeProvider(Now));
    private static ClaimedWorkflowRun Run(WorkflowState? state = null) => new(new(Guid.NewGuid()), state ?? WorkflowState.Publishing, 7);
    private static PublicationSource Source(ClaimedWorkflowRun run, string title = "Source title") => new(new(Guid.NewGuid()), run.WorkflowRunId, "source-key", title, "https://example.test/source");
    private static PublicationClaim Claim(ClaimedWorkflowRun run, PublicationSource source, int ordinal, ClaimCandidateId? id = null, string text = "Evidence claim", string json = "{}", bool validated = true, bool approved = true) => new(id ?? new(Guid.NewGuid()), run.WorkflowRunId, source.SourceRecordId, ordinal, text, json, validated, approved);
    private static IEnumerable<PublicationEvidenceLink> Links(IEnumerable<PublicationClaim> claims, PublicationSource source) => claims.Select(c => new PublicationEvidenceLink(c.CandidateId, source.SourceRecordId, "authoritative-source"));
    private static ApprovedPublicationBatch Batch(ClaimedWorkflowRun run, WorkflowRunId? workflowRunId = null, int? version = null, string approvalIdentity = "approval-1", DateTimeOffset? approvedAt = null, string reviewerIdentity = "reviewer-1", PublicationSource? source = null, IEnumerable<PublicationClaim>? claims = null, IEnumerable<PublicationEvidenceLink>? evidenceLinks = null)
    {
        source ??= Source(run); claims ??= [Claim(run, source, 1)]; evidenceLinks ??= Links(claims, source);
        return new(workflowRunId ?? run.WorkflowRunId, version ?? run.Version, WorkflowState.Publishing, approvalIdentity, approvedAt ?? Now.AddMinutes(-1), reviewerIdentity, source, claims, evidenceLinks);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class FakePersistence : IEvidencePublicationPersistence
    {
        public ApprovedPublicationBatch? Batch { get; set; }
        public AtomicPublicationResult Result { get; set; } = AtomicPublicationResult.NewlyPublished;
        public Exception? LoadException { get; set; }
        public Exception? PublishException { get; set; }
        public int LoadCalls { get; private set; }
        public int PublishCalls { get; private set; }
        public AtomicPublicationCommand? Command { get; private set; }
        public Task<ApprovedPublicationBatch?> LoadApprovedPublicationBatchAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken) { LoadCalls++; if (LoadException is not null) throw LoadException; return Task.FromResult(Batch); }
        public Task<AtomicPublicationResult> PublishAtomicallyAsync(AtomicPublicationCommand command, CancellationToken cancellationToken) { PublishCalls++; Command = command; if (PublishException is not null) throw PublishException; return Task.FromResult(Result); }
    }
}
