using Longevity.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Longevity.Infrastructure.Tests;

public sealed class PostgresPersistenceTests
{
    [Fact]
    public void Persistence_is_disabled_by_default()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();

        services.AddPostgresPersistence(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<NpgsqlDataSource>());
    }

    [Fact]
    public void Disabled_persistence_needs_no_connection_string()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(("Postgres:Enabled", "false"));

        services.AddPostgresPersistence(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<NpgsqlDataSource>());
    }

    [Fact]
    public void Enabled_persistence_rejects_a_missing_connection_string()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(("Postgres:Enabled", "true"));

        services.AddPostgresPersistence(configuration);

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<NpgsqlDataSource>());
        Assert.Contains("connection string", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enabled_valid_configuration_registers_npgsql_data_source()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(
            ("Postgres:Enabled", "true"),
            ("Postgres:ConnectionString", "Host=localhost;Username=longevity;Password=longevity;Database=longevity"));

        services.AddPostgresPersistence(configuration);

        using var provider = services.BuildServiceProvider();

        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        Assert.NotNull(dataSource);
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
    {
        var data = values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value));
        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }
}
