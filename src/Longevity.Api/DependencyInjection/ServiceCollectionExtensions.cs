using Longevity.Application.Orchestration;
using Longevity.Application.Contracts;
using Longevity.Infrastructure.Persistence;

namespace Longevity.Api.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLongevityApplication(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowRunPhaseHandler, ExtractingWorkflowRunPhaseHandler>();
        services.AddSingleton<IWorkflowRunPhaseHandler, ValidatingWorkflowRunPhaseHandler>();
        return services;
    }

    public static IServiceCollection AddLongevityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPostgresPersistence(configuration);
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
                    options.EnsureValid();
                    return true;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
            }, "PollingIntervalSeconds must be greater than zero.")
            .ValidateOnStart();

        return services;
    }
}
