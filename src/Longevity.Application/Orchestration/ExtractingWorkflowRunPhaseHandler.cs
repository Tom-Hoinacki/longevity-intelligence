using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;

namespace Longevity.Application.Orchestration;

public sealed class ExtractingWorkflowRunPhaseHandler(
    IClaimExtractionModel model,
    IClaimExtractionPersistence persistence) : IWorkflowRunPhaseHandler
{
    public WorkflowState State => WorkflowState.Extracting;

    public async Task<WorkflowRunPhaseHandlingResult> HandleAsync(
        ClaimedWorkflowRun claimedRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claimedRun);
        if (claimedRun.State != State)
        {
            throw new ArgumentException("The extracting handler received a run in an unsupported state.", nameof(claimedRun));
        }

        var source = await persistence.LoadNormalizedSourceAsync(claimedRun.WorkflowRunId, cancellationToken)
            ?? throw new InvalidOperationException("No normalized source exists for the claimed workflow run.");

        if (source.WorkflowRunId != claimedRun.WorkflowRunId)
        {
            throw new InvalidOperationException("The normalized source does not belong to the claimed workflow run.");
        }

        var extraction = await model.ExtractAsync(source, cancellationToken)
            ?? throw new InvalidOperationException("The extraction model returned no result.");

        var request = new ClaimExtractionPersistenceRequest(claimedRun, source, extraction);
        if (request.Candidates.Count == 0)
        {
            return new WorkflowRunPhaseHandlingResult(WorkflowState.NoCandidateExtracted);
        }

        await persistence.PersistExtractionAsync(request, cancellationToken);
        return new WorkflowRunPhaseHandlingResult(WorkflowState.CandidateExtracted);
    }
}
