using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Subscription.DomainService.Services;

/// <summary>
/// The one place this worker's process keeps its answer to "can the PDF renderer produce a PDF
/// right now".
/// </summary>
/// <remarks>
/// A single mutable flag, not a generic health-check registry. The worker hosts renewals,
/// payments, usage rating and document issuance in the same process as document delivery, and
/// every one of those keeps working when Chromium is down — only the code that calls the renderer
/// has anything to gate. A health-check framework answers "is the process healthy" for an
/// orchestrator; this answers "can this one call be trusted", for callers inside the same process,
/// which is a narrower and different question. Building the general version to answer the specific
/// one would give every other subsystem a place to accidentally wire itself to Chromium's fate.
/// <para>
/// Registered as a singleton, so the readiness check's startup probe, the periodic monitor's
/// retries, and the delivery handler's read all see one shared state rather than three copies that
/// could disagree about whether the renderer is up.
/// </para>
/// </remarks>
public sealed class FinancialDocumentRendererHealthGate : IFinancialDocumentRendererHealth
{
    public const string MeterName = "Blocks.Subscription.FinancialDocumentRenderer";

    private readonly ILogger<FinancialDocumentRendererHealthGate> _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _unhealthyTransitions;
    private readonly Counter<long> _recoveries;

    // Volatile rather than behind a lock: one bool, read far more often (every delivery attempt)
    // than it is written (once per probe), and a reader racing a writer by a few milliseconds
    // reads the old-but-still-accurate answer rather than anything unsafe.
    private volatile bool _isHealthy = true;

    public FinancialDocumentRendererHealthGate(ILogger<FinancialDocumentRendererHealthGate> logger)
    {
        _logger = logger;
        _meter = new Meter(MeterName);

        _unhealthyTransitions = _meter.CreateCounter<long>(
            "financial_document.renderer.unhealthy",
            unit: "{transition}",
            description: "Times the PDF renderer's health probe failed after last succeeding.");

        _recoveries = _meter.CreateCounter<long>(
            "financial_document.renderer.recovered",
            unit: "{transition}",
            description: "Times the PDF renderer's health probe succeeded after last failing.");
    }

    public bool IsHealthy => _isHealthy;

    public void RecordSuccess()
    {
        // Only the transition is interesting, both for the metric and for the log: a probe
        // succeeding for the hundredth time in a row is not a fact anybody needs to see.
        var wasUnhealthy = !_isHealthy;
        _isHealthy = true;

        if (!wasUnhealthy)
        {
            return;
        }

        _recoveries.Add(1);
        _logger.LogInformation(
            "The PDF renderer's health probe succeeded again. Document delivery reopens.");
    }

    public void RecordFailure(Exception? exception, string reason)
    {
        var wasHealthy = _isHealthy;
        _isHealthy = false;

        if (!wasHealthy)
        {
            return;
        }

        _unhealthyTransitions.Add(1);

        // Critical, not error: nothing else in this process will surface this on its own, since by
        // design every other subsystem keeps running. This is the one line that says why document
        // delivery has gone quiet.
        _logger.LogCritical(
            exception,
            "The PDF renderer's health probe failed ({Reason}). Document delivery is paused until " +
            "it recovers; renewals, payments, usage rating and document issuance continue " +
            "normally.",
            reason);
    }
}
