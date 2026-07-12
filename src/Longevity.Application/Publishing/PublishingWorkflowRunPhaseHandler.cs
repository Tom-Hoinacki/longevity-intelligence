using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;

namespace Longevity.Application.Publishing;

public sealed class PublishingWorkflowRunPhaseHandler : IWorkflowRunPhaseHandler
{
    private readonly IEvidencePublicationPersistence persistence;
    private readonly TimeProvider timeProvider;

    public PublishingWorkflowRunPhaseHandler(IEvidencePublicationPersistence persistence, TimeProvider? timeProvider = null)
    {
        this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public WorkflowState State => WorkflowState.Publishing;
    public async Task<WorkflowRunPhaseHandlingResult> HandleAsync(ClaimedWorkflowRun claimedRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimedRun);
        if (claimedRun.State != State) throw new PublicationInvariantException("The publishing handler received an unsupported state.");
        var batch = await persistence.LoadApprovedPublicationBatchAsync(claimedRun.WorkflowRunId, cancellationToken) ?? throw new PublicationInvariantException("No approved publication batch exists.");
        Validate(claimedRun, batch, timeProvider.GetUtcNow());
        var publicationResult = await persistence.PublishAtomicallyAsync(PublicationCommandFactory.Create(batch), cancellationToken);
        if (publicationResult is not AtomicPublicationResult.NewlyPublished and not AtomicPublicationResult.AlreadyPublishedIdentically)
        {
            throw new InvalidOperationException("The publication persistence returned an unsupported result.");
        }

        return new WorkflowRunPhaseHandlingResult(WorkflowState.Published);
    }
    private static void Validate(ClaimedWorkflowRun run, ApprovedPublicationBatch batch, DateTimeOffset now)
    {
        if (batch.WorkflowRunId != run.WorkflowRunId || batch.WorkflowRunVersion != run.Version || batch.WorkflowState != WorkflowState.Publishing) throw new PublicationInvariantException("Publication batch identity or state is invalid.");
        if (batch.Source.WorkflowRunId != run.WorkflowRunId || batch.ApprovedAt == default || batch.ApprovedAt > now || batch.Claims.Count == 0) throw new PublicationInvariantException("Publication batch approval or source is invalid.");
        if (batch.Claims.Any(c => c is null || c.WorkflowRunId != run.WorkflowRunId || c.SourceRecordId != batch.Source.SourceRecordId || !c.ValidationPassed || !c.HumanApproved)) throw new PublicationInvariantException("Publication claim invariants are not satisfied.");
        if (batch.Claims.Select(c => c.CandidateId).Distinct().Count() != batch.Claims.Count || batch.Claims.Select(c => c.Ordinal).Distinct().Count() != batch.Claims.Count || !batch.Claims.Select(c => c.Ordinal).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, batch.Claims.Count))) throw new PublicationInvariantException("Publication claim ordering is invalid.");
        if (batch.EvidenceLinks.Any(link => link is null)) throw new PublicationInvariantException("Publication evidence links must be non-null.");
        var candidateIds = batch.Claims.Select(claim => claim.CandidateId).ToHashSet();
        if (batch.EvidenceLinks.Any(link => !candidateIds.Contains(link.CandidateId))) throw new PublicationInvariantException("Publication evidence link references a candidate outside the approved batch.");
        if (batch.EvidenceLinks.Any(link => link.SourceRecordId != batch.Source.SourceRecordId)) throw new PublicationInvariantException("Publication evidence link references a non-authoritative source.");
        if (batch.EvidenceLinks.Select(link => (link.CandidateId, link.SourceRecordId, link.EvidenceType)).Distinct().Count() != batch.EvidenceLinks.Count) throw new PublicationInvariantException("Publication evidence links contain a duplicate candidate, source, and evidence-type tuple.");
        if (batch.Claims.Any(claim => !batch.EvidenceLinks.Any(link => link.CandidateId == claim.CandidateId))) throw new PublicationInvariantException("Publication provenance is incomplete.");
    }
}
