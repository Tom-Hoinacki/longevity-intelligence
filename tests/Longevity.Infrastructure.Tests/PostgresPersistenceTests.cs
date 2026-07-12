using Longevity.Infrastructure.Persistence;
using Longevity.Application.Contracts;
using Longevity.Domain.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Longevity.Api.DependencyInjection;
using Longevity.Application.Orchestration;

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
        Assert.Contains("RETURNING run.id, run.state, run.version", WorkflowRunClaimPolicy.ClaimNextRunnableSql);
    }

    [Fact]
    public void Completion_policy_maps_all_supported_transitions()
    {
        Assert.Contains(("extracting", "candidate_extracted"), WorkflowRunClaimPolicy.CompletionTransitions);
        Assert.Contains(("extracting", "no_candidate_extracted"), WorkflowRunClaimPolicy.CompletionTransitions);
        Assert.Contains(("validating", "awaiting_human_approval"), WorkflowRunClaimPolicy.CompletionTransitions);
        Assert.Contains(("validating", "validation_failed"), WorkflowRunClaimPolicy.CompletionTransitions);
        Assert.Contains(("publishing", "published"), WorkflowRunClaimPolicy.CompletionTransitions);
        Assert.Equal(5, WorkflowRunClaimPolicy.CompletionTransitions.Count);
    }

    [Fact]
    public void Completion_policy_rejects_unsupported_transitions()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            WorkflowRunClaimPolicy.GetCompletionTarget(WorkflowState.Received, WorkflowState.Published));

        Assert.Contains("is not supported", exception.Message);
    }

    [Fact]
    public void Failure_policy_maps_all_supported_retry_transitions()
    {
        Assert.Equal("source_normalized", WorkflowRunClaimPolicy.GetFailureRetryTarget(WorkflowState.Extracting));
        Assert.Equal("candidate_extracted", WorkflowRunClaimPolicy.GetFailureRetryTarget(WorkflowState.Validating));
        Assert.Equal("approved", WorkflowRunClaimPolicy.GetFailureRetryTarget(WorkflowState.Publishing));
    }

    [Fact]
    public void Failure_policy_maps_all_supported_terminal_transitions()
    {
        Assert.Equal("no_candidate_extracted", WorkflowRunClaimPolicy.GetFailureTerminalTarget(WorkflowState.Extracting));
        Assert.Equal("validation_failed", WorkflowRunClaimPolicy.GetFailureTerminalTarget(WorkflowState.Validating));
        Assert.Equal("publication_failed", WorkflowRunClaimPolicy.GetFailureTerminalTarget(WorkflowState.Publishing));
    }

    [Fact]
    public void Failure_policy_rejects_unsupported_transitions()
    {
        var retryException = Assert.Throws<ArgumentException>(() =>
            WorkflowRunClaimPolicy.GetFailureRetryTarget(WorkflowState.Received));

        var terminalException = Assert.Throws<ArgumentException>(() =>
            WorkflowRunClaimPolicy.GetFailureTerminalTarget(WorkflowState.Received));

        Assert.Contains("does not have a supported failure retry transition", retryException.Message);
        Assert.Contains("does not have a supported failure terminal transition", terminalException.Message);
    }

    [Fact]
    public void Completion_sql_enforces_expected_identity_state_and_version()
    {
        Assert.Contains("WHERE id = $1", WorkflowRunClaimPolicy.CompleteClaimedPhaseSql);
        Assert.Contains("AND state = $2", WorkflowRunClaimPolicy.CompleteClaimedPhaseSql);
        Assert.Contains("AND version = $3", WorkflowRunClaimPolicy.CompleteClaimedPhaseSql);
        Assert.Contains("SET state = $4", WorkflowRunClaimPolicy.CompleteClaimedPhaseSql);
        Assert.Contains("version = version + 1", WorkflowRunClaimPolicy.CompleteClaimedPhaseSql);
    }

    [Fact]
    public void Completion_sql_is_parameterized_and_updates_completion_timestamp_only_for_publishing()
    {
        Assert.Contains("$1", WorkflowRunClaimPolicy.CompleteClaimedPhaseSql);
        Assert.Contains("$4 IN ('no_candidate_extracted', 'validation_failed', 'published')", WorkflowRunClaimPolicy.CompleteClaimedPhaseSql);
        Assert.Contains("THEN now()", WorkflowRunClaimPolicy.CompleteClaimedPhaseSql);
        Assert.Contains("ELSE completed_at", WorkflowRunClaimPolicy.CompleteClaimedPhaseSql);
        Assert.Contains("RETURNING id, state, version", WorkflowRunClaimPolicy.CompleteClaimedPhaseSql);
    }

    [Fact]
    public void Failure_sql_enforces_expected_identity_state_and_version()
    {
        Assert.Contains("WHERE id = $1", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("AND state = $2", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("AND version = $3", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("SET state = CASE", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("retry_count = CASE", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("version = version + 1", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
    }

    [Fact]
    public void Failure_sql_is_parameterized_and_updates_retry_and_terminal_columns()
    {
        Assert.Contains("$4", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("$5", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("$6", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("$7", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("retry_count + 1 < max_retries", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("last_error_summary", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("completed_at = CASE", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
        Assert.Contains("RETURNING id, state, version, retry_count", WorkflowRunClaimPolicy.FailClaimedPhaseSql);
    }

    [Fact]
    public void Conflict_result_is_clear_and_not_successful()
    {
        var result = WorkflowRunCompletionResult.Conflict(new WorkflowRunId(Guid.NewGuid()));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowRunCompletionStatus.Conflict, result.Status);
        Assert.Null(result.State);
        Assert.Null(result.Version);
    }

    [Fact]
    public void Failure_conflict_result_is_clear_and_not_successful()
    {
        var result = WorkflowRunFailureResult.Conflict(new WorkflowRunId(Guid.NewGuid()));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowRunFailureStatus.Conflict, result.Status);
        Assert.Null(result.State);
        Assert.Null(result.Version);
        Assert.Null(result.RetryCount);
    }

    [Fact]
    public void Failure_result_can_distinguish_retry_scheduled_and_terminal_failure()
    {
        var runId = new WorkflowRunId(Guid.NewGuid());
        var retryScheduled = new WorkflowRunFailureResult(
            WorkflowRunFailureStatus.RetryScheduled,
            runId,
            WorkflowState.SourceNormalized,
            2,
            1);
        var terminalFailure = new WorkflowRunFailureResult(
            WorkflowRunFailureStatus.TerminalFailure,
            runId,
            WorkflowState.ValidationFailed,
            3,
            3);

        Assert.True(retryScheduled.Succeeded);
        Assert.True(terminalFailure.Succeeded);
        Assert.Equal(WorkflowRunFailureStatus.RetryScheduled, retryScheduled.Status);
        Assert.Equal(WorkflowRunFailureStatus.TerminalFailure, terminalFailure.Status);
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

    [Fact]
    public void Extraction_persistence_sql_is_parameterized_and_maps_audit_fields()
    {
        Assert.Contains("WHERE workflow_run_id = $1", ClaimExtractionPersistencePolicy.LoadNormalizedSourceSql);
        Assert.Contains("candidate_version", ClaimExtractionPersistencePolicy.InsertClaimCandidateSql);
        Assert.Contains("candidate_ordinal", ClaimExtractionPersistencePolicy.InsertClaimCandidateSql);
        Assert.Contains("structured_candidate", ClaimExtractionPersistencePolicy.InsertClaimCandidateSql);
        Assert.Contains("$15", ClaimExtractionPersistencePolicy.InsertClaimCandidateSql);
        Assert.DoesNotContain("{", ClaimExtractionPersistencePolicy.InsertClaimCandidateSql);
    }

    [Fact]
    public void Extraction_persistence_registration_follows_postgres_enablement()
    {
        var disabled = new ServiceCollection();
        disabled.AddPostgresPersistence(BuildConfiguration());
        Assert.DoesNotContain(disabled, descriptor => descriptor.ServiceType == typeof(IClaimExtractionPersistence));

        var enabled = new ServiceCollection();
        enabled.AddPostgresPersistence(BuildConfiguration(
            ("Postgres:Enabled", "true"),
            ("Postgres:ConnectionString", "Host=localhost;Username=test;Password=test;Database=test")));
        Assert.Contains(enabled, descriptor => descriptor.ServiceType == typeof(IClaimExtractionPersistence)
            && descriptor.ImplementationType == typeof(PostgresClaimExtractionPersistence));
    }

    [Fact]
    public void Candidate_persistence_policy_declares_explicit_transaction_boundary_in_adapter()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Longevity.Infrastructure", "Persistence", "PostgresClaimExtractionPersistence.cs"));
        Assert.Contains("BeginTransactionAsync", source);
        Assert.Contains("CommitAsync", source);
        Assert.Contains("OpenConnectionAsync", source);
    }

    [Fact]
    public void Validation_persistence_sql_uses_latest_version_ordinal_order_and_exact_scope()
    {
        Assert.Contains("ORDER BY candidate_version DESC", ValidationPersistencePolicy.LoadLatestCandidateBatchSql);
        Assert.Contains("ORDER BY candidate_ordinal", ValidationPersistencePolicy.LoadLatestCandidateBatchSql);
        Assert.Contains("candidate_version = (SELECT candidate_version FROM latest_version)", ValidationPersistencePolicy.LoadLatestCandidateBatchSql);
        Assert.Contains("AND workflow_run_id = $2", ValidationPersistencePolicy.UpdateValidationResultSql);
        Assert.Contains("AND candidate_version = $3", ValidationPersistencePolicy.UpdateValidationResultSql);
        Assert.Contains("AND candidate_ordinal = $4", ValidationPersistencePolicy.UpdateValidationResultSql);
        Assert.Contains("$6", ValidationPersistencePolicy.UpdateValidationResultSql);
    }

    [Fact]
    public void Validation_adapter_declares_transaction_and_expected_row_count_rollback()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Longevity.Infrastructure", "Persistence", "PostgresClaimCandidateValidationPersistence.cs"));
        Assert.Contains("BeginTransactionAsync", source);
        Assert.Contains("affected != 1", source);
        Assert.Contains("RollbackAsync", source);
        Assert.Contains("NpgsqlDbType.Jsonb", source);
    }

    [Fact]
    public void Validation_persistence_registration_follows_postgres_enablement()
    {
        var disabled = new ServiceCollection();
        disabled.AddPostgresPersistence(BuildConfiguration());
        Assert.DoesNotContain(disabled, descriptor => descriptor.ServiceType == typeof(IClaimCandidateValidationPersistence));

        var enabled = new ServiceCollection();
        enabled.AddPostgresPersistence(BuildConfiguration(
            ("Postgres:Enabled", "true"),
            ("Postgres:ConnectionString", "Host=localhost;Username=test;Password=test;Database=test")));
        Assert.Contains(enabled, descriptor => descriptor.ServiceType == typeof(IClaimCandidateValidationPersistence)
            && descriptor.ImplementationType == typeof(PostgresClaimCandidateValidationPersistence));
    }

    [Fact]
    public void Application_registration_adds_exactly_extracting_and_validating_handlers_only()
    {
        var services = new ServiceCollection();

        services.AddLongevityApplication();

        var handlers = services.Where(descriptor => descriptor.ServiceType == typeof(IWorkflowRunPhaseHandler)).ToArray();
        Assert.Equal(2, handlers.Length);
        Assert.Contains(handlers, descriptor => descriptor.ImplementationType == typeof(ExtractingWorkflowRunPhaseHandler));
        Assert.Contains(handlers, descriptor => descriptor.ImplementationType == typeof(ValidatingWorkflowRunPhaseHandler));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IWorkflowRunProcessor));
    }

    [Fact]
    public void Registered_handlers_resolve_when_fake_application_dependencies_are_supplied()
    {
        var services = new ServiceCollection();
        services.AddLongevityApplication();
        services.AddSingleton<IClaimExtractionModel, TestExtractionModel>();
        services.AddSingleton<IClaimExtractionPersistence, TestExtractionPersistence>();
        services.AddSingleton<IClaimCandidateValidator, TestValidator>();
        services.AddSingleton<IClaimCandidateValidationPersistence, TestValidationPersistence>();

        using var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IWorkflowRunPhaseHandler>().ToArray();

        Assert.Equal(2, handlers.Length);
        Assert.Contains(handlers, handler => handler is ExtractingWorkflowRunPhaseHandler);
        Assert.Contains(handlers, handler => handler is ValidatingWorkflowRunPhaseHandler);
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
    {
        var data = values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value));
        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }

    private sealed class TestExtractionModel : IClaimExtractionModel
    {
        public Task<ClaimExtractionResult> ExtractAsync(NormalizedScientificSource source, CancellationToken cancellationToken) =>
            Task.FromResult(new ClaimExtractionResult([], new ClaimExtractionExecutionMetadata("schema", "provider", "model", "prompt")));
    }

    private sealed class TestExtractionPersistence : IClaimExtractionPersistence
    {
        public Task<NormalizedScientificSource?> LoadNormalizedSourceAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken) => Task.FromResult<NormalizedScientificSource?>(null);
        public Task PersistExtractionAsync(ClaimExtractionPersistenceRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestValidator : IClaimCandidateValidator
    {
        public Task<DeterministicValidationResult> ValidateAsync(ClaimCandidateForValidation candidate, CancellationToken cancellationToken) =>
            Task.FromResult(new DeterministicValidationResult(true, "{}"));
    }

    private sealed class TestValidationPersistence : IClaimCandidateValidationPersistence
    {
        public Task<IReadOnlyList<ClaimCandidateForValidation>> LoadLatestCandidateBatchAsync(WorkflowRunId workflowRunId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClaimCandidateForValidation>>([]);
        public Task PersistValidationResultsAsync(WorkflowRunId workflowRunId, IReadOnlyList<ClaimCandidateValidationUpdate> updates, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
