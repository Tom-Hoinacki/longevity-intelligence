using Longevity.Application.Contracts;
using Longevity.Application.SourceNormalization;
using Longevity.Application.WorkflowIntake;
using Longevity.Domain.Workflow;

namespace Longevity.UnitTests;

public sealed class WorkflowIntakeServiceTests
{
    [Fact]
    public async Task Valid_source_is_normalized_before_persistence()
    {
        var persistence = new FakePersistence { Result = new(new(Guid.NewGuid()), WorkflowState.SourceNormalized, 1, false) };
        var service = new WorkflowIntakeService(new ScientificSourceNormalizer(), persistence);
        var request = Request();
        var result = await service.IntakeAsync(request, default);
        Assert.Equal(persistence.Result, result);
        Assert.Equal("url:https://example.test/study", persistence.Normalized!.SourceIdentityKey);
        Assert.Equal("journal_article", persistence.Normalized.SourceType);
    }

    [Fact]
    public async Task Invalid_url_and_unsupported_source_type_are_rejected_before_persistence()
    {
        var persistence = new FakePersistence();
        var service = new WorkflowIntakeService(new ScientificSourceNormalizer(), persistence);
        await Assert.ThrowsAsync<ArgumentException>(() => service.IntakeAsync(Request() with { Source = Request().Source with { CanonicalUrl = "javascript:alert(1)" } }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.IntakeAsync(Request() with { Source = Request().Source with { SourceType = "web_page" } }, default));
        Assert.Equal(0, persistence.Calls);
    }

    [Fact]
    public async Task Cancellation_propagates_before_persistence()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var persistence = new FakePersistence();
        var service = new WorkflowIntakeService(new ScientificSourceNormalizer(), persistence);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.IntakeAsync(Request(), cancellation.Token));
        Assert.Equal(0, persistence.Calls);
    }

    private static WorkflowIntakeRequest Request() => new(
        "intake-1",
        new SubmittedAuthoritativeSource("journal_article", "Study", "source text", "https://example.test/study"),
        "scientific_source_claim_extraction");

    private sealed class FakePersistence : IWorkflowIntakePersistence
    {
        public WorkflowIntakeResult? Result { get; init; }
        public ScientificSourceNormalizationResult? Normalized { get; private set; }
        public int Calls { get; private set; }
        public Task<WorkflowIntakeResult> CreateOrGetAsync(WorkflowIntakeRequest request, ScientificSourceNormalizationResult normalized, CancellationToken cancellationToken)
        { Calls++; Normalized = normalized; return Task.FromResult(Result!); }
    }
}
