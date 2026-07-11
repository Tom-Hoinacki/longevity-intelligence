using Longevity.Application.Orchestration;

namespace Longevity.Api.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLongevityApplication(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddLongevityInfrastructure(this IServiceCollection services)
    {
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
