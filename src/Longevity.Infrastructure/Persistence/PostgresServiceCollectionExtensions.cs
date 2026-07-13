using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Longevity.Application.Contracts;
using Longevity.Application.HumanReview;
using Longevity.Application.PrivateProfile;
using Longevity.Infrastructure.PrivateProfile;

namespace Longevity.Infrastructure.Persistence;

public static class PostgresServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(PostgresOptions.SectionName);

        services
            .AddOptions<PostgresOptions>()
            .Bind(section)
            .Validate(options =>
            {
                try
                {
                    options.EnsureValid();
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }, "A PostgreSQL connection string is required when Postgres persistence is enabled.")
            .ValidateOnStart();

        if (section.GetValue<bool>(nameof(PostgresOptions.Enabled)))
        {
            services.AddSingleton<NpgsqlDataSource>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
                options.EnsureValid();
                return new NpgsqlDataSourceBuilder(options.ConnectionString!).Build();
            });
            services.AddSingleton<IWorkflowRunRepository, PostgresWorkflowRunRepository>();
            services.AddSingleton<IClaimExtractionPersistence, PostgresClaimExtractionPersistence>();
            services.AddSingleton<IClaimCandidateValidationPersistence, PostgresClaimCandidateValidationPersistence>();
            services.AddSingleton<IHumanReviewPersistence, PostgresHumanReviewPersistence>();
            services.AddSingleton<IPrivateProfileStore, PostgresPrivateProfileStore>();
        }
        else
        {
            // Never silently switch private-profile requests to an in-memory or demo store.
            services.AddSingleton<IPrivateProfileStore, DisabledPrivateProfileStore>();
        }

        return services;
    }
}
