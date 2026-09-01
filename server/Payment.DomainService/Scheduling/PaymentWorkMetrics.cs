using System.Diagnostics.Metrics;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Scheduling;

/// <summary>
/// What the queue looks like from outside, as numbers rather than log lines.
/// </summary>
/// <remarks>
/// Logs already say what happened to each item, which is enough to investigate an incident once
/// somebody notices one. These exist to notice one: a queue filling faster than it drains, an oldest
/// due age creeping up, dead letters appearing at all.
/// <para>
/// Built on <see cref="Meter"/> from the framework rather than on a metrics library, deliberately.
/// No exporter is configured in this repository, and choosing one is a platform decision about
/// collectors and endpoints rather than a subscription decision. Instruments nobody listens to cost
/// almost nothing, and an exporter added later attaches to these without touching this code.
/// </para>
/// </remarks>
public sealed class PaymentWorkMetrics : IDisposable
{
    /// <summary>The name an exporter subscribes to.</summary>
    public const string MeterName = "Blocks.Payment.BackgroundWork";

    private readonly Meter _meter;
    private readonly Counter<long> _claimed;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _retried;
    private readonly Counter<long> _deadLettered;
    private readonly Counter<long> _leaseLost;
    private readonly Histogram<double> _duration;
    private readonly Histogram<double> _lag;
    private readonly Counter<long> _setupExpired;
    private readonly Histogram<double> _setupPendingAge;

    /// <summary>
    /// The last depth reading, published rather than measured on demand.
    /// </summary>
    /// <remarks>
    /// Depth is an aggregation over a collection in another database. Measuring it inside a gauge
    /// callback would put that query on whatever thread the exporter happens to collect on, at
    /// whatever interval it happens to use. The scheduler already computes it on an idle pass, so
    /// the gauge reports what that pass last saw.
    /// </remarks>
    private volatile IReadOnlyList<PaymentWorkQueueDepth> _depths = [];

    public PaymentWorkMetrics()
    {
        _meter = new Meter(MeterName);

        _claimed = _meter.CreateCounter<long>(
            "payment.work.claimed",
            unit: "{item}",
            description: "Work items claimed by a worker.");

        _completed = _meter.CreateCounter<long>(
            "payment.work.completed",
            unit: "{item}",
            description: "Work items that finished successfully.");

        _retried = _meter.CreateCounter<long>(
            "payment.work.retried",
            unit: "{item}",
            description: "Work items returned to the queue after a transient failure.");

        _deadLettered = _meter.CreateCounter<long>(
            "payment.work.dead_lettered",
            unit: "{item}",
            description: "Work items given up on. Anything above zero needs a person.");

        _leaseLost = _meter.CreateCounter<long>(
            "payment.work.lease_lost",
            unit: "{item}",
            description:
                "Attempts that lost their lease mid-flight, by expiry or by another worker taking " +
                "it. A rising count means leases are shorter than the work they cover.");

        _duration = _meter.CreateHistogram<double>(
            "payment.work.duration",
            unit: "ms",
            description: "How long a handler took.");

        _lag = _meter.CreateHistogram<double>(
            "payment.work.lag",
            unit: "s",
            description:
                "How long after becoming due an item was picked up. The number that means " +
                "\"a tenant's renewal is late\", which depth alone does not.");

        _setupExpired = _meter.CreateCounter<long>(
            "payment.setup.expired",
            unit: "{item}",
            description:
                "Card setups the recovery sweep gave up on because a completion signal never " +
                "arrived. Anything above zero is a webhook Adyen never delivered.");

        _setupPendingAge = _meter.CreateHistogram<double>(
            "payment.setup.pending_age",
            unit: "s",
            description:
                "Age of every currently pending card setup still missing a completion signal, " +
                "tagged by which of the two is missing, observed on each reconciliation sweep -- " +
                "not only once a setup is already due for expiry, so the age can be watched " +
                "climbing over time.");

        _meter.CreateObservableGauge(
            "payment.work.queue_depth",
            ObserveDepth,
            unit: "{item}",
            description: "Items waiting, by work type and status.");

        _meter.CreateObservableGauge(
            "payment.work.oldest_due_age",
            ObserveOldestDueAge,
            unit: "s",
            description:
                "How long the oldest unfinished item has been due. A queue can be shallow and " +
                "still be failing to drain the one thing that matters.");
    }

    public void RecordClaimed(PaymentWorkType workType) =>
        _claimed.Add(1, Tag(workType));

    public void RecordCompleted(PaymentWorkType workType, TimeSpan duration, TimeSpan lag)
    {
        _completed.Add(1, Tag(workType));
        _duration.Record(duration.TotalMilliseconds, Tag(workType), Outcome("completed"));
        _lag.Record(lag.TotalSeconds, Tag(workType));
    }

    public void RecordRetried(PaymentWorkType workType, string errorCode, TimeSpan duration)
    {
        _retried.Add(1, Tag(workType), Error(errorCode));
        _duration.Record(duration.TotalMilliseconds, Tag(workType), Outcome("retried"));
    }

    public void RecordDeadLettered(
        PaymentWorkType workType,
        string errorCode,
        TimeSpan duration)
    {
        _deadLettered.Add(1, Tag(workType), Error(errorCode));
        _duration.Record(duration.TotalMilliseconds, Tag(workType), Outcome("dead_lettered"));
    }

    public void RecordLeaseLost(PaymentWorkType workType) =>
        _leaseLost.Add(1, Tag(workType));

    /// <summary>Publishes what an idle pass measured, for the gauges to report.</summary>
    public void RecordDepth(IReadOnlyList<PaymentWorkQueueDepth> depths) =>
        _depths = depths;

    /// <summary>
    /// Records one card setup the expiry sweep is still watching, tagged by which of the two
    /// completion signals it is still missing (or "both") -- the detail a raw age alone cannot
    /// tell an operator.
    /// </summary>
    public void RecordSetupPendingAge(TimeSpan age, string missingSignal) =>
        _setupPendingAge.Record(age.TotalSeconds, MissingSignal(missingSignal));

    /// <summary>Records a setup the sweep gave up on and expired.</summary>
    public void RecordSetupExpired(string missingSignal) =>
        _setupExpired.Add(1, MissingSignal(missingSignal));

    private static KeyValuePair<string, object?> MissingSignal(string missingSignal) =>
        new("missing_signal", missingSignal);

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

    private static KeyValuePair<string, object?> Tag(PaymentWorkType workType) =>
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
