using Longevity.Application.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Longevity.Infrastructure.ModelProviders.OpenRouter;

public static class OpenRouterClaimExtractionServiceCollectionExtensions
{
    public static IServiceCollection AddOpenRouterClaimExtractionModel(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OpenRouterClaimExtractionOptions>().Bind(configuration.GetSection(OpenRouterClaimExtractionOptions.SectionName)).Validate(o =>
            Uri.TryCreate(o.BaseAddress, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrWhiteSpace(o.ApiKey) && !string.IsNullOrWhiteSpace(o.Model) && o.RequestTimeout > TimeSpan.Zero,
            "OpenRouter claim-extraction configuration is invalid.").ValidateOnStart();
        services.AddHttpClient<OpenRouterClaimExtractionModel>((sp, client) =>
        {
            var o = sp.GetRequiredService<IOptions<OpenRouterClaimExtractionOptions>>().Value;
            client.BaseAddress = new Uri(o.BaseAddress, UriKind.Absolute);
            client.Timeout = o.RequestTimeout;
        });
        services.AddSingleton<IClaimExtractionModel>(sp => sp.GetRequiredService<OpenRouterClaimExtractionModel>());
        return services;
    }
}
