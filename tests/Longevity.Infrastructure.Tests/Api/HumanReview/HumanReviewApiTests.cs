using Longevity.Api.DependencyInjection;
using Longevity.Api.Diagnostics;
using Longevity.Api.HumanReview;
using Longevity.Application.HumanReview;
using Longevity.Domain.Workflow;
using Longevity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text;

namespace Longevity.Infrastructure.Tests.Api.HumanReview;

public sealed class HumanReviewApiTests
{
    private const string Secret = "test-only-internal-review-secret-123456789";
    private static readonly DateTimeOffset DecisionAt = new(2026, 7, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Unauthorized_request_is_rejected_before_service_access()
    {
        var service = new FakeService { Batch = Batch() };
        var getResult = await HumanReviewEndpoints.GetAsync(Request(), Guid.NewGuid().ToString(), service, EnabledOptions(), default);
        var postResult = await HumanReviewEndpoints.PostDecisionAsync(
            Request(), Guid.NewGuid().ToString(), new(Guid.NewGuid().ToString(), "approve", "reviewer"), service, EnabledOptions(), default);
        Assert.Equal(StatusCodes.Status401Unauthorized, Status(getResult));
        Assert.Equal(StatusCodes.Status401Unauthorized, Status(postResult));
        Assert.Equal(0, service.LoadCalls);
        Assert.Null(service.DecisionRequest);
    }

    [Fact]
    public async Task Authorized_get_returns_safe_pending_batch_response()
    {
        var batch = Batch();
        var service = new FakeService { Batch = batch };
        var result = await HumanReviewEndpoints.GetAsync(Request(true), batch.WorkflowRunId.Value.ToString(), service, EnabledOptions(), default);

        Assert.Equal(StatusCodes.Status200OK, Status(result));
        var response = Value<PendingHumanReviewResponse>(result);
        Assert.Equal(batch.WorkflowRunId.Value, response.WorkflowRunId);
        Assert.Equal(batch.ExpectedWorkflowVersion, response.ExpectedWorkflowVersion);
        Assert.Equal("awaiting_human_approval", response.State);
        var candidate = Assert.Single(response.Candidates);
        Assert.Equal(1, candidate.CandidateOrdinal);
        Assert.Equal("claim text", candidate.ClaimText);
        Assert.True(candidate.StructuredCandidate.GetProperty("private").GetBoolean());
        Assert.True(candidate.DeterministicValidationResult.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public async Task Authorized_approval_succeeds_and_normalizes_decision_identity()
    {
        var runId = Guid.NewGuid();
        var decisionId = Guid.NewGuid();
        var service = new FakeService
        {
            Result = new(new WorkflowRunId(runId), decisionId.ToString("D"), HumanReviewDecision.Approve, WorkflowState.Approved, DecisionAt)
        };

        var result = await HumanReviewEndpoints.PostDecisionAsync(
            Request(true), runId.ToString(), new(decisionId.ToString("B"), "approve", "trusted-reviewer"), service, EnabledOptions(), default);

        Assert.Equal(StatusCodes.Status200OK, Status(result));
        Assert.Equal(HumanReviewDecision.Approve, service.DecisionRequest!.Decision);
        Assert.Equal(decisionId.ToString("D"), service.DecisionRequest.DecisionId);
        Assert.Equal("approved", Value<HumanReviewDecisionResponse>(result).TargetState);
    }

    [Fact]
    public async Task Authorized_rejection_succeeds_with_reason()
    {
        var runId = Guid.NewGuid();
        var decisionId = Guid.NewGuid();
        var service = new FakeService
        {
            Result = new(new WorkflowRunId(runId), decisionId.ToString("D"), HumanReviewDecision.Reject, WorkflowState.Rejected, DecisionAt)
        };

        var result = await HumanReviewEndpoints.PostDecisionAsync(
            Request(true), runId.ToString(), new(decisionId.ToString(), "reject", "trusted-reviewer", "insufficient support", "private note"), service, EnabledOptions(), default);

        Assert.Equal(StatusCodes.Status200OK, Status(result));
        Assert.Equal(HumanReviewDecision.Reject, service.DecisionRequest!.Decision);
        Assert.Equal("insufficient support", service.DecisionRequest.Reason);
        Assert.Equal("private note", service.DecisionRequest.Note);
    }

    [Fact]
    public async Task Rejection_without_reason_is_bad_request_before_service_access()
    {
        var service = new FakeService();
        var result = await HumanReviewEndpoints.PostDecisionAsync(
            Request(true), Guid.NewGuid().ToString(), new(Guid.NewGuid().ToString(), "reject", "reviewer"), service, EnabledOptions(), default);

        Assert.Equal(StatusCodes.Status400BadRequest, Status(result));
        Assert.Equal("rejection_reason_required", Value<HumanReviewErrorResponse>(result).Code);
        Assert.Null(service.DecisionRequest);
    }

    [Fact]
    public async Task Malformed_json_body_is_sanitized_bad_request()
    {
        var request = Request(true);
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"decisionId\": private reviewer note"));
        var service = new FakeService();

        var result = await HumanReviewEndpoints.PostDecisionFromHttpAsync(
            request, Guid.NewGuid().ToString(), service, EnabledOptions(), default);

        Assert.Equal(StatusCodes.Status400BadRequest, Status(result));
        var error = Value<HumanReviewErrorResponse>(result);
        Assert.Equal("invalid_request", error.Code);
        Assert.DoesNotContain("private reviewer note", error.ToString(), StringComparison.Ordinal);
        Assert.Null(service.DecisionRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_workflow_identity_is_bad_request(string workflowRunId)
    {
        var result = await HumanReviewEndpoints.GetAsync(Request(true), workflowRunId, new FakeService(), EnabledOptions(), default);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(result));
        Assert.Equal("invalid_workflow_run_id", Value<HumanReviewErrorResponse>(result).Code);
    }

    [Theory]
    [InlineData("not-a-guid", "approve", "invalid_request")]
    [InlineData("00000000-0000-0000-0000-000000000000", "approve", "invalid_request")]
    [InlineData("11111111-1111-1111-1111-111111111111", "revise", "invalid_decision")]
    public async Task Invalid_decision_identity_or_value_is_bad_request(string decisionId, string decision, string expectedCode)
    {
        var result = await HumanReviewEndpoints.PostDecisionAsync(
            Request(true), Guid.NewGuid().ToString(), new(decisionId, decision, "reviewer"), new FakeService(), EnabledOptions(), default);

        Assert.Equal(StatusCodes.Status400BadRequest, Status(result));
        Assert.Equal(expectedCode, Value<HumanReviewErrorResponse>(result).Code);
    }

    [Fact]
    public async Task Missing_pending_work_is_not_found()
    {
        var result = await HumanReviewEndpoints.GetAsync(Request(true), Guid.NewGuid().ToString(), new FakeService(), EnabledOptions(), default);
        Assert.Equal(StatusCodes.Status404NotFound, Status(result));
        Assert.Equal("pending_review_not_found", Value<HumanReviewErrorResponse>(result).Code);
    }

    [Fact]
    public async Task Decision_missing_and_conflict_failures_have_stable_status_codes()
    {
        var body = new HumanReviewDecisionBody(Guid.NewGuid().ToString(), "approve", "reviewer");
        var missing = new FakeService { DecisionException = new HumanReviewNotFoundException() };
        var conflict = new FakeService { DecisionException = new HumanReviewConflictException() };

        var missingResult = await HumanReviewEndpoints.PostDecisionAsync(Request(true), Guid.NewGuid().ToString(), body, missing, EnabledOptions(), default);
        var conflictResult = await HumanReviewEndpoints.PostDecisionAsync(Request(true), Guid.NewGuid().ToString(), body, conflict, EnabledOptions(), default);

        Assert.Equal(StatusCodes.Status404NotFound, Status(missingResult));
        Assert.Equal(StatusCodes.Status409Conflict, Status(conflictResult));
    }

    [Fact]
    public async Task Already_decided_batch_is_conflict()
    {
        var service = new FakeService { DecisionException = new HumanReviewConflictException() };
        var result = await HumanReviewEndpoints.PostDecisionAsync(
            Request(true),
            Guid.NewGuid().ToString(),
            new(Guid.NewGuid().ToString(), "approve", "reviewer"),
            service,
            EnabledOptions(),
            default);

        Assert.Equal(StatusCodes.Status409Conflict, Status(result));
    }

    [Fact]
    public async Task Unexpected_failure_is_sanitized()
    {
        const string sensitive = Secret + ";Host=private;Password=secret;SELECT *;private candidate json;reviewer note";
        var service = new FakeService { LoadException = new InvalidOperationException(sensitive) };
        var result = await HumanReviewEndpoints.GetAsync(Request(true), Guid.NewGuid().ToString(), service, EnabledOptions(), default);

        Assert.Equal(StatusCodes.Status500InternalServerError, Status(result));
        var error = Value<HumanReviewErrorResponse>(result);
        Assert.Equal("human_review_unavailable", error.Code);
        Assert.DoesNotContain(Secret, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("select", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("candidate", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reviewer note", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Request_cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new FakeService { LoadException = new OperationCanceledException(cancellation.Token) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HumanReviewEndpoints.GetAsync(
            Request(true), Guid.NewGuid().ToString(), service, EnabledOptions(), cancellation.Token));

        Assert.Equal(cancellation.Token, service.LoadToken);
    }

    [Fact]
    public void Client_decision_contract_does_not_accept_workflow_or_target_state()
    {
        var properties = typeof(HumanReviewDecisionBody).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain("ExpectedWorkflowVersion", properties);
        Assert.DoesNotContain("TargetState", properties);
        Assert.DoesNotContain("WorkflowState", properties);
    }

    [Fact]
    public void Human_review_is_disabled_by_default()
    {
        var services = new ServiceCollection();
        services.AddHumanReviewApi(Configuration());
        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<IOptions<HumanReviewApiOptions>>().Value.Enabled);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHumanReviewService));
    }

