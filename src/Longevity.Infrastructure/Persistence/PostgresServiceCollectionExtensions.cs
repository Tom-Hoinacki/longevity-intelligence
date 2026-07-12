using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

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
        }

        return services;
    }
}
