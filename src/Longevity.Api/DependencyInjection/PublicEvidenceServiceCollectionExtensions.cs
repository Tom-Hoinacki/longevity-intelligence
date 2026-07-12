using Longevity.Application.PublicEvidence;
using Longevity.Infrastructure.PublicEvidence;
using Longevity.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Longevity.Api.DependencyInjection;

public static class PublicEvidenceServiceCollectionExtensions
{
    public static IServiceCollection AddPublicEvidenceApi(this IServiceCollection services, IConfiguration configuration)
    {
        var postgresEnabled = configuration.GetSection(PostgresOptions.SectionName).GetValue<bool>(nameof(PostgresOptions.Enabled));
        services.AddOptions<PublicEvidenceOptions>().Bind(configuration.GetSection(PublicEvidenceOptions.SectionName)).Validate(o => { try { o.EnsureValid(postgresEnabled); return true; } catch (ArgumentException) { return false; } }, "PublicEvidence provider configuration is invalid.").ValidateOnStart();
        if (configuration.GetSection(PublicEvidenceOptions.SectionName).GetValue<PublicEvidenceProvider>(nameof(PublicEvidenceOptions.Provider)) == PublicEvidenceProvider.Demo)
            services.AddSingleton<IPublicEvidenceCatalog, DemoPublicEvidenceCatalog>();
        else
            services.AddSingleton<IPublicEvidenceCatalog, PostgresPublicEvidenceCatalog>();
        return services;
    }
}
