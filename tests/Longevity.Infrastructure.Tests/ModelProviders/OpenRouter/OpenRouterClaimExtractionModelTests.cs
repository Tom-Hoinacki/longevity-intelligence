using System.Net;
using System.Text;
using System.Text.Json;
using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;
using Longevity.Infrastructure.ModelProviders.OpenRouter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Longevity.Infrastructure.Tests.ModelProviders.OpenRouter;

public sealed class OpenRouterClaimExtractionModelTests
{
    [Fact]
    public async Task Sends_expected_request_and_maps_candidates_and_metadata()
    {
        var handler = new RecordingHandler(Response("[{\"claimText\":\"first\",\"structuredCandidate\":{\"claim\":\"first\"}},{\"claimText\":\"second\",\"structuredCandidate\":{\"claim\":\"second\"}}]", 12, 7, 0.0123m));
        var model = Create(handler, out var source);
        var result = await model.ExtractAsync(source, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("api/v1/chat/completions", handler.Request.RequestUri!.PathAndQuery.TrimStart('/'));
        Assert.Equal("Bearer test-key", handler.Request.Headers.Authorization!.ToString());
        Assert.Equal("Test App", handler.Request.Headers.GetValues("X-Title").Single());
        Assert.Equal("https://app.example", handler.Request.Headers.GetValues("HTTP-Referer").Single());
        Assert.Equal("test-model", JsonDocument.Parse(handler.Body!).RootElement.GetProperty("model").GetString());
        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("json_schema", body.GetProperty("response_format").GetProperty("type").GetString());
        var user = body.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.Equal(source.Title, JsonDocument.Parse(user).RootElement.GetProperty("title").GetString());
        Assert.Equal(source.SourceIdentityKey, JsonDocument.Parse(user).RootElement.GetProperty("sourceIdentityKey").GetString());
        Assert.Equal(source.NormalizedText, JsonDocument.Parse(user).RootElement.GetProperty("text").GetString());
        Assert.Equal(new[] { "first", "second" }, result.Candidates.Select(x => x.ClaimText));
        Assert.Equal("{\"claim\":\"first\"}", result.Candidates[0].StructuredCandidateJson);
        Assert.Equal(12, result.Metadata.InputTokenCount);
        Assert.Equal(7, result.Metadata.OutputTokenCount);
        Assert.Equal(0.0123m, result.Metadata.EstimatedCost);
        Assert.True(result.Metadata.LatencyMilliseconds >= 0);
        Assert.Equal("req-safe-1", result.Metadata.TraceIdentifier);
    }

    [Fact]
    public async Task Allows_empty_candidates()
    {
        var model = Create(new RecordingHandler(Response("[]")), out var source);
        var result = await model.ExtractAsync(source, default);
        Assert.Empty(result.Candidates);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"{\\\"candidates\\\":null}\"}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"{\\\"candidates\\\":[null]}\"}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"{\\\"candidates\\\":[{\\\"claimText\\\":\\\"\\\",\\\"structuredCandidate\\\":{}}]}\"}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"{\\\"candidates\\\":[{\\\"claimText\\\":\\\"x\\\",\\\"structuredCandidate\\\":[]}] }\"}}]}")]
    public async Task Rejects_invalid_provider_shapes(string response)
    {
        var model = Create(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) }), out var source);
        var exception = await Assert.ThrowsAsync<OpenRouterClaimExtractionException>(() => model.ExtractAsync(source, default));
        Assert.DoesNotContain(source.NormalizedText, exception.Message);
    }

    [Fact]
    public async Task Sanitizes_http_failures()
    {
        const string secret = "test-key"; const string sourceText = "private source text"; const string providerBody = "provider secret body";
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent(providerBody) });
        var model = Create(handler, out var source);
        var exception = await Assert.ThrowsAsync<OpenRouterClaimExtractionException>(() => model.ExtractAsync(source with { NormalizedText = sourceText }, default));
        Assert.DoesNotContain(secret, exception.Message); Assert.DoesNotContain(sourceText, exception.Message); Assert.DoesNotContain(providerBody, exception.Message);
    }

    [Fact]
    public async Task Propagates_cancellation()
    {
        var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var model = Create(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK), cancellation: true), out var source);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.ExtractAsync(source, cancellation.Token));
    }

    [Fact]
    public void Registration_validates_configuration_and_registers_one_contract()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["OpenRouterClaimExtraction:ApiKey"] = "key", ["OpenRouterClaimExtraction:Model"] = "model", ["OpenRouterClaimExtraction:RequestTimeout"] = "00:00:00" }).Build();
        services.AddOpenRouterClaimExtractionModel(config);
        Assert.Single(services.Where(x => x.ServiceType == typeof(IClaimExtractionModel)));
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OpenRouterClaimExtractionOptions>>().Value);
    }

    private static OpenRouterClaimExtractionModel Create(RecordingHandler handler, out NormalizedScientificSource source)
    {
        var options = Options.Create(new OpenRouterClaimExtractionOptions { ApiKey = "test-key", Model = "test-model", ApplicationTitle = "Test App", ApplicationUrl = "https://app.example" });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.test/api/v1/") };
        source = new NormalizedScientificSource(new SourceRecordId(Guid.NewGuid()), new WorkflowRunId(Guid.NewGuid()), "doi:10.1000/test", "Scientific title", "A normalized text.\nSecond line.");
        return new OpenRouterClaimExtractionModel(client, options);
    }

    private static HttpResponseMessage Response(string candidates, int? input = null, int? output = null, decimal? cost = null)
    {
        var candidateElement = JsonDocument.Parse(candidates).RootElement;
        var content = JsonSerializer.Serialize(new { candidates = candidateElement });
        var envelope = new Dictionary<string, object?> { ["choices"] = new[] { new { message = new { content } } } };
        if (input is not null) envelope["usage"] = new { prompt_tokens = input, completion_tokens = output, cost };
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json") };
        response.Headers.Add("x-request-id", "req-safe-1"); return response;
    }

    private sealed class RecordingHandler(HttpResponseMessage response, bool cancellation = false) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request; Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (cancellation) throw new OperationCanceledException(cancellationToken);
            return response;
        }
    }
}
