using System.Text.Json;
using Longevity.Api.Security;
using Longevity.Application.HumanReview;
using Longevity.Domain.Workflow;
using Microsoft.Extensions.Options;

namespace Longevity.Api.HumanReview;

public static class HumanReviewEndpoints
{
    public static WebApplication MapHumanReviewApi(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<HumanReviewApiOptions>>().Value;
        if (!options.Enabled) return app;

        var group = app.MapGroup("/internal/human-review");
        group.MapGet("/{workflowRunId}", GetAsync);
        group.MapPost("/{workflowRunId}/decisions", PostDecisionFromHttpAsync);
        return app;
    }

    public static async Task<IResult> PostDecisionFromHttpAsync(
        HttpRequest request,
        string workflowRunId,
        IHumanReviewService service,
        IOptions<HumanReviewApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(request, options.Value)) return Results.Unauthorized();

        HumanReviewDecisionBody? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<HumanReviewDecisionBody>(
                request.Body,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return BadRequest("invalid_request", "Human-review request is invalid.");
        }
        catch (NotSupportedException)
        {
            return BadRequest("invalid_request", "Human-review request is invalid.");
        }

        return await PostDecisionAsync(request, workflowRunId, body, service, options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IResult> GetAsync(
        HttpRequest request,
        string workflowRunId,
        IHumanReviewService service,
        IOptions<HumanReviewApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(request, options.Value)) return Results.Unauthorized();
        if (!TryParseIdentity(workflowRunId, out var runId)) return BadRequest("invalid_workflow_run_id", "Workflow-run identity is invalid.");

        try
        {
            var batch = await service.LoadAsync(new WorkflowRunId(runId), cancellationToken).ConfigureAwait(false);
            if (batch is null) return NotFound();

            return Results.Ok(new PendingHumanReviewResponse(
                batch.WorkflowRunId.Value,
                batch.ExpectedWorkflowVersion,
                batch.State.DatabaseValue,
                batch.Candidates.Select(candidate => new PendingHumanReviewCandidateResponse(
                    candidate.CandidateId.Value,
                    candidate.CandidateVersion,
                    candidate.CandidateOrdinal,
                    candidate.ClaimText,
                    JsonSerializer.Deserialize<JsonElement>(candidate.StructuredCandidateJson),
                    JsonSerializer.Deserialize<JsonElement>(candidate.Validation.ValidationResultJson))).ToArray()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HumanReviewConflictException)
        {
            return Conflict();
        }
        catch (ArgumentException)
        {
            return BadRequest("invalid_request", "Human-review request is invalid.");
        }
        catch
        {
            return UnexpectedFailure();
        }
    }

    public static async Task<IResult> PostDecisionAsync(
        HttpRequest request,
        string workflowRunId,
        HumanReviewDecisionBody? body,
        IHumanReviewService service,
        IOptions<HumanReviewApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(request, options.Value)) return Results.Unauthorized();
        if (!TryParseIdentity(workflowRunId, out var runId)) return BadRequest("invalid_workflow_run_id", "Workflow-run identity is invalid.");
        if (body is null || !TryParseIdentity(body.DecisionId, out var decisionId) || string.IsNullOrWhiteSpace(body.ReviewerIdentity))
            return BadRequest("invalid_request", "Human-review request is invalid.");
        if (!TryParseDecision(body.Decision, out var decision))
            return BadRequest("invalid_decision", "Decision must be approve or reject.");
        if (decision == HumanReviewDecision.Reject && string.IsNullOrWhiteSpace(body.RejectionReason))
            return BadRequest("rejection_reason_required", "A rejection reason is required.");

        try
        {
            var result = await service.DecideAsync(
                new HumanReviewDecisionRequest(
                    new WorkflowRunId(runId),
                    decision,
                    body.ReviewerIdentity,
                    decisionId.ToString("D"),
                    body.RejectionReason,
                    body.ReviewerNote),
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(new HumanReviewDecisionResponse(
                result.WorkflowRunId.Value,
                result.DecisionId,
                result.Decision == HumanReviewDecision.Approve ? "approve" : "reject",
                result.TargetState.DatabaseValue,
                result.DecisionAt));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HumanReviewNotFoundException)
        {
            return NotFound();
        }
        catch (HumanReviewConflictException)
        {
            return Conflict();
        }
        catch (ArgumentException)
        {
            return BadRequest("invalid_request", "Human-review request is invalid.");
        }
        catch
        {
            return UnexpectedFailure();
        }
    }

    private static bool IsAuthorized(HttpRequest request, HumanReviewApiOptions options)
        => options.Enabled && TrustedBearerAuthorization.IsAuthorized(request, options.AccessSecret);

    private static bool TryParseIdentity(string? value, out Guid identity) =>
        Guid.TryParse(value, out identity) && identity != Guid.Empty;

    private static bool TryParseDecision(string? value, out HumanReviewDecision decision)
    {
        if (string.Equals(value, "approve", StringComparison.OrdinalIgnoreCase))
        {
            decision = HumanReviewDecision.Approve;
            return true;
        }
        if (string.Equals(value, "reject", StringComparison.OrdinalIgnoreCase))
        {
            decision = HumanReviewDecision.Reject;
            return true;
        }

        decision = default;
        return false;
    }

    private static IResult BadRequest(string code, string message) =>
        Results.BadRequest(new HumanReviewErrorResponse(code, message));

    private static IResult NotFound() =>
        Results.NotFound(new HumanReviewErrorResponse("pending_review_not_found", "No pending human review exists."));

    private static IResult Conflict() =>
        Results.Conflict(new HumanReviewErrorResponse("human_review_conflict", "The review decision conflicts with current state."));

    private static IResult UnexpectedFailure() =>
        Results.Json(
            new HumanReviewErrorResponse("human_review_unavailable", "Human review is temporarily unavailable."),
            statusCode: StatusCodes.Status500InternalServerError);
}

public sealed record HumanReviewDecisionBody(
    string? DecisionId,
    string? Decision,
    string? ReviewerIdentity,
    string? RejectionReason = null,
    string? ReviewerNote = null);

public sealed record PendingHumanReviewCandidateResponse(
    Guid CandidateId,
    int CandidateVersion,
    int CandidateOrdinal,
    string ClaimText,
    JsonElement StructuredCandidate,
    JsonElement DeterministicValidationResult);

public sealed record PendingHumanReviewResponse(
    Guid WorkflowRunId,
    int ExpectedWorkflowVersion,
    string State,
    IReadOnlyList<PendingHumanReviewCandidateResponse> Candidates);

public sealed record HumanReviewDecisionResponse(
    Guid WorkflowRunId,
    string DecisionId,
    string Decision,
    string TargetState,
    DateTimeOffset DecisionAt);

public sealed record HumanReviewErrorResponse(string Code, string Message);
