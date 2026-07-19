using System.Diagnostics;

namespace Longevity.Api.PrivateProfile;

public sealed class PrivateProfileObservabilityMiddleware(
    RequestDelegate next,
    ILogger<PrivateProfileObservabilityMiddleware> logger)
{
    private static readonly PathString PrivateProfilePrefix = new("/api/v1/me");

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(PrivateProfilePrefix, StringComparison.Ordinal))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            if (context.Request.ContentLength > PrivateProfileAuthorization.MaximumRequestBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            await next(context).ConfigureAwait(false);
        }
        finally
        {
            logger.LogInformation(
                "Private-profile request completed. Method {Method}; status {StatusCode}; elapsed {ElapsedMilliseconds} ms.",
                context.Request.Method,
                context.Response.StatusCode,
                Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1));
        }
    }
}

public static class PrivateProfileObservabilityExtensions
{
    public static WebApplication UsePrivateProfileSafeObservability(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<PrivateProfileObservabilityMiddleware>();
        return app;
    }
}
