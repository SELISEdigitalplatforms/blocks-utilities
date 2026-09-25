using System.Diagnostics.Metrics;
using FluentAssertions;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Scheduling;

namespace XUnitTest.Subscription;

/// <summary>
/// Depth arrives per tenant so each tenant's log line carries its own TenantId, but the gauge has
/// no tenant tag: two tenants' rows for one work type and status must reach it as one value.
/// </summary>
public sealed class SubscriptionWorkDepthMetricsTests
{
    [Fact]
    public void Per_tenant_depth_is_summed_into_one_gauge_value_per_work_type_and_status()
    {
        using var metrics = new SubscriptionWorkMetrics();
        var observed = new List<long>();

        // This instance's meter only: other tests running in parallel publish meters of the same name.
        var meter = typeof(SubscriptionWorkMetrics)
            .GetField("_meter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(metrics);

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument.Meter, meter) && instrument.Name == "subscription.work.queue_depth")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => observed.Add(value));
        listener.Start();

        var oldest = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc);
        metrics.RecordDepth(
        [
            new SubscriptionWorkQueueDepth(SubscriptionWorkType.Renewal, BackgroundWorkStatus.DeadLetter, 3, oldest, "T1"),
            new SubscriptionWorkQueueDepth(SubscriptionWorkType.Renewal, BackgroundWorkStatus.DeadLetter, 4, oldest.AddDays(1), "T2")
        ]);

        listener.RecordObservableInstruments();

        observed.Should().Equal(7);
    }
}
