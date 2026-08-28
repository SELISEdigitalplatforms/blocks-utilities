using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Services;
using Worker;

namespace XUnitTest.Worker;

/// <summary>
/// The startup probe no longer stops the host — see the check's own remarks for why a worker that
/// also runs renewals, payments and usage rating must not be taken down by a presentation
/// dependency. What matters here is that <see cref="FinancialDocumentRendererReadinessCheck"/>
/// never throws out of <c>StartAsync</c>, whatever the renderer does, and that it records exactly
/// what the renderer did onto <see cref="IFinancialDocumentRendererHealth"/> for
/// <c>FinancialDocumentDeliveryWorkHandler</c> to read afterwards.
/// </summary>
public sealed class FinancialDocumentRendererReadinessCheckTests
{
    [Fact]
    public async Task A_renderer_that_produces_bytes_starts_cleanly_and_is_recorded_healthy()
    {
        var renderer = Renderer(bytes: [0x25, 0x50, 0x44, 0x46]);
        var health = new Mock<IFinancialDocumentRendererHealth>();

        await Check(renderer, health).StartAsync(CancellationToken.None);

        health.Verify(gate => gate.RecordSuccess(), Times.Once);
        health.Verify(
            gate => gate.RecordFailure(It.IsAny<Exception?>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task A_renderer_that_returns_nothing_starts_the_host_anyway_but_is_recorded_unhealthy()
    {
        var renderer = Renderer(bytes: null);
        var health = new Mock<IFinancialDocumentRendererHealth>();

        // The one behavior this whole change exists to remove: this must not throw. A worker that
        // also runs renewals and payment reconciliation must come up even when Chromium cannot.
        await Check(renderer, health)
            .Invoking(check => check.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();

        health.Verify(
            gate => gate.RecordFailure(null, It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task A_renderer_that_returns_an_empty_array_is_recorded_unhealthy()
    {
        var renderer = Renderer(bytes: []);
        var health = new Mock<IFinancialDocumentRendererHealth>();

        await Check(renderer, health).StartAsync(CancellationToken.None);

        health.Verify(gate => gate.RecordFailure(null, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task A_renderer_that_throws_starts_the_host_and_records_the_cause()
    {
        var renderer = new Mock<IFinancialDocumentPdfRenderer>();
        var thrown = new InvalidOperationException("Chromium executable not found");
        renderer
            .Setup(engine => engine.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrown);
        var health = new Mock<IFinancialDocumentRendererHealth>();

        await Check(renderer, health)
            .Invoking(check => check.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();

        health.Verify(gate => gate.RecordFailure(thrown, It.IsAny<string>()), Times.Once);
    }

    private static Mock<IFinancialDocumentPdfRenderer> Renderer(byte[]? bytes)
    {
        var renderer = new Mock<IFinancialDocumentPdfRenderer>();
        renderer
            .Setup(engine => engine.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        return renderer;
    }

    private static FinancialDocumentRendererReadinessCheck Check(
        Mock<IFinancialDocumentPdfRenderer> renderer,
        Mock<IFinancialDocumentRendererHealth> health) =>
        new(
            renderer.Object,
            health.Object,
            NullLogger<FinancialDocumentRendererReadinessCheck>.Instance);
}
