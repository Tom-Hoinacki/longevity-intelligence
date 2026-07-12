using System.Diagnostics;
using Longevity.Domain.Workflow;

namespace Longevity.Application.HumanReview;

public sealed class HumanReviewService(IHumanReviewPersistence persistence, TimeProvider timeProvider) : IHumanReviewService
{
    public Task<PendingHumanReviewBatch?> LoadAsync(
        WorkflowRunId workflowRunId,
        CancellationToken cancellationToken) =>
        persistence.LoadPendingAsync(workflowRunId, cancellationToken);

    public async Task<HumanReviewDecisionResult> DecideAsync(
        HumanReviewDecisionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.WorkflowRunId.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ReviewerIdentity)
            || string.IsNullOrWhiteSpace(request.DecisionId))
            throw new ArgumentException("Review decision identity and reviewer are required.");
        if (request.Decision is not HumanReviewDecision.Approve and not HumanReviewDecision.Reject)
            throw new ArgumentOutOfRangeException(nameof(request), "Review decision must be approve or reject.");
        if (request.Decision == HumanReviewDecision.Reject && string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("A rejection reason is required.", nameof(request));

        var reviewer = request.ReviewerIdentity.Trim();
        var decisionId = request.DecisionId.Trim();
        var reason = request.Decision == HumanReviewDecision.Reject ? request.Reason!.Trim() : null;
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var targetState = request.Decision switch
        {
            HumanReviewDecision.Approve => WorkflowState.Approved,
            HumanReviewDecision.Reject => WorkflowState.Rejected,
            _ => throw new UnreachableException()
        };

        var existing = await persistence.LoadDecisionAsync(decisionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Matches(request.WorkflowRunId, request.Decision, reviewer, reason, note, targetState))
                return existing.ToResult();

            throw new HumanReviewConflictException();
        }

        var batch = await persistence.LoadPendingAsync(request.WorkflowRunId, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            if (await persistence.WorkflowRunExistsAsync(request.WorkflowRunId, cancellationToken).ConfigureAwait(false))
                throw new HumanReviewConflictException();
            throw new HumanReviewNotFoundException();
        }
        if (batch.WorkflowRunId != request.WorkflowRunId)
            throw new HumanReviewDataIntegrityException();

        return await persistence.AppendDecisionAsync(
            new HumanReviewPersistenceRequest(
                batch.WorkflowRunId,
                batch.ExpectedWorkflowVersion,
                decisionId,
                request.Decision,
                reviewer,
                reason,
                note,
                timeProvider.GetUtcNow(),
                targetState),
            cancellationToken).ConfigureAwait(false);
    }
}
