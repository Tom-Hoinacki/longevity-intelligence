namespace Longevity.Api.Diagnostics;

using System.Diagnostics;

public interface IReadinessProbe
{
    string Name { get; }
    Task<ReadinessProbeResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed record ReadinessProbeResult(bool IsHealthy, string Status)
{
    public static ReadinessProbeResult Healthy(string status = "healthy") => new(true, status);
    public static ReadinessProbeResult Unhealthy(string status = "unhealthy") => new(false, status);
}

public sealed record ReadinessComponent(string Name, string Status, bool Healthy, double? ElapsedMilliseconds);

public sealed record ReadinessReport(bool Ready, IReadOnlyList<ReadinessComponent> Components);

public sealed class ReadinessAggregator(IEnumerable<IReadinessProbe> probes)
{
    private readonly IReadinessProbe[] _probes = probes.OrderBy(probe => probe.Name, StringComparer.Ordinal).ToArray();

    public async Task<ReadinessReport> CheckAsync(CancellationToken cancellationToken)
    {
        var components = new List<ReadinessComponent>(_probes.Length);
        foreach (var probe in _probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            ReadinessProbeResult result;
            try
            {
                result = await probe.CheckAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                result = ReadinessProbeResult.Unhealthy("check_failed");
            }

            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            components.Add(new ReadinessComponent(probe.Name, result.Status, result.IsHealthy, elapsed));
        }

        return new ReadinessReport(components.All(component => component.Healthy), components);
    }
}
