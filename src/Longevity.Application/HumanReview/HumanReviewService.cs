using System.Diagnostics;
using Longevity.Domain.Workflow;

namespace Longevity.Application.HumanReview;

public sealed class HumanReviewService(IHumanReviewPersistence persistence, TimeProvider timeProvider) : IHumanReviewService
{
    public Task<PendingHumanReviewBatch?> LoadAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken) => persistence.LoadPendingAsync(workflowRunId, cancellationToken);

    public async Task<HumanReviewDecisionResult> DecideAsync(HumanReviewDecisionRequest request, CancellationToken cancellationToken)
    {
        if (request.WorkflowRunId.Value == Guid.Empty || string.IsNullOrWhiteSpace(request.ReviewerIdentity) || string.IsNullOrWhiteSpace(request.DecisionId)) throw new ArgumentException("Review decision identity and reviewer are required.");
        if (request.Decision is not HumanReviewDecision.Approve and not HumanReviewDecision.Reject) throw new ArgumentOutOfRangeException(nameof(request), "Review decision must be approve or reject.");
        if (request.Decision == HumanReviewDecision.Reject && string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("A rejection reason is required.", nameof(request));
        var batch = await persistence.LoadPendingAsync(request.WorkflowRunId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("No pending human review exists.");
        if (batch.WorkflowRunId != request.WorkflowRunId) throw new InvalidOperationException("Pending human review does not match the requested workflow.");
        var target = request.Decision switch
        {
            HumanReviewDecision.Approve => WorkflowState.Approved,
            HumanReviewDecision.Reject => WorkflowState.Rejected,
            _ => throw new UnreachableException()
        };
        var reason = request.Decision == HumanReviewDecision.Reject ? request.Reason!.Trim() : null;
        var at = timeProvider.GetUtcNow();
        return await persistence.AppendDecisionAsync(new HumanReviewPersistenceRequest(batch.WorkflowRunId, batch.ExpectedWorkflowVersion, request.DecisionId.Trim(), request.Decision, request.ReviewerIdentity.Trim(), reason, request.Note?.Trim(), at, target), cancellationToken).ConfigureAwait(false);
    }
}
