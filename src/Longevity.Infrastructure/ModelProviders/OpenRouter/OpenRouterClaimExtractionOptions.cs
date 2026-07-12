namespace Longevity.Infrastructure.ModelProviders.OpenRouter;

public sealed class OpenRouterClaimExtractionOptions
{
    public const string SectionName = "OpenRouterClaimExtraction";
    public string BaseAddress { get; set; } = "https://openrouter.ai/api/v1/";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = "claim-extraction-v1";
    public string PromptVersion { get; set; } = "claim-extraction-prompt-v1";
    public string? ApplicationTitle { get; set; }
    public string? ApplicationUrl { get; set; }
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
