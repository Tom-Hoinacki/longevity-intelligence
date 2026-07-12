using Longevity.Infrastructure.Persistence;
using Npgsql;

namespace Longevity.Api.Diagnostics;

public static class DiagnosticsExtensions
{
    public static IServiceCollection AddLongevityDiagnostics(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ReadinessAggregator>();
        if (configuration.GetSection(PostgresOptions.SectionName).GetValue<bool>(nameof(PostgresOptions.Enabled)))
        {
            services.AddSingleton<IReadinessProbe, PostgresReadinessProbe>();
        }

        return services;
    }

    public static WebApplication MapLongevityDiagnostics(this WebApplication app)
    {
        app.MapGet("/", () => Results.Ok(new { service = "longevity-api", status = "running" }));
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/health/ready", async Task<IResult>
            (ReadinessAggregator aggregator, CancellationToken cancellationToken) =>
        {
            var report = await aggregator.CheckAsync(cancellationToken);
            var response = new ReadinessResponse(report.Ready, report.Components);
            return report.Ready
                ? Results.Ok(response)
                : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
        });
        return app;
    }
}

public sealed record ReadinessResponse(bool Ready, IReadOnlyList<ReadinessComponent> Components);

internal sealed class PostgresReadinessProbe(NpgsqlDataSource dataSource) : IReadinessProbe
{
    public string Name => "postgres";

    public async Task<ReadinessProbeResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await using var connection = await dataSource.OpenConnectionAsync(timeout.Token).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("select 1", connection);
            await command.ExecuteScalarAsync(timeout.Token).ConfigureAwait(false);
            return ReadinessProbeResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ReadinessProbeResult.Unhealthy("unavailable");
        }
    }
}
