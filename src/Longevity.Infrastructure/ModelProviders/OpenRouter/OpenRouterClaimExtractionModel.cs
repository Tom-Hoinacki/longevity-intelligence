using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Longevity.Application.Contracts;
using Microsoft.Extensions.Options;

namespace Longevity.Infrastructure.ModelProviders.OpenRouter;

public sealed class OpenRouterClaimExtractionModel(HttpClient httpClient, IOptions<OpenRouterClaimExtractionOptions> options)
    : IClaimExtractionModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenRouterClaimExtractionOptions settings = options.Value;

    public async Task<ClaimExtractionResult> ExtractAsync(NormalizedScientificSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        if (!string.IsNullOrWhiteSpace(settings.ApplicationTitle)) request.Headers.Add("X-Title", settings.ApplicationTitle);
        if (!string.IsNullOrWhiteSpace(settings.ApplicationUrl)) request.Headers.Add("HTTP-Referer", settings.ApplicationUrl);
        var userPayload = JsonSerializer.Serialize(new { source.Title, source.SourceIdentityKey, text = source.NormalizedText }, JsonOptions);
        request.Content = JsonContent(new
        {
            model = settings.Model,
            temperature = 0,
            messages = new[] { new { role = "system", content = "Extract source-grounded evidence claims only. Do not give medical advice. Preserve uncertainty and limitations. Return only the requested JSON object." }, new { role = "user", content = userPayload } },
            response_format = new { type = "json_schema", json_schema = new { name = "claim_extraction", strict = true, schema = new { type = "object", properties = new { candidates = new { type = "array", items = new { type = "object", properties = new { claimText = new { type = "string" }, structuredCandidate = StructuredCandidateSchema() }, required = new[] { "claimText", "structuredCandidate" }, additionalProperties = false } } }, required = new[] { "candidates" }, additionalProperties = false } } }
        });
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var requestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null;
        if (!response.IsSuccessStatusCode)
            throw new OpenRouterClaimExtractionException($"OpenRouter request failed with HTTP {(int)response.StatusCode} for model '{settings.Model}'{(requestId is null ? string.Empty : $" (request {requestId})")}.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content)) throw Invalid("Provider content was empty.");
            using var output = JsonDocument.Parse(content);
            var candidates = output.RootElement.GetProperty("candidates").EnumerateArray().Select(ParseCandidate).ToArray();
            var usage = root.TryGetProperty("usage", out var u) ? u : default;
            int? input = UsageInt(usage, "prompt_tokens"); int? outputTokens = UsageInt(usage, "completion_tokens");
            decimal? cost = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("cost", out var c) && c.TryGetDecimal(out var costValue) ? costValue : null;
            stopwatch.Stop();
            return new ClaimExtractionResult(candidates, new ClaimExtractionExecutionMetadata(settings.SchemaVersion, "openrouter", settings.Model, settings.PromptVersion, input, outputTokens, cost, checked((int)stopwatch.ElapsedMilliseconds), requestId));
        }
        catch (OpenRouterClaimExtractionException) { throw; }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        { throw new OpenRouterClaimExtractionException("OpenRouter returned an invalid claim-extraction response.", ex); }
    }

    private static ExtractedClaimCandidate ParseCandidate(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) throw Invalid("A provider candidate was not an object.");
        var claim = item.GetProperty("claimText").GetString();
        var structured = item.GetProperty("structuredCandidate");
        if (structured.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(claim)) throw Invalid("A provider candidate was invalid.");
        return new ExtractedClaimCandidate(claim, structured.GetRawText());
    }
    private static int? UsageInt(JsonElement usage, string name) => usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) && result >= 0 ? result : null;
    private static OpenRouterClaimExtractionException Invalid(string message) => new(message);
    private static StringContent JsonContent(object value) => new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static object StructuredCandidateSchema() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["assetSlug"] = new { type = "string" },
            ["assetName"] = new { type = "string" },
            ["assetType"] = new { type = "string" },
            ["assetSummary"] = new { type = new[] { "string", "null" } },
            ["claimType"] = new { type = new[] { "string", "null" } },
            ["targetSystem"] = new { type = new[] { "string", "null" } },
            ["population"] = new { type = new[] { "string", "null" } },
            ["outcomeMeasured"] = new { type = new[] { "string", "null" } },
            ["evidenceLevel"] = new { type = "string" },
            ["evidenceDirection"] = new { type = "string", @enum = new[] { "supports", "contradicts", "neutral" } },
            ["effectSummary"] = new { type = new[] { "string", "null" } },
            ["limitations"] = new { type = "string" },
            ["relevanceScore"] = new { type = new[] { "number", "null" } },
            ["evidenceScore"] = new { type = new[] { "number", "null" } },
            ["hypeScore"] = new { type = new[] { "number", "null" } },
            ["riskScore"] = new { type = new[] { "number", "null" } },
            ["plainEnglishVerdict"] = new { type = new[] { "string", "null" } }
        },
        required = new[] { "assetSlug", "assetName", "assetType", "assetSummary", "claimType", "targetSystem", "population", "outcomeMeasured", "evidenceLevel", "evidenceDirection", "effectSummary", "limitations", "relevanceScore", "evidenceScore", "hypeScore", "riskScore", "plainEnglishVerdict" },
        additionalProperties = false
    };
}
