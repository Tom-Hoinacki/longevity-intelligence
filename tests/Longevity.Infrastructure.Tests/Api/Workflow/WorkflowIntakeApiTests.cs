using System.Text;
using Longevity.Api.Workflow;
using Longevity.Application.Contracts;
using Longevity.Application.SourceNormalization;
using Longevity.Application.WorkflowIntake;
using Longevity.Domain.Workflow;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Longevity.Infrastructure.Tests.Api.Workflow;

public sealed class WorkflowIntakeApiTests
{
    private const string Secret = "test-only-workflow-intake-secret-123456789";

    [Fact]
    public async Task Disabled_by_default_and_route_is_not_mapped()
    {
        var routes = await RoutesAsync(Configuration());
        Assert.DoesNotContain("/internal/workflow-runs", routes);
    }

    [Fact]
    public async Task Enabled_route_is_mapped_only_with_trusted_configuration()
    {
        var routes = await RoutesAsync(Configuration(("WorkflowIntake:Enabled", "true"), ("WorkflowIntake:AccessSecret", Secret), ("Postgres:Enabled", "true")));
        Assert.Contains("/internal/workflow-runs", routes);
    }

    [Fact]
    public async Task Missing_or_invalid_authentication_is_rejected_before_service_access()
    {
        var service = new FakeService();
        var missing = await WorkflowIntakeEndpoints.PostFromHttpAsync(Request(), service, EnabledOptions(), default);
        var invalid = await WorkflowIntakeEndpoints.PostFromHttpAsync(Request("wrong-secret"), service, EnabledOptions(), default);
        Assert.Equal(StatusCodes.Status401Unauthorized, Status(missing));
        Assert.Equal(StatusCodes.Status401Unauthorized, Status(invalid));
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Valid_intake_returns_created_without_echoing_source_content()
    {
        var runId = new WorkflowRunId(Guid.NewGuid());
        var service = new FakeService { Result = new(runId, WorkflowState.SourceNormalized, 1, false) };
        var result = await WorkflowIntakeEndpoints.PostFromHttpAsync(Request(Secret, ValidBody()), service, EnabledOptions(), default);
        Assert.Equal(StatusCodes.Status201Created, Status(result));
        Assert.NotNull(service.Request);
        Assert.Equal(runId, service.Result!.WorkflowRunId);
        Assert.Equal(WorkflowIntakeEndpoints.WorkflowType, service.Request.WorkflowType);
        Assert.DoesNotContain("source excerpt", Value<WorkflowIntakeResult>(result).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Identical_duplicate_returns_existing_and_conflicting_duplicate_is_409()
    {
        var existing = new FakeService { Result = new(new(Guid.NewGuid()), WorkflowState.SourceNormalized, 1, true) };
        var existingResult = await WorkflowIntakeEndpoints.PostFromHttpAsync(Request(Secret, ValidBody()), existing, EnabledOptions(), default);
        var conflict = new FakeService { Exception = new WorkflowIntakeConflictException("private details") };
        var conflictResult = await WorkflowIntakeEndpoints.PostFromHttpAsync(Request(Secret, ValidBody()), conflict, EnabledOptions(), default);
        Assert.Equal(StatusCodes.Status200OK, Status(existingResult));
        Assert.Equal(StatusCodes.Status409Conflict, Status(conflictResult));
        Assert.Equal("idempotency_conflict", Value<WorkflowIntakeErrorResponse>(conflictResult).Code);
    }

    [Fact]
    public async Task Oversized_and_malformed_requests_are_sanitized_bad_requests()
    {
        var oversized = Request(Secret, ValidBody());
        oversized.ContentLength = WorkflowIntakeEndpoints.MaximumRequestBytes + 1;
        var malformed = Request(Secret, "{\"rawContent\": private source excerpt");
        var service = new FakeService();
        var oversizedResult = await WorkflowIntakeEndpoints.PostFromHttpAsync(oversized, service, EnabledOptions(), default);
        var malformedResult = await WorkflowIntakeEndpoints.PostFromHttpAsync(malformed, service, EnabledOptions(), default);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(oversizedResult));
        Assert.Equal(StatusCodes.Status400BadRequest, Status(malformedResult));
        Assert.DoesNotContain("private source excerpt", Value<WorkflowIntakeErrorResponse>(malformedResult).ToString(), StringComparison.Ordinal);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Unsupported_source_type_invalid_url_and_unknown_fields_are_rejected()
    {
        var persistence = new FakeIntakePersistence();
        var service = new WorkflowIntakeService(new ScientificSourceNormalizer(), persistence);
        var unsupported = await WorkflowIntakeEndpoints.PostFromHttpAsync(Request(Secret, ValidBody().Replace("journal_article", "blog_post", StringComparison.Ordinal)), service, EnabledOptions(), default);
        var invalidUrl = await WorkflowIntakeEndpoints.PostFromHttpAsync(Request(Secret, ValidBody().Replace("https://example.test/study", "javascript:alert(1)", StringComparison.Ordinal)), service, EnabledOptions(), default);
        var unknownField = await WorkflowIntakeEndpoints.PostFromHttpAsync(Request(Secret, ValidBody().Replace("}", ",\"workflowType\":\"bypass\"}", StringComparison.Ordinal)), service, EnabledOptions(), default);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(unsupported));
        Assert.Equal(StatusCodes.Status400BadRequest, Status(invalidUrl));
        Assert.Equal(StatusCodes.Status400BadRequest, Status(unknownField));
        Assert.Equal(0, persistence.Calls);
    }

    [Fact]
    public async Task Unavailable_and_unexpected_dependencies_return_sanitized_responses()
    {
        var unavailable = new FakeService { Exception = new WorkflowIntakeUnavailableException("Host=private;Password=secret") };
        var unexpected = new FakeService { Exception = new InvalidOperationException("SELECT private candidate JSON") };
        var unavailableResult = await WorkflowIntakeEndpoints.PostFromHttpAsync(Request(Secret, ValidBody()), unavailable, EnabledOptions(), default);
        var unexpectedResult = await WorkflowIntakeEndpoints.PostFromHttpAsync(Request(Secret, ValidBody()), unexpected, EnabledOptions(), default);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Status(unavailableResult));
        Assert.Equal(StatusCodes.Status500InternalServerError, Status(unexpectedResult));
        Assert.DoesNotContain("private", Value<WorkflowIntakeErrorResponse>(unavailableResult).ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("select", Value<WorkflowIntakeErrorResponse>(unexpectedResult).ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new FakeService
        {
            CancellationSource = cancellation,
            Exception = new OperationCanceledException(cancellation.Token)
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkflowIntakeEndpoints.PostFromHttpAsync(Request(Secret, ValidBody()), service, EnabledOptions(), cancellation.Token));
        Assert.Equal(cancellation.Token, service.Token);
    }

    [Fact]
    public void Enabled_configuration_requires_postgres_and_secret()
    {
        AssertInvalid(("WorkflowIntake:Enabled", "true"), ("WorkflowIntake:AccessSecret", Secret));
        AssertInvalid(("WorkflowIntake:Enabled", "true"), ("Postgres:Enabled", "true"));
        AssertInvalid(("WorkflowIntake:Enabled", "true"), ("Postgres:Enabled", "true"), ("WorkflowIntake:AccessSecret", "short"));
    }

    private static async Task<IReadOnlyList<string>> RoutesAsync(IConfiguration configuration)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddWorkflowIntakeApi(builder.Configuration);
        builder.Services.AddSingleton<IWorkflowIntakeService, FakeService>();
        await using var app = builder.Build();
        app.MapWorkflowIntakeApi();
        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty).ToArray();
    }

    private static void AssertInvalid(params (string Key, string Value)[] values)
    {
        var services = new ServiceCollection();
        services.AddWorkflowIntakeApi(Configuration(values));
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<WorkflowIntakeApiOptions>>().Value);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))).Build();

    private static HttpRequest Request(string? secret = null, string? body = null)
    {
        var context = new DefaultHttpContext();
        if (secret is not null) context.Request.Headers.Authorization = $"Bearer {secret}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? ValidBody()));
        context.Request.ContentType = "application/json";
        return context.Request;
    }

    private static string ValidBody() => """
        {"idempotencyKey":"intake-1","sourceType":"journal_article","title":"Study title","rawContent":"source excerpt","canonicalUrl":"https://example.test/study"}
        """;
    private static IOptions<WorkflowIntakeApiOptions> EnabledOptions() => Options.Create(new WorkflowIntakeApiOptions { Enabled = true, AccessSecret = Secret });
    private static int Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? StatusCodes.Status200OK;
    private static T Value<T>(IResult result) where T : class => Assert.IsType<T>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);

    private sealed class FakeService : IWorkflowIntakeService
    {
        public WorkflowIntakeResult? Result { get; init; }
        public Exception? Exception { get; init; }
        public CancellationTokenSource? CancellationSource { get; init; }
        public int Calls { get; private set; }
        public WorkflowIntakeRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<WorkflowIntakeResult> IntakeAsync(WorkflowIntakeRequest request, CancellationToken cancellationToken)
        {
            Calls++; Request = request; Token = cancellationToken;
            CancellationSource?.Cancel();
            if (Exception is not null) throw Exception;
            return Task.FromResult(Result!);
        }
    }

    private sealed class FakeIntakePersistence : IWorkflowIntakePersistence
    {
        public int Calls { get; private set; }
        public Task<WorkflowIntakeResult> CreateOrGetAsync(WorkflowIntakeRequest request, ScientificSourceNormalizationResult normalized, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new WorkflowIntakeResult(new(Guid.NewGuid()), WorkflowState.SourceNormalized, 1, false));
        }
    }
}
