using System.Text.Json;
using System.Text.Json.Serialization;
using Longevity.Api.Security;
using Longevity.Application.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Longevity.Api.Workflow;

public static class WorkflowIntakeEndpoints
{
    public const string WorkflowType = "scientific_source_claim_extraction";
    public const long MaximumRequestBytes = 1_100_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static IServiceCollection AddWorkflowIntakeApi(this IServiceCollection services, IConfiguration configuration)
    {
        var postgresEnabled = configuration.GetSection("Postgres").GetValue<bool>("Enabled");
        var section = configuration.GetSection(WorkflowIntakeApiOptions.SectionName);
        services.AddOptions<WorkflowIntakeApiOptions>()
            .Bind(section)
            .Validate(options => { try { options.EnsureValid(postgresEnabled); return true; } catch (ArgumentException) { return false; } },
                "Workflow intake requires enabled PostgreSQL persistence and a trusted access secret.")
            .ValidateOnStart();
        return services;
    }

    public static WebApplication MapWorkflowIntakeApi(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<WorkflowIntakeApiOptions>>().Value;
        if (!options.Enabled) return app;
        app.MapPost("/internal/workflow-runs", PostFromHttpAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaximumRequestBytes));
        return app;
    }

    public static async Task<IResult> PostFromHttpAsync(
        HttpRequest request,
        IWorkflowIntakeService service,
        IOptions<WorkflowIntakeApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled || !TrustedBearerAuthorization.IsAuthorized(request, options.Value.AccessSecret))
            return Results.Unauthorized();
        if (request.ContentLength is > MaximumRequestBytes)
            return BadRequest("request_too_large", "Workflow intake request is too large.");

        WorkflowIntakeBody? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<WorkflowIntakeBody>(request.Body, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return BadRequest("invalid_request", "Workflow intake request is invalid.");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.IdempotencyKey) || string.IsNullOrWhiteSpace(body.SourceType) ||
            string.IsNullOrWhiteSpace(body.Title) || body.Title.Length > ScientificSourcePolicy.MaximumTitleLength ||
            string.IsNullOrWhiteSpace(body.RawContent) || body.RawContent.Length > ScientificSourcePolicy.MaximumContentLength)
            return BadRequest("invalid_request", "Workflow intake request is invalid.");

        try
        {
            var result = await service.IntakeAsync(
                new WorkflowIntakeRequest(
                    body.IdempotencyKey,
                    new SubmittedAuthoritativeSource(body.SourceType, body.Title, body.RawContent, body.CanonicalUrl, body.Doi, body.Pmid, body.ClinicalTrialsGovIdentifier),
                    WorkflowType),
                cancellationToken).ConfigureAwait(false);
            return result.AlreadyExisted
                ? Results.Ok(result)
                : Results.Created($"/internal/workflow-runs/{result.WorkflowRunId.Value:N}", result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (WorkflowIntakeConflictException) { return Results.Conflict(new WorkflowIntakeErrorResponse("idempotency_conflict", "The idempotency key conflicts with an existing workflow.")); }
        catch (WorkflowIntakeUnavailableException) { return Results.Json(new WorkflowIntakeErrorResponse("workflow_intake_unavailable", "Workflow intake is temporarily unavailable."), statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (ArgumentException) { return BadRequest("invalid_source", "The submitted source is invalid."); }
        catch { return Results.Json(new WorkflowIntakeErrorResponse("workflow_intake_failure", "Workflow intake failed."), statusCode: StatusCodes.Status500InternalServerError); }
    }

    private static IResult BadRequest(string code, string message) => Results.BadRequest(new WorkflowIntakeErrorResponse(code, message));

    public sealed record WorkflowIntakeBody(
        string? IdempotencyKey,
        string? SourceType,
        string? Title,
        string? RawContent,
        string? CanonicalUrl,
        string? Doi,
        string? Pmid,
        string? ClinicalTrialsGovIdentifier);
}

public sealed record WorkflowIntakeErrorResponse(string Code, string Message);
