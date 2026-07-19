using Longevity.Api.Security;
using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;
using Microsoft.Extensions.Options;

namespace Longevity.Api.Workflow;

public static class WorkflowIntakeEndpoints
{
    public static IServiceCollection AddWorkflowIntakeApi(this IServiceCollection services, IConfiguration configuration)
    {
        var postgresEnabled = configuration.GetSection("Postgres").GetValue<bool>("Enabled");
        services.AddOptions<WorkflowIntakeApiOptions>().Bind(configuration.GetSection(WorkflowIntakeApiOptions.SectionName)).Validate(o => { try { o.EnsureValid(postgresEnabled); return true; } catch (ArgumentException) { return false; } }, "Workflow intake requires enabled PostgreSQL persistence and a trusted access secret.").ValidateOnStart();
        return services;
    }

    public static IEndpointRouteBuilder MapWorkflowIntakeApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/internal/workflow-runs");
        group.MapPost("", async (WorkflowIntakeBody body, HttpRequest request, IOptions<WorkflowIntakeApiOptions> options, IWorkflowIntakeService service, CancellationToken cancellationToken) =>
        {
            if (!options.Value.Enabled) return Results.NotFound();
            if (!TrustedBearerAuthorization.IsAuthorized(request, options.Value.AccessSecret)) return Results.Unauthorized();
            if (body is null || string.IsNullOrWhiteSpace(body.IdempotencyKey) || string.IsNullOrWhiteSpace(body.Title) || string.IsNullOrWhiteSpace(body.RawContent) || body.RawContent.Length > 1_000_000) return Results.BadRequest(new { error = "invalid_request" });
            try
            {
                var result = await service.IntakeAsync(new WorkflowIntakeRequest(body.IdempotencyKey, new SubmittedAuthoritativeSource(body.SourceType ?? "scientific", body.Title, body.RawContent, body.CanonicalUrl, body.Doi, body.Pmid, body.ClinicalTrialsGovIdentifier), body.WorkflowType ?? "scientific_source_claim_extraction"), cancellationToken);
                return result.AlreadyExisted ? Results.Ok(result) : Results.Created($"/internal/workflow-runs/{result.WorkflowRunId.Value:N}", result);
            }
            catch (WorkflowIntakeConflictException) { return Results.Conflict(new { error = "idempotency_conflict" }); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "invalid_source" }); }
        });
        return endpoints;
    }

    public sealed record WorkflowIntakeBody(string? IdempotencyKey, string? SourceType, string? Title, string? RawContent, string? CanonicalUrl, string? Doi, string? Pmid, string? ClinicalTrialsGovIdentifier, string? WorkflowType);
}
