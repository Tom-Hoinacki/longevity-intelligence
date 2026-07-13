using Longevity.Application.PublicEvidence;
using Longevity.Infrastructure.Persistence;
using Longevity.Infrastructure.PublicEvidence;

namespace Longevity.Api.DependencyInjection;

public static class MarketIntelligenceServiceCollectionExtensions
{
    public static IServiceCollection AddMarketIntelligenceApi(this IServiceCollection services, IConfiguration configuration)
    {
        var postgresEnabled = configuration.GetSection(PostgresOptions.SectionName).GetValue<bool>(nameof(PostgresOptions.Enabled));
        services.AddOptions<MarketIntelligenceOptions>().Bind(configuration.GetSection(MarketIntelligenceOptions.SectionName)).Validate(o => { try { o.EnsureValid(postgresEnabled); return true; } catch (ArgumentException) { return false; } }, "MarketIntelligence provider configuration is invalid.").ValidateOnStart();
        if (configuration.GetSection(MarketIntelligenceOptions.SectionName).GetValue<MarketIntelligenceProvider>(nameof(MarketIntelligenceOptions.Provider)) == MarketIntelligenceProvider.Postgres)
            services.AddSingleton<IMarketIntelligenceCatalog, PostgresMarketIntelligenceCatalog>();
        else
            services.AddSingleton<IMarketIntelligenceCatalog, DemoMarketIntelligenceCatalog>();
        return services;
    }
}
