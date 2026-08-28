using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Worker;

namespace XUnitTest.Worker;

/// <summary>
/// The self-healing half of the fix: while the gate is unhealthy this must keep re-probing on an
/// interval, and while it is healthy it must leave the renderer alone.
/// </summary>
public sealed class FinancialDocumentRendererHealthMonitorTests
{
    [Fact]
    public async Task Never_probes_while_the_gate_is_already_healthy()
    {
        var renderer = new Mock<IFinancialDocumentPdfRenderer>();
        var health = new Mock<IFinancialDocumentRendererHealth>();
        health.Setup(gate => gate.IsHealthy).Returns(true);

        using var monitor = Monitor(renderer, health, intervalSeconds: 1);
        using var cts = new CancellationTokenSource();

        await monitor.StartAsync(cts.Token);
        await Task.Delay(2500);
        await monitor.StopAsync(cts.Token);

        // A healthy renderer has nothing for this loop to prove. Probing it anyway would be pure
        // cost for a signal nothing downstream needs — see the monitor's own remarks.
        renderer.Verify(
            r => r.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Keeps_reprobing_on_the_configured_interval_while_unhealthy()
    {
        var renderer = new Mock<IFinancialDocumentPdfRenderer>();
        renderer
            .Setup(r => r.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        var health = new Mock<IFinancialDocumentRendererHealth>();
        health.Setup(gate => gate.IsHealthy).Returns(false);

        using var monitor = Monitor(renderer, health, intervalSeconds: 1);
        using var cts = new CancellationTokenSource();

        await monitor.StartAsync(cts.Token);
        await Task.Delay(2500);
        await monitor.StopAsync(cts.Token);

        // At a one-second interval, 2.5 seconds is enough for at least two probes without being
        // exact about how many — the point is that it keeps trying, not a precise count.
        renderer.Verify(
            r => r.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
        health.Verify(
            gate => gate.RecordFailure(null, It.IsAny<string>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task Records_recovery_the_moment_a_probe_succeeds()
    {
        var renderer = new Mock<IFinancialDocumentPdfRenderer>();
        renderer
            .Setup(r => r.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([0x25, 0x50, 0x44, 0x46]);
        var health = new Mock<IFinancialDocumentRendererHealth>();
        health.Setup(gate => gate.IsHealthy).Returns(false);

        using var monitor = Monitor(renderer, health, intervalSeconds: 1);
        using var cts = new CancellationTokenSource();

        await monitor.StartAsync(cts.Token);
        await Task.Delay(1500);
        await monitor.StopAsync(cts.Token);

        health.Verify(gate => gate.RecordSuccess(), Times.AtLeastOnce);
    }

    private static FinancialDocumentRendererHealthMonitor Monitor(
        Mock<IFinancialDocumentPdfRenderer> renderer,
        Mock<IFinancialDocumentRendererHealth> health,
        int intervalSeconds) =>
        new(
            renderer.Object,
            health.Object,
            new OptionsStub(intervalSeconds),
            NullLogger<FinancialDocumentRendererHealthMonitor>.Instance);

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public OptionsStub(int rendererHealthProbeIntervalSeconds) =>
            CurrentValue = new SubscriptionOptions
            {
                RendererHealthProbeIntervalSeconds = rendererHealthProbeIntervalSeconds
            };

        public SubscriptionOptions CurrentValue { get; }

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
