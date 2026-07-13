using Longevity.Application.PrivateProfile;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Longevity.Api.PrivateProfile;

public static class PrivateProfileServiceCollectionExtensions
{
    public static IServiceCollection AddPrivateProfileApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddAuthentication(options =>
            {
                options.DefaultScheme ??= PrivateProfileAuthorization.RejectAllScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, RejectAllPrivateProfileAuthenticationHandler>(
                PrivateProfileAuthorization.RejectAllScheme,
                _ => { });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PrivateProfileAuthorization.PolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    HttpCurrentUserContext.TryGetTrustedSubject(context.User, out _));
            });
        });
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
