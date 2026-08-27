using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// The periodic sweep that finishes a scheduled cancellation once its period has actually run out.
/// </summary>
public sealed class SubscriptionCancellationEffectiveProcessorTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly Mock<IUsagePeriodClosureRepository> _closures = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

    private IReadOnlyList<SubscriptionDetail> _due = [];
    private SubscriptionTransition? _transition;

    public SubscriptionCancellationEffectiveProcessorTests()
    {
        _subscriptions
            .Setup(repository => repository.ListDueForCancellationAsync(
                TenantId,
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _due);

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                It.IsAny<string>(),
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionTransition, CancellationToken>(
                (_, _, transition, _) => _transition = transition)
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task A_due_scheduled_cancellation_is_carried_to_effective()
    {
        _due = [NewSubscription("sub-1")];

        var ended = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        ended.Should().Be(1);
        _transition!.NewStatus.Should().Be(SubscriptionStatus.Canceled);
        _transition.CancelAtPeriodEnd.Should().BeFalse();
        _transition.CanCancelImmediately.Should().BeFalse();
        _transition.EndedAtUtc.Should().Be(_time.GetUtcNow().UtcDateTime);
        _transition.ClearNextFeeBillingAt.Should().BeTrue();
        _transition.ClearNextUsageBillingAt.Should().BeTrue();
        _transition.Event!.EventType.Should().Be(SubscriptionConstants.SubscriptionCanceled);
    }

    /// <summary>
    /// The sweep may pick this subscription up well after its period actually ended — a busy
    /// queue, a paused worker, a deploy. What was promised must not silently stretch to cover
    /// however late the pass happened to run.
    /// </summary>
    [Fact]
    public async Task A_worker_running_late_still_ends_the_subscription_at_the_promised_boundary()
    {
        var subscription = NewSubscription("sub-1");
        subscription.CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.CurrentUsagePeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.CurrentUsagePeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        _due = [subscription];
        _time.Advance(TimeSpan.FromDays(3)); // the pass actually runs on September 4.

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _transition!.EndedAtUtc.Should().Be(
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            "the promised boundary, not the instant this late pass happened to run");
        _transition.OutgoingUsagePeriod!.PeriodEndUtc.Should().Be(
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            "an invoice through September 4 would claim three days of service never granted");
    }

    /// <summary>
    /// A usage window that runs longer than the billing period it is nested in — the ordinary
    /// shape for, say, an annual plan metering monthly. The window must still be cut at the
    /// billing boundary cancellation actually promised, not left to run to its own later end.
    /// </summary>
    [Fact]
    public async Task A_usage_window_extending_beyond_the_billing_period_is_cut_at_the_promised_boundary()
    {
        var subscription = NewSubscription("sub-1");
        subscription.CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.CurrentUsagePeriodStartUtc = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        subscription.CurrentUsagePeriodEndUtc = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
        _due = [subscription];

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _transition!.OutgoingUsagePeriod!.PeriodStartUtc.Should().Be(
            new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc));
        _transition.OutgoingUsagePeriod.PeriodEndUtc.Should().Be(
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            "the billing period's own end, cut short of the usage window's natural September 15");
    }

    [Fact]
    public async Task Finalizing_starts_closing_the_usage_period_at_the_promised_boundary()
    {
        var subscription = NewSubscription("sub-1");
        _due = [subscription];

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _closures.Verify(
            closures => closures.StartClosingAsync(
                TenantId, "sub-1", It.IsAny<string>(), subscription.CurrentPeriodEndUtc,
                subscription.CorrelationId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Finishing_a_cancellation_invalidates_the_cached_entitlement()
    {
        _due = [NewSubscription("sub-1")];

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _cache.Verify(
            cache => cache.Invalidate(TenantId, OrganizationId),
            Times.Once);
    }

    [Fact]
    public async Task Nothing_due_processes_nothing()
    {
        _due = [];

        var ended = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        ended.Should().Be(0);
        _cache.Verify(
            cache => cache.Invalidate(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task A_lost_compare_and_set_is_not_treated_as_an_error()
    {
        _due = [NewSubscription("sub-1")];
        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                It.IsAny<string>(),
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var ended = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        ended.Should().Be(0, "another worker or an interactive escalation already ended it — " +
                             "its outcome stands, and the cache must not be invalidated twice");
        _cache.Verify(
            cache => cache.Invalidate(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task The_batch_size_setting_bounds_the_query()
    {
        _due = [NewSubscription("sub-1")];

        await Processor(batchSize: 5).ProcessDueAsync(TenantId, CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.ListDueForCancellationAsync(
                TenantId,
                It.IsAny<DateTime>(),
                5,
                It.IsAny<CancellationToken>()));
    }

    private SubscriptionCancellationEffectiveProcessor Processor(int batchSize = 50) => new(
        _subscriptions.Object,
        new SubscriptionOutboxEventFactory(),
        _cache.Object,
        new OptionsStub(batchSize),
        NullLogger<SubscriptionCancellationEffectiveProcessor>.Instance,
        _time,
        _closures.Object);

    private static SubscriptionDetail NewSubscription(string id) => new()
    {
        ItemId = id,
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CancelAtPeriodEnd = true,
        CanCancelImmediately = true,
        CanceledAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public OptionsStub(int batchSize) =>
            CurrentValue = new SubscriptionOptions { CancellationBatchSize = batchSize };

        public SubscriptionOptions CurrentValue { get; }

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