    [Fact]
    public void Enabled_human_review_requires_postgres_and_trusted_secret()
    {
        AssertInvalidConfiguration(("HumanReview:Enabled", "true"), ("HumanReview:AccessSecret", Secret));
        AssertInvalidConfiguration(("HumanReview:Enabled", "true"), ("Postgres:Enabled", "true"));
        AssertInvalidConfiguration(("HumanReview:Enabled", "true"), ("Postgres:Enabled", "true"), ("HumanReview:AccessSecret", "too-short"));
    }

    [Fact]
    public void Valid_enabled_configuration_registers_service_and_postgres_adapter()
    {
        var configuration = Configuration(
            ("HumanReview:Enabled", "true"),
            ("HumanReview:AccessSecret", Secret),
            ("Postgres:Enabled", "true"),
            ("Postgres:ConnectionString", "Host=localhost;Username=test;Password=test;Database=test"));
        var services = new ServiceCollection();
        services.AddPostgresPersistence(configuration);
        services.AddHumanReviewApi(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<IOptions<HumanReviewApiOptions>>().Value.Enabled);
        Assert.IsType<HumanReviewService>(provider.GetRequiredService<IHumanReviewService>());
        Assert.IsType<PostgresHumanReviewPersistence>(provider.GetRequiredService<IHumanReviewPersistence>());
    }

