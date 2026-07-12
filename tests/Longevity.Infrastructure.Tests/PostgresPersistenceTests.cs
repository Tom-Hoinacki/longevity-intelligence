using Longevity.Infrastructure.Persistence;
using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Longevity.Infrastructure.Tests;

public sealed class PostgresPersistenceTests
{
    [Fact]
    public void Claim_policy_maps_only_supported_processing_transitions()
    {
        Assert.Equal("extracting", WorkflowRunClaimPolicy.StateTransitions["source_normalized"]);
        Assert.Equal("validating", WorkflowRunClaimPolicy.StateTransitions["candidate_extracted"]);
        Assert.Equal("publishing", WorkflowRunClaimPolicy.StateTransitions["approved"]);
        Assert.Equal(3, WorkflowRunClaimPolicy.StateTransitions.Count);
    }

    [Fact]
    public void Claim_policy_excludes_waiting_and_terminal_states()
    {
        Assert.DoesNotContain("awaiting_human_approval", WorkflowRunClaimPolicy.StateTransitions.Keys);
        Assert.DoesNotContain("published", WorkflowRunClaimPolicy.StateTransitions.Keys);
        Assert.DoesNotContain("validation_failed", WorkflowRunClaimPolicy.StateTransitions.Keys);
        Assert.DoesNotContain("rejected", WorkflowRunClaimPolicy.StateTransitions.Keys);
        Assert.DoesNotContain("publication_failed", WorkflowRunClaimPolicy.StateTransitions.Keys);
        Assert.DoesNotContain("received", WorkflowRunClaimPolicy.StateTransitions.Keys);
    }

    [Fact]
    public void Claim_sql_uses_deterministic_runnable_ordering_and_locking()
    {
        Assert.Contains("available_at <= now()", WorkflowRunClaimPolicy.ClaimNextRunnableSql);
        Assert.Contains("retry_count < max_retries", WorkflowRunClaimPolicy.ClaimNextRunnableSql);
        Assert.Contains("FOR UPDATE SKIP LOCKED", WorkflowRunClaimPolicy.ClaimNextRunnableSql);
        Assert.Contains("ORDER BY available_at, created_at, id", WorkflowRunClaimPolicy.ClaimNextRunnableSql);
        Assert.Contains("version = run.version + 1", WorkflowRunClaimPolicy.ClaimNextRunnableSql);
        Assert.Contains("RETURNING run.id, run.state", WorkflowRunClaimPolicy.ClaimNextRunnableSql);
    }

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

    [Fact]
    public void Enabled_configuration_registers_workflow_run_repository()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration(
            ("Postgres:Enabled", "true"),
            ("Postgres:ConnectionString", "Host=localhost;Username=longevity;Password=longevity;Database=longevity"));

        services.AddPostgresPersistence(configuration);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowRunRepository)
            && descriptor.ImplementationType == typeof(PostgresWorkflowRunRepository));
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
    {
        var data = values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value));
        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }
}
