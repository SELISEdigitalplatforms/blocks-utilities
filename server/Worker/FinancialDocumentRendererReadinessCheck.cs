using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Subscription.DomainService.Services;

namespace Worker;

/// <summary>
/// Probes the PDF renderer once at startup and records what it found — without stopping the host.
/// </summary>
/// <remarks>
/// This used to throw out of <see cref="StartAsync"/> to fail the whole Generic Host when Chromium
/// could not produce a PDF, on the reasoning that a worker with no HTTP surface has no other way to
/// signal "not ready". That reasoning was correct about the signalling problem and wrong about the
/// blast radius: this process is not only the document renderer. It is also where renewals,
/// payment reconciliation, usage rating and every other piece of subscription background work run,
/// and none of those touch Chromium. Refusing to start the process over a presentation dependency
/// turned "PDFs are delayed" into "nothing gets paid, renewed, or billed" — a strictly worse
/// outage for a component whose job is money movement.
/// <para>
/// The probe still runs first and still logs critical on failure — an operator needs to see this
/// immediately, not discover it from a growing pending-document queue. What changed is what
/// happens next: the result is recorded on <see cref="IFinancialDocumentRendererHealth"/>, the host
/// starts regardless, and <see cref="FinancialDocumentRendererHealthMonitor"/> keeps retrying the
/// same probe on an interval so a renderer that comes up late — or a container that starts before
/// its Chromium image layer has finished settling — recovers on its own instead of requiring a
/// restart. Only <see cref="Scheduling.SubscriptionWorkHandlers.FinancialDocumentDeliveryWorkHandler"/>
/// reads the gate this sets; every other consumer of this worker is unaffected by what it finds.
/// </para>
/// </remarks>
public sealed class FinancialDocumentRendererReadinessCheck : IHostedService
{
    /// <summary>
    /// The smallest HTML PuppeteerSharp will still turn into a real PDF — enough to prove the
    /// browser launches, prints and returns bytes, and nothing about a real financial document.
    /// </summary>
    internal const string ProbeHtml =
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head>" +
        "<body>renderer readiness probe</body></html>";

    private readonly IFinancialDocumentPdfRenderer _renderer;
    private readonly IFinancialDocumentRendererHealth _health;
    private readonly ILogger<FinancialDocumentRendererReadinessCheck> _logger;

    public FinancialDocumentRendererReadinessCheck(
        IFinancialDocumentPdfRenderer renderer,
        IFinancialDocumentRendererHealth health,
        ILogger<FinancialDocumentRendererReadinessCheck> logger)
    {
        _renderer = renderer;
        _health = health;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await FinancialDocumentRendererProbe.RunAsync(
            _renderer, _health, _logger, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The one probe both the startup check and the periodic monitor run, so "is the renderer healthy"
/// is answered the same way regardless of who is asking.
/// </summary>
internal static class FinancialDocumentRendererProbe
{
    public static async Task RunAsync(
        IFinancialDocumentPdfRenderer renderer,
        IFinancialDocumentRendererHealth health,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        byte[]? pdf;

        try
        {
            pdf = await renderer
                .RenderAsync(FinancialDocumentRendererReadinessCheck.ProbeHtml, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            health.RecordFailure(exception, "the renderer threw");

            return;
        }

        if (pdf is not { Length: > 0 })
        {
            health.RecordFailure(
                null,
                "the renderer produced no bytes — check PuppeteerSharp:ExecutablePath and that " +
                "Chromium is actually installed in this image");

            return;
        }

        logger.LogInformation("PDF renderer probe succeeded ({Bytes} bytes).", pdf.Length);
        health.RecordSuccess();
    }
}