    [Fact]
    public async Task Endpoint_mapping_is_conditional_and_diagnostics_remain_intact()
    {
        var disabledRoutes = await RoutesAsync(Configuration());
        var enabledRoutes = await RoutesAsync(Configuration(
            ("HumanReview:Enabled", "true"),
            ("HumanReview:AccessSecret", Secret),
            ("Postgres:Enabled", "true")));

        Assert.Contains("/health/live", disabledRoutes);
        Assert.DoesNotContain(disabledRoutes, route => route.StartsWith("/internal/human-review", StringComparison.Ordinal));
        Assert.Contains("/health/live", enabledRoutes);
        Assert.Contains("/internal/human-review/{workflowRunId}", enabledRoutes);
        Assert.Contains("/internal/human-review/{workflowRunId}/decisions", enabledRoutes);
    }

    private static async Task<IReadOnlyList<string>> RoutesAsync(IConfiguration configuration)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddLongevityDiagnostics(builder.Configuration);
        builder.Services.AddHumanReviewApi(builder.Configuration);
        await using var app = builder.Build();
        app.MapLongevityDiagnostics();
        app.MapHumanReviewApi();
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToArray();
    }

    private static void AssertInvalidConfiguration(params (string Key, string Value)[] values)
    {
        var services = new ServiceCollection();
        services.AddHumanReviewApi(Configuration(values));
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<HumanReviewApiOptions>>().Value);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))).Build();

    private static HttpRequest Request(bool authorized = false)
    {
        var context = new DefaultHttpContext();
        if (authorized) context.Request.Headers.Authorization = $"Bearer {Secret}";
        return context.Request;
    }

    private static IOptions<HumanReviewApiOptions> EnabledOptions() =>
        Options.Create(new HumanReviewApiOptions { Enabled = true, AccessSecret = Secret });

    private static int Status(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? StatusCodes.Status200OK;

    private static T Value<T>(IResult result) where T : class =>
        Assert.IsType<T>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);

    private static PendingHumanReviewBatch Batch()
    {
        var runId = new WorkflowRunId(Guid.NewGuid());
        return new PendingHumanReviewBatch(
            runId,
            7,
            WorkflowState.AwaitingHumanApproval,
            [new PendingHumanReviewCandidate(
                new ClaimCandidateId(Guid.NewGuid()),
                runId,
                new SourceRecordId(Guid.NewGuid()),
                2,
                1,
                "claim text",
                "{\"private\":true}",
                new DeterministicValidationSnapshot(true, "{\"passed\":true}"))]);
    }

    private sealed class FakeService : IHumanReviewService
    {
        public PendingHumanReviewBatch? Batch { get; init; }
        public HumanReviewDecisionResult? Result { get; init; }
        public Exception? LoadException { get; init; }
        public Exception? DecisionException { get; init; }
        public int LoadCalls { get; private set; }
        public CancellationToken LoadToken { get; private set; }
        public HumanReviewDecisionRequest? DecisionRequest { get; private set; }

        public Task<PendingHumanReviewBatch?> LoadAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken)
        {
            LoadCalls++;
            LoadToken = cancellationToken;
            if (LoadException is not null) throw LoadException;
            return Task.FromResult(Batch);
        }

        public Task<HumanReviewDecisionResult> DecideAsync(HumanReviewDecisionRequest request, CancellationToken cancellationToken)
        {
            DecisionRequest = request;
            if (DecisionException is not null) throw DecisionException;
            return Task.FromResult(Result!);
        }
    }
}
