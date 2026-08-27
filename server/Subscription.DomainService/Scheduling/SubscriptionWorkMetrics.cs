using System.Diagnostics.Metrics;
using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// What the queue looks like from outside, as numbers rather than log lines.
/// </summary>
/// <remarks>
/// Logs already say what happened to each item, which is enough to investigate an incident once
/// somebody notices one. These exist to notice one: a queue filling faster than it drains, an oldest
/// due age creeping up, dead letters appearing at all.
/// <para>
/// Built on <see cref="Meter"/> and exported by the worker through OTLP. Alert rules and the
/// dashboard are versioned under <c>monitoring/</c>, so production visibility is deployed with the
/// code whose behavior it describes.
/// </para>
/// </remarks>
public sealed class SubscriptionWorkMetrics : IDisposable
{
    /// <summary>The name an exporter subscribes to.</summary>
    public const string MeterName = "Blocks.Subscription.BackgroundWork";

    private readonly Meter _meter;
    private readonly Counter<long> _claimed;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _retried;
    private readonly Counter<long> _deadLettered;
    private readonly Counter<long> _leaseLost;
    private readonly Counter<long> _repairAnnouncements;
    private readonly Histogram<double> _duration;
    private readonly Histogram<double> _lag;

    /// <summary>
    /// The last depth reading, published rather than measured on demand.
    /// </summary>
    /// <remarks>
    /// Depth is an aggregation over a collection in another database. Measuring it inside a gauge
    /// callback would put that query on whatever thread the exporter happens to collect on, at
    /// whatever interval it happens to use. The scheduler already computes it on an idle pass, so
    /// the gauge reports what that pass last saw.
    /// </remarks>
    private volatile IReadOnlyList<SubscriptionWorkQueueDepth> _depths = [];

    public SubscriptionWorkMetrics()
    {
        _meter = new Meter(MeterName);

        _claimed = _meter.CreateCounter<long>(
            "subscription.work.claimed",
            unit: "{item}",
            description: "Work items claimed by a worker.");

        _completed = _meter.CreateCounter<long>(
            "subscription.work.completed",
            unit: "{item}",
            description: "Work items that finished successfully.");

        _retried = _meter.CreateCounter<long>(
            "subscription.work.retried",
            unit: "{item}",
            description: "Work items returned to the queue after a transient failure.");

        _deadLettered = _meter.CreateCounter<long>(
            "subscription.work.dead_lettered",
            unit: "{item}",
            description: "Work items given up on. Anything above zero needs a person.");

        _leaseLost = _meter.CreateCounter<long>(
            "subscription.work.lease_lost",
            unit: "{item}",
            description:
                "Attempts that lost their lease mid-flight, by expiry or by another worker taking " +
                "it. A rising count means leases are shorter than the work they cover.");

        _repairAnnouncements = _meter.CreateCounter<long>(
            "subscription.work.repair_announced",
            unit: "{item}",
            description:
                "Work the repair sweep found unannounced and enqueued. Steadily above zero means " +
                "producers at the point of change are losing their scheduling writes, which the " +
                "sweep is covering for — the queue draining normally is not evidence that they are " +
                "working.");

        _duration = _meter.CreateHistogram<double>(
            "subscription.work.duration",
            unit: "ms",
            description: "How long a handler took.");

        _lag = _meter.CreateHistogram<double>(
            "subscription.work.lag",
            unit: "s",
            description:
                "How long after becoming due an item was picked up. The number that means " +
                "\"a tenant's renewal is late\", which depth alone does not.");

        _meter.CreateObservableGauge(
            "subscription.work.queue_depth",
            ObserveDepth,
            unit: "{item}",
            description: "Items waiting, by work type and status.");

        _meter.CreateObservableGauge(
            "subscription.work.oldest_due_age",
            ObserveOldestDueAge,
            unit: "s",
            description:
                "How long the oldest unfinished item has been due. A queue can be shallow and " +
                "still be failing to drain the one thing that matters.");
    }

    public void RecordClaimed(SubscriptionWorkType workType) =>
        _claimed.Add(1, Tag(workType));

    public void RecordCompleted(SubscriptionWorkType workType, TimeSpan duration, TimeSpan lag)
    {
        _completed.Add(1, Tag(workType));
        _duration.Record(duration.TotalMilliseconds, Tag(workType), Outcome("completed"));
        _lag.Record(lag.TotalSeconds, Tag(workType));
    }

    public void RecordRetried(SubscriptionWorkType workType, string errorCode, TimeSpan duration)
    {
        _retried.Add(1, Tag(workType), Error(errorCode));
        _duration.Record(duration.TotalMilliseconds, Tag(workType), Outcome("retried"));
    }

    public void RecordDeadLettered(
        SubscriptionWorkType workType,
        string errorCode,
        TimeSpan duration)
    {
        _deadLettered.Add(1, Tag(workType), Error(errorCode));
        _duration.Record(duration.TotalMilliseconds, Tag(workType), Outcome("dead_lettered"));
    }

    public void RecordLeaseLost(SubscriptionWorkType workType) =>
        _leaseLost.Add(1, Tag(workType));

    /// <summary>
    /// Records work the repair sweep had to announce because nothing else had.
    /// </summary>
    /// <remarks>
    /// Worth its own counter rather than being folded into scheduling volume. Everything else the
    /// queue reports is the system working; this is the system having already failed once, quietly,
    /// somewhere upstream, and being caught.
    /// </remarks>
    public void RecordRepairAnnouncements(int count)
    {
        if (count > 0)
        {
            _repairAnnouncements.Add(count);
        }
    }

    /// <summary>Publishes what an idle pass measured, for the gauges to report.</summary>
    public void RecordDepth(IReadOnlyList<SubscriptionWorkQueueDepth> depths) =>
        _depths = depths;

    private IEnumerable<Measurement<long>> ObserveDepth() =>
        _depths.Select(depth => new Measurement<long>(
            depth.Count,
            Tag(depth.WorkType),
            new KeyValuePair<string, object?>("status", depth.Status.ToString())));

    private IEnumerable<Measurement<double>> ObserveOldestDueAge()
    {
        var now = DateTime.UtcNow;

        return _depths
            .Where(depth => depth.OldestDueAtUtc is not null)
            .Select(depth => new Measurement<double>(
                Math.Max(0, (now - depth.OldestDueAtUtc!.Value).TotalSeconds),
                Tag(depth.WorkType),
                new KeyValuePair<string, object?>("status", depth.Status.ToString())));
    }

    private static KeyValuePair<string, object?> Tag(SubscriptionWorkType workType) =>
        new("work_type", workType.ToString());

    private static KeyValuePair<string, object?> Outcome(string outcome) =>
        new("outcome", outcome);

    /// <summary>
    /// A classification, never a provider message.
    /// </summary>
    /// <remarks>
    /// Error codes are a bounded set; provider messages are not, and a metric tagged with unbounded
    /// values is a cardinality explosion in whatever collects it.
    /// </remarks>
    private static KeyValuePair<string, object?> Error(string errorCode) =>
        new("error_code", string.IsNullOrWhiteSpace(errorCode) ? "unknown" : errorCode);

    public void Dispose() => _meter.Dispose();
}
