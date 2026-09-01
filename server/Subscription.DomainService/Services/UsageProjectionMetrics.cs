using System.Diagnostics;
using System.Diagnostics.Metrics;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// The current-usage projection as numbers rather than log lines.
/// </summary>
/// <remarks>
/// Logs say what happened to one read or one publish, which is enough to investigate an incident once
/// somebody notices one. These exist to notice one, and to answer the question the projection was
/// built to answer: is reading it actually faster than reading the counters, and is it staying
/// current enough to be worth reading?
/// <para>
/// The duration histogram carries the read mode as a dimension, so p50, p95 and p99 for
/// <c>authoritative</c> and <c>projection</c> come out of the same instrument and are directly
/// comparable — no separate benchmark harness needed to answer the comparison, only load applied to
/// both modes.
/// </para>
/// <para>
/// <b>No tenant, organization or subscription dimension.</b> Deliberately: a per-tenant label
/// multiplies every series by the tenant count, and there are thousands. Those identifiers belong in
/// logs and trace spans, where they are attached to individual reads, and both carry them.
/// </para>
/// <para>
/// Built on <see cref="Meter"/> and exported through OTLP, following
/// <c>SubscriptionWorkMetrics</c>.
/// </para>
/// </remarks>
public sealed class UsageProjectionMetrics : IDisposable
{
    /// <summary>The name an exporter subscribes to.</summary>
    public const string MeterName = "Blocks.Subscription.UsageProjection";

    /// <summary>
    /// The process-wide instrument set.
    /// </summary>
    /// <remarks>
    /// A shared default so a service constructed by hand — in a test, or by a caller that predates
    /// this — records into the same instruments the running process exports, rather than needing one
    /// threaded through every call site before any of it is visible.
    /// </remarks>
    public static UsageProjectionMetrics Shared { get; } = new();

    private readonly Meter _meter;
    private readonly Histogram<double> _readDuration;
    private readonly Histogram<double> _projectionAge;
    private readonly Histogram<double> _publishDuration;
    private readonly Histogram<long> _versionLag;
    private readonly Counter<long> _reads;
    private readonly Counter<long> _fallbacks;
    private readonly Counter<long> _staleReads;
    private readonly Counter<long> _publishes;
    private readonly Counter<long> _publishFailures;
    private readonly Counter<long> _repairsScheduled;
    private readonly Counter<long> _repairsCompleted;

    public UsageProjectionMetrics()
    {
        _meter = new Meter(MeterName);

        _readDuration = _meter.CreateHistogram<double>(
            "subscription.usage.read.duration",
            unit: "ms",
            description:
            "How long a current-usage read took, by requested and actual mode. The mode dimension " +
            "is what makes p50/p95/p99 comparable between the counters and the projection.");

        _projectionAge = _meter.CreateHistogram<double>(
            "subscription.usage.projection.age",
            unit: "s",
            description:
            "How old the freshest projected document in an answer was. Recorded only for reads the " +
            "projection actually served.");

        _publishDuration = _meter.CreateHistogram<double>(
            "subscription.usage.projection.publish.duration",
            unit: "ms",
            description:
            "How long publishing one projected document took. This runs inside a customer-facing " +
            "billing call, so its tail is latency added to that call.");

        _versionLag = _meter.CreateHistogram<long>(
            "subscription.usage.projection.version_lag",
            unit: "{records}",
            description:
            "How far a projection was behind its counter when the reconciliation pass found it, in " +
            "ledger entries.");

        _reads = _meter.CreateCounter<long>(
            "subscription.usage.read.count",
            description: "Current-usage reads, by requested and actual mode.");

        _fallbacks = _meter.CreateCounter<long>(
            "subscription.usage.read.fallback.count",
            description:
            "Projection reads answered by the counters instead, by reason - nothing published, or " +
            "only some windows published.");

        _staleReads = _meter.CreateCounter<long>(
            "subscription.usage.read.stale.count",
            description: "Reads whose answer contained at least one document past the staleness threshold.");

        _publishes = _meter.CreateCounter<long>(
            "subscription.usage.projection.publish.count",
            description: "Projection publish attempts, by outcome.");

        _publishFailures = _meter.CreateCounter<long>(
            "subscription.usage.projection.publish.failure.count",
            description:
            "Publishes that could not be written after the usage had committed. Every one of these " +
            "left a projection behind its counter and scheduled a repair.");

        _repairsScheduled = _meter.CreateCounter<long>(
            "subscription.usage.projection.repair.scheduled.count",
            description: "Repair items scheduled, by what noticed the miss.");

        _repairsCompleted = _meter.CreateCounter<long>(
            "subscription.usage.projection.repair.completed.count",
            description: "Projected documents written by a repair, by what drove it.");
    }

    public void RecordRead(
        UsageReadMode requested,
        UsageReadMode actual,
        UsageReadFallback fallback,
        TimeSpan duration,
        double? newestAgeSeconds,
        bool stale)
    {
        var tags = new TagList
        {
            { "requested_mode", requested.ToString() },
            { "actual_mode", actual.ToString() }
        };

        _reads.Add(1, tags);
        _readDuration.Record(duration.TotalMilliseconds, tags);

        if (newestAgeSeconds is { } age)
        {
            _projectionAge.Record(age, tags);
        }

        if (stale)
        {
            _staleReads.Add(1, tags);
        }

        if (fallback != UsageReadFallback.None)
        {
            _fallbacks.Add(1, new TagList
            {
                { "requested_mode", requested.ToString() },
                { "reason", fallback.ToString() }
            });
        }
    }

    public void RecordPublish(UsageProjectionOutcome outcome, TimeSpan duration)
    {
        var tags = new TagList { { "outcome", outcome.ToString() } };

        _publishes.Add(1, tags);
        _publishDuration.Record(duration.TotalMilliseconds, tags);

        if (outcome == UsageProjectionOutcome.RepairScheduled)
        {
            _publishFailures.Add(1);
        }
    }

    public void RecordRepairScheduled(string source) =>
        _repairsScheduled.Add(1, new TagList { { "source", source } });

    public void RecordRepairCompleted(string source, int written) =>
        _repairsCompleted.Add(written, new TagList { { "source", source } });

    /// <summary>
    /// How far behind a projection was when the reconciliation pass repaired it.
    /// </summary>
    /// <remarks>
    /// Recorded by the sweep rather than by a read, because measuring lag needs both the projection
    /// and its counter and the sweep is the only place that reads both. A read reports age instead,
    /// which is cheaper and is why the two are separate instruments.
    /// </remarks>
    public void RecordVersionLag(long lag) => _versionLag.Record(lag);

    public void Dispose() => _meter.Dispose();
}
