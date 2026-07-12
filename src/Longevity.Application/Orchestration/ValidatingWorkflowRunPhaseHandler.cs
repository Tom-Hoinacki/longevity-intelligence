using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;

namespace Longevity.Application.Orchestration;

public sealed class ValidatingWorkflowRunPhaseHandler(
    IClaimCandidateValidator validator,
    IClaimCandidateValidationPersistence persistence) : IWorkflowRunPhaseHandler
{
    public WorkflowState State => WorkflowState.Validating;

    public async Task<WorkflowRunPhaseHandlingResult> HandleAsync(
        ClaimedWorkflowRun claimedRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimedRun);
        if (claimedRun.State != State)
        {
            throw new ArgumentException("The validating handler received a run in an unsupported state.", nameof(claimedRun));
        }

        var candidates = await persistence.LoadLatestCandidateBatchAsync(claimedRun.WorkflowRunId, cancellationToken);
        if (candidates is null || candidates.Count == 0)
        {
            throw new InvalidOperationException("No complete candidate batch exists for the claimed workflow run.");
        }

        var ordered = candidates
            .OrderBy(candidate => candidate.CandidateVersion)
            .ThenBy(candidate => candidate.CandidateOrdinal)
            .ToArray();
        var updates = new List<ClaimCandidateValidationUpdate>(ordered.Length);
        foreach (var candidate in ordered)
        {
            if (candidate.WorkflowRunId != claimedRun.WorkflowRunId)
            {
                throw new InvalidOperationException("A candidate does not belong to the claimed workflow run.");
            }

            var result = await validator.ValidateAsync(candidate, cancellationToken);
            updates.Add(new ClaimCandidateValidationUpdate(candidate, result));
        }

        await persistence.PersistValidationResultsAsync(claimedRun.WorkflowRunId, updates, cancellationToken);
        return new WorkflowRunPhaseHandlingResult(
            updates.All(update => update.Result.Passed)
                ? WorkflowState.AwaitingHumanApproval
                : WorkflowState.ValidationFailed);
    }
}
