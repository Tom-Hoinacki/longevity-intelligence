using Longevity.Api.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Longevity.Infrastructure.Tests.Api.Diagnostics;

public sealed class DiagnosticsTests
{
    [Fact]
    public async Task Aggregator_runs_probes_once_in_deterministic_order_and_sanitizes_failures()
    {
        var calls = new List<string>();
        var services = new IReadinessProbe[]
        {
            new FakeProbe("zeta", () => { calls.Add("zeta"); throw new InvalidOperationException("secret connection string"); }),
            new FakeProbe("alpha", () => { calls.Add("alpha"); return ReadinessProbeResult.Healthy(); })
        };

        var report = await new ReadinessAggregator(services).CheckAsync(CancellationToken.None);

        Assert.False(report.Ready);
        Assert.Equal(new[] { "alpha", "zeta" }, report.Components.Select(component => component.Name));
        Assert.Equal("check_failed", report.Components[1].Status);
        Assert.Equal(new[] { "alpha", "zeta" }, calls);
        Assert.DoesNotContain(report.Components, component => component.Status.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Aggregator_with_no_probes_is_ready()
    {
        var report = await new ReadinessAggregator(Array.Empty<IReadinessProbe>()).CheckAsync(CancellationToken.None);
        Assert.True(report.Ready);
        Assert.Empty(report.Components);
    }

    [Fact]
    public async Task Aggregator_propagates_request_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ReadinessAggregator(new[] { new FakeProbe("cancelled", () => ReadinessProbeResult.Healthy()) })
                .CheckAsync(cancellation.Token));
    }

    [Fact]
    public void Disabled_postgres_does_not_register_a_probe()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Postgres:Enabled"] = "false"
        }).Build();
        var services = new ServiceCollection();
        services.AddLongevityDiagnostics(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Empty(provider.GetServices<IReadinessProbe>());
    }

    private sealed class FakeProbe(string name, Func<ReadinessProbeResult> check) : IReadinessProbe
    {
        public string Name => name;
        public Task<ReadinessProbeResult> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(check());
    }
}
