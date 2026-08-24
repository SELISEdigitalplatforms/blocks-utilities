using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// What each kind of work is worth, when it comes due, and what a producer is allowed to break.
/// </summary>
/// <remarks>
/// These decisions live in the scheduler rather than at each call site on purpose: the grace windows
/// are read by the sweep too, and several services announce the same kinds of work. Duplicated, they
/// drift — and a due instant that drifts is a renewal that runs at the wrong time.
/// </remarks>
public sealed class SubscriptionWorkSchedulerTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<ISubscriptionWorkQueue> _queue = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero));

    private readonly List<SubscriptionBackgroundWork> _scheduled = [];

    public SubscriptionWorkSchedulerTests() =>
        _queue
            .Setup(queue => queue.ScheduleAsync(
                It.IsAny<SubscriptionBackgroundWork>(), It.IsAny<CancellationToken>()))
            .Callback((SubscriptionBackgroundWork work, CancellationToken _) => _scheduled.Add(work))
            .ReturnsAsync(true);

    [Fact]
    public async Task Money_moving_work_outranks_bookkeeping()
    {
        // What runs first when the queue is behind. A renewal that waits is revenue not collected;
        // an outbox event that waits is a notification that arrives late.
        foreach (var workType in Enum.GetValues<SubscriptionWorkType>())
        {
            await Scheduler().ScheduleAsync(
                workType, TenantId, $"key-{workType}", _time.GetUtcNow().UtcDateTime, "corr-1");
        }

        var byType = _scheduled.ToDictionary(work => work.WorkType, work => work.Priority);

        byType[SubscriptionWorkType.SettlementReservationRecovery]
            .Should().BeLessThan(byType[SubscriptionWorkType.Renewal]);
        byType[SubscriptionWorkType.Renewal]
            .Should().BeLessThan(byType[SubscriptionWorkType.UsageInvoiceCharge]);
        byType[SubscriptionWorkType.UsageInvoiceCharge]
            .Should().BeLessThan(byType[SubscriptionWorkType.OutboxPublication]);
    }

    [Fact]
    public async Task A_reservation_recovery_comes_due_after_the_grace_window()
    {
        var reservedAt = _time.GetUtcNow().UtcDateTime;

        await Scheduler(new SubscriptionOptions { SettlementReservationGraceMinutes = 20 })
            .ScheduleReservationRecoveryAsync(
                NewSubscription(),
                new SettlementReservation
                {
                    ReservationId = "reservation-1",
                    ReservedAtUtc = reservedAt
                },
                "corr-1");

        var work = _scheduled.Should().ContainSingle().Subject;

        // Not now: a reservation that settles normally is gone long before this, and the handler
        // finds nothing to do. Due immediately, it would recover reservations never in trouble.
        work.DueAtUtc.Should().Be(reservedAt.AddMinutes(20));
        work.WorkKey.Should().Be("reservation:reservation-1");
        work.AggregateId.Should().Be("sub-1");
    }

    [Fact]
    public async Task An_activation_recovery_comes_due_after_the_shopper_has_had_their_grace()
    {
        await Scheduler(new SubscriptionOptions { InitialChargeGraceMinutes = 45 })
            .ScheduleActivationRecoveryAsync(NewSubscription());

        var work = _scheduled.Should().ContainSingle().Subject;

        work.DueAtUtc.Should().Be(_time.GetUtcNow().UtcDateTime.AddMinutes(45));
        work.WorkKey.Should().Be("activation:sub-1");
    }

    [Fact]
    public async Task A_usage_invoice_is_charged_as_soon_as_it_exists()
    {
        await Scheduler().ScheduleUsageInvoiceChargeAsync(
            NewSubscription(), "M20260901T000000Z", "corr-1");

        var work = _scheduled.Should().ContainSingle().Subject;

        // The invoice is written, so waiting only delays revenue and the subscriber's own record.
        work.DueAtUtc.Should().Be(_time.GetUtcNow().UtcDateTime);
        work.WorkKey.Should().Be("usage-charge:M20260901T000000Z");
    }

    [Fact]
    public async Task A_usage_window_closes_on_the_clock_rather_than_on_demand()
    {
        var endsAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        await Scheduler().ScheduleUsagePeriodClosureAsync(NewSubscription(), endsAt);

        var work = _scheduled.Should().ContainSingle().Subject;

        work.DueAtUtc.Should().Be(endsAt);
        work.WorkKey.Should().Be("usage-close:20261001T000000Z");
    }

    [Fact]
    public async Task A_producer_that_cannot_schedule_is_not_a_producer_that_failed()
    {
        // By the time a producer runs, what it announces has already happened. A scheduling write
        // in another database that fails must not be able to undo or fail it.
        _queue
            .Setup(queue => queue.ScheduleAsync(
                It.IsAny<SubscriptionBackgroundWork>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("root database unreachable"));

        var scheduled = await Scheduler().TryScheduleAsync(
            SubscriptionWorkType.Renewal,
            TenantId,
            "renewal:M20261001T000000Z",
            _time.GetUtcNow().UtcDateTime,
            "corr-1");

        scheduled.Should().BeFalse("the caller is told, without being interrupted");
    }

    [Fact]
    public async Task A_caller_that_needs_to_know_still_hears_about_a_failure()
    {
        _queue
            .Setup(queue => queue.ScheduleAsync(
                It.IsAny<SubscriptionBackgroundWork>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("root database unreachable"));

        var act = async () => await Scheduler().ScheduleAsync(
            SubscriptionWorkType.Renewal,
            TenantId,
            "renewal:M20261001T000000Z",
            _time.GetUtcNow().UtcDateTime,
            "corr-1");

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Scheduling_carries_the_attempt_ceiling_from_configuration()
    {
        await Scheduler(new SubscriptionOptions { SchedulerMaxAttempts = 9 })
            .ScheduleAsync(
                SubscriptionWorkType.Renewal,
                TenantId,
                "renewal:M20261001T000000Z",
                _time.GetUtcNow().UtcDateTime,
                "corr-1");

        _scheduled.Should().ContainSingle().Which.MaxAttempts.Should().Be(9);
    }

    private SubscriptionWorkScheduler Scheduler(SubscriptionOptions? options = null) => new(
        _queue.Object,
        new SubscriptionOptionsMonitorStub(options ?? new SubscriptionOptions()),
        NullLogger<SubscriptionWorkScheduler>.Instance,
        _time);

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = "org-1",
        Status = SubscriptionStatus.Incomplete,
        CorrelationId = "corr-created"
    };
}
