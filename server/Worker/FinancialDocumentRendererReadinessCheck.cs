using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Subscription.DomainService.Services;

namespace Worker;

/// <summary>
/// Proves the PDF renderer actually works before this worker claims any real document work.
/// </summary>
/// <remarks>
/// This host has no HTTP surface — see <c>Dockerfile.worker</c>'s own "no HTTP host" comment — so
/// there is no <c>/health</c> endpoint an orchestrator could probe. The signal available instead is
/// the same one every other Generic Host startup failure uses: a hosted service that throws from
/// <see cref="StartAsync"/> stops the host from starting at all, which is what turns "Chromium is
/// missing or unusable in this image" into a container that never becomes ready — loud, restart-
/// looping, and impossible to mistake for a worker quietly failing every document it touches.
/// <para>
/// Registered first, deliberately, so it runs before any consumer starts pulling real work off a
/// queue. A worker that came up broken and started claiming document-delivery items anyway would
/// convert a deployment mistake into a pile of abandoned deliveries instead of a failed rollout.
/// </para>
/// </remarks>
public sealed class FinancialDocumentRendererReadinessCheck : IHostedService
{
    /// <summary>
    /// The smallest HTML PuppeteerSharp will still turn into a real PDF — enough to prove the
    /// browser launches, prints and returns bytes, and nothing about a real financial document.
    /// </summary>
    private const string ProbeHtml =
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head>" +
        "<body>renderer readiness probe</body></html>";

    private readonly IFinancialDocumentPdfRenderer _renderer;
    private readonly ILogger<FinancialDocumentRendererReadinessCheck> _logger;

    public FinancialDocumentRendererReadinessCheck(
        IFinancialDocumentPdfRenderer renderer,
        ILogger<FinancialDocumentRendererReadinessCheck> logger)
    {
        _renderer = renderer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        byte[]? pdf;

        try
        {
            pdf = await _renderer.RenderAsync(ProbeHtml, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogCritical(
                exception,
                "The PDF renderer failed its startup probe. This worker will not start; it must " +
                "not claim document-delivery work with a renderer that cannot produce a PDF.");

            throw new InvalidOperationException(
                "The PDF renderer failed its startup probe — see the inner exception.", exception);
        }

        if (pdf is not { Length: > 0 })
        {
            _logger.LogCritical(
                "The PDF renderer's startup probe produced no bytes. This worker will not start; " +
                "it must not claim document-delivery work with a renderer that cannot produce a " +
                "PDF. Check PuppeteerSharp:ExecutablePath and that Chromium is actually installed " +
                "in this image.");

            throw new InvalidOperationException(
                "The PDF renderer's startup probe produced no bytes.");
        }

        _logger.LogInformation(
            "PDF renderer startup probe succeeded ({Bytes} bytes).", pdf.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
