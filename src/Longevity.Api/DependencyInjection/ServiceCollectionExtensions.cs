using Longevity.Application.Orchestration;
using Longevity.Application.Contracts;
using Longevity.Application.HumanReview;
using Longevity.Application.Publishing;
using Longevity.Application.SourceNormalization;
using Longevity.Application.Validation;
using Longevity.Application.WorkflowIntake;
using Longevity.Application.EvidenceScoring;
using Longevity.Api.HumanReview;
using Longevity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Longevity.Api.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLongevityApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<EvidenceScoringEngine>();
        services.AddSingleton<ISourceNormalizer, ScientificSourceNormalizer>();
        services.AddSingleton<IClaimCandidateValidator, DeterministicClaimCandidateValidator>();
        services.AddSingleton<IWorkflowIntakeService, WorkflowIntakeService>();
        services.AddSingleton<IWorkflowRunPhaseHandler, ExtractingWorkflowRunPhaseHandler>();
        services.AddSingleton<IWorkflowRunPhaseHandler, ValidatingWorkflowRunPhaseHandler>();
        services.AddSingleton<IWorkflowRunPhaseHandler, PublishingWorkflowRunPhaseHandler>();
        return services;
    }

    public static IServiceCollection AddLongevityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPostgresPersistence(configuration);
        return services;
    }

    public static IServiceCollection AddHumanReviewApi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var humanReviewSection = configuration.GetSection(HumanReviewApiOptions.SectionName);
        var postgresEnabled = configuration.GetSection(PostgresOptions.SectionName).GetValue<bool>(nameof(PostgresOptions.Enabled));

        services
            .AddOptions<HumanReviewApiOptions>()
            .Bind(humanReviewSection)
            .Validate(options =>
            {
                try
                {
                    options.EnsureValid(postgresEnabled);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }, "Human review requires enabled PostgreSQL persistence and a trusted access secret of at least 32 characters.")
            .ValidateOnStart();

        if (humanReviewSection.GetValue<bool>(nameof(HumanReviewApiOptions.Enabled)))
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IHumanReviewService, HumanReviewService>();
        }

        return services;
    }

    public static IServiceCollection AddWorkflowOrchestrator(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<WorkflowOrchestratorOptions>()
            .Bind(configuration.GetSection(WorkflowOrchestratorOptions.SectionName))
            .Validate(options =>
            {
                try
                {
                    options.EnsureValid(PostgresEnabled(configuration));
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }, "The workflow orchestrator requires positive polling/retry intervals and PostgreSQL persistence when enabled.")
            .ValidateOnStart();

        services.AddSingleton<IWorkflowRunProcessor>(sp => new WorkflowRunProcessor(
            sp.GetRequiredService<IWorkflowRunRepository>(),
            sp.GetServices<IWorkflowRunPhaseHandler>(),
            sp.GetRequiredService<TimeProvider>(),
            TimeSpan.FromSeconds(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkflowOrchestratorOptions>>().Value.RetryDelaySeconds)));

        return services;
    }

    private static bool PostgresEnabled(IConfiguration configuration) => configuration.GetSection(PostgresOptions.SectionName).GetValue<bool>(nameof(PostgresOptions.Enabled));
}
