using Longevity.Application.PrivateProfile;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Longevity.Api.PrivateProfile;

public static class PrivateProfileServiceCollectionExtensions
{
    public static IServiceCollection AddPrivateProfileApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddAuthorization();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
        services.AddScoped<PrivateProfileService>();
        services.AddScoped<IPrivateProfileService>(sp => sp.GetRequiredService<PrivateProfileService>());
        services.AddScoped<IPrivateConsentService>(sp => sp.GetRequiredService<PrivateProfileService>());
        services.AddScoped<IPrivateObservationService>(sp => sp.GetRequiredService<PrivateProfileService>());
        services.AddScoped<IPrivatePreferenceService>(sp => sp.GetRequiredService<PrivateProfileService>());
        services.AddScoped<IPrivateGoalService>(sp => sp.GetRequiredService<PrivateProfileService>());
        return services;
    }
}
