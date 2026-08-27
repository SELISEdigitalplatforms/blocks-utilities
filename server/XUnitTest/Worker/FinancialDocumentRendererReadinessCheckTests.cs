using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Services;
using Worker;

namespace XUnitTest.Worker;

/// <summary>
/// This host has no HTTP surface to probe, so the only signal available is whether the process
/// starts at all — see the check's own remarks. What matters here is exactly that: a broken
/// renderer must throw out of <c>StartAsync</c>, and a working one must not.
/// </summary>
public sealed class FinancialDocumentRendererReadinessCheckTests
{
    [Fact]
    public async Task A_renderer_that_produces_bytes_starts_cleanly()
    {
        var renderer = new Mock<IFinancialDocumentPdfRenderer>();
        renderer
            .Setup(engine => engine.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([0x25, 0x50, 0x44, 0x46]);

        var check = Check(renderer);

        await check.Invoking(check => check.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_renderer_that_returns_nothing_fails_startup()
    {
        var renderer = new Mock<IFinancialDocumentPdfRenderer>();
        renderer
            .Setup(engine => engine.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var check = Check(renderer);

        // The only mechanism this non-HTTP host has for failing readiness: the hosted service
        // throwing out of StartAsync stops the Generic Host from starting at all.
        await check.Invoking(check => check.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_renderer_that_returns_an_empty_array_fails_startup()
    {
        var renderer = new Mock<IFinancialDocumentPdfRenderer>();
        renderer
            .Setup(engine => engine.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var check = Check(renderer);

        await check.Invoking(check => check.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_renderer_that_throws_fails_startup_with_the_cause_attached()
    {
        var renderer = new Mock<IFinancialDocumentPdfRenderer>();
        renderer
            .Setup(engine => engine.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Chromium executable not found"));

        var check = Check(renderer);

        var thrown = await check.Invoking(check => check.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.InnerException!.Message.Should().Contain("Chromium executable not found");
    }

    private static FinancialDocumentRendererReadinessCheck Check(
        Mock<IFinancialDocumentPdfRenderer> renderer) =>
        new(renderer.Object, NullLogger<FinancialDocumentRendererReadinessCheck>.Instance);
}
