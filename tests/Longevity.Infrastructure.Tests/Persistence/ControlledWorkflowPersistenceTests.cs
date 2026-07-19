namespace Longevity.Infrastructure.Tests.Persistence;

public sealed class ControlledWorkflowPersistenceTests
{
    [Fact]
    public void Migration_order_is_unique_forward_only_and_separate_from_private_profile()
    {
        var directory = RepositoryPath("supabase", "migrations");
        var names = Directory.GetFiles(directory, "*.sql").Select(Path.GetFileName).Cast<string>().OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        var workflowIndex = Array.IndexOf(names, "20260712210000_workflow_publication_idempotency.sql");
        var profileIndex = Array.IndexOf(names, "20260713000000_private_profile_foundation.sql");
        Assert.True(workflowIndex > Array.IndexOf(names, "20260712200000_human_review_decision_idempotency.sql"));
        Assert.True(profileIndex > workflowIndex);
        var migration = File.ReadAllText(Path.Combine(directory, names[workflowIndex]));
        Assert.DoesNotContain("private_profile", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("market_intelligence", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop ", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Publication_migration_enforces_idempotency_receipt_rls_and_fk_indexes()
    {
        var migration = Read("supabase", "migrations", "20260712210000_workflow_publication_idempotency.sql");
        Assert.Contains("unique (workflow_run_id, workflow_run_version)", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("constraint workflow_publications_idempotency_key_key unique (idempotency_key)", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public_source_id uuid not null references public.sources", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("public_claim_ids uuid[] not null", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enable row level security", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workflow_publications_source_idx", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workflow_publications_run_idx", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("disable row level security", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant update", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Publication_serializes_concurrent_identity_and_persists_receipt_after_graph_writes()
    {
        var policy = Read("src", "Longevity.Infrastructure", "Persistence", "PublicationPersistencePolicy.cs");
        var adapter = Read("src", "Longevity.Infrastructure", "Persistence", "PostgresEvidencePublicationPersistence.cs");
        Assert.Contains("pg_advisory_xact_lock", policy, StringComparison.Ordinal);
        Assert.True(adapter.IndexOf("LockPublicationIdentitySql", StringComparison.Ordinal) < adapter.IndexOf("LoadPublicationSql", StringComparison.Ordinal));
        Assert.True(adapter.IndexOf("InsertEvidenceSql", StringComparison.Ordinal) < adapter.IndexOf("InsertPublicationSql", StringComparison.Ordinal));
        Assert.Contains("publicClaimIds.ToArray()", adapter, StringComparison.Ordinal);
        Assert.Contains("AlreadyPublishedIdentically", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void Publication_prevalidates_then_uses_one_rollback_safe_transaction()
    {
        var adapter = Read("src", "Longevity.Infrastructure", "Persistence", "PostgresEvidencePublicationPersistence.cs");
        Assert.True(adapter.IndexOf("PrepareClaims(command.Batch.Claims)", StringComparison.Ordinal) < adapter.IndexOf("OpenConnectionAsync", StringComparison.Ordinal));
        Assert.Equal(1, Count(adapter, "BeginTransactionAsync"));
        Assert.Equal(2, Count(adapter, "CommitAsync(cancellationToken)"));
        Assert.Contains("RollbackAsync(CancellationToken.None)", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("private_profile", adapter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenRouter", adapter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Publication_uses_deterministic_score_and_never_overwrites_existing_asset()
    {
        var policy = Read("src", "Longevity.Infrastructure", "Persistence", "PublicationPersistencePolicy.cs");
        var adapter = Read("src", "Longevity.Infrastructure", "Persistence", "PostgresEvidencePublicationPersistence.cs");
        Assert.Contains("prepared.Scoring.PublicScore", adapter, StringComparison.Ordinal);
        Assert.Contains("prepared.Scoring.PolicyId", adapter, StringComparison.Ordinal);
        Assert.Contains("NULL, NULL", policy, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (slug) DO NOTHING", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LockAssetIdentitySql", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("DO UPDATE", policy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("structured.EvidenceScore", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void Intake_is_transactional_parameterized_and_private_profile_independent()
    {
        var policy = Read("src", "Longevity.Infrastructure", "Persistence", "WorkflowIntakePersistencePolicy.cs");
        var adapter = Read("src", "Longevity.Infrastructure", "Persistence", "PostgresWorkflowIntakePersistence.cs");
        Assert.Contains("ON CONFLICT (workflow_type, idempotency_key) DO NOTHING", policy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BeginTransactionAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("RollbackAsync(CancellationToken.None)", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("private_profile", policy + adapter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$\"", policy, StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0; var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) { count++; index += value.Length; }
        return count;
    }
    private static string Read(params string[] parts) => File.ReadAllText(RepositoryPath(parts));
    private static string RepositoryPath(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "..", .. parts]);
}
