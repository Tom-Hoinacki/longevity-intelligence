namespace Longevity.Infrastructure.ModelProviders.OpenRouter;

public sealed class OpenRouterClaimExtractionException : Exception
{
    public OpenRouterClaimExtractionException(string message) : base(message) { }
    public OpenRouterClaimExtractionException(string message, Exception inner) : base(message, inner) { }
}
