using Longevity.Domain.Workflow;

namespace Longevity.Application.HumanReview;

public sealed class HumanReviewService(IHumanReviewPersistence persistence, TimeProvider timeProvider) : IHumanReviewService
{
    public Task<PendingHumanReviewBatch?> LoadAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken) => persistence.LoadPendingAsync(workflowRunId, cancellationToken);

    public async Task<HumanReviewDecisionResult> DecideAsync(HumanReviewDecisionRequest request, CancellationToken cancellationToken)
    {
        if (request.WorkflowRunId.Value == Guid.Empty || string.IsNullOrWhiteSpace(request.ReviewerIdentity) || string.IsNullOrWhiteSpace(request.DecisionId)) throw new ArgumentException("Review decision identity and reviewer are required.");
        if (request.Decision == HumanReviewDecision.Reject && string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("A rejection reason is required.", nameof(request));
        var batch = await persistence.LoadPendingAsync(request.WorkflowRunId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("No pending human review exists.");
        var target = request.Decision == HumanReviewDecision.Approve ? WorkflowState.Approved : WorkflowState.Rejected;
        var at = timeProvider.GetUtcNow();
        return await persistence.AppendDecisionAsync(new HumanReviewPersistenceRequest(batch.WorkflowRunId, batch.ExpectedWorkflowVersion, request.DecisionId.Trim(), request.Decision, request.ReviewerIdentity.Trim(), request.Reason?.Trim(), request.Note?.Trim(), at, target), cancellationToken).ConfigureAwait(false);
    }
}
