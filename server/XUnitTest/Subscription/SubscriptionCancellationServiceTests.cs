using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Ending a subscription, and how little that changes today.
/// </summary>
public sealed class SubscriptionCancellationServiceTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IEntitlementSnapshotCache> _cache = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail? _subscription = NewSubscription();
    private SubscriptionTransition? _transition;

    public SubscriptionCancellationServiceTests()
    {
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        _subscriptions
            .Setup(repository => repository.GetAsync(
                TenantId, OrganizationId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                "sub-1",
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionTransition, CancellationToken>(
                (_, _, transition, _) => _transition = transition)
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task Cancelling_keeps_the_period_that_was_paid_for()
    {
        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _transition!.NewStatus.Should().Be(SubscriptionStatus.Active,
            "taking access away on the day someone cancels is charging for a month and " +
            "delivering part of one");
        _transition.CancelAtPeriodEnd.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(SubscriptionStatus.Active));
    }

    [Fact]
    public async Task Cancelling_stops_the_next_payment()
    {
        await Service().CancelAsync(
            "sub-1", immediately: false, null, "corr-1", CancellationToken.None);

        _transition!.ClearNextFeeBillingAt.Should().BeTrue();
        _transition.CanceledAtUtc.Should().Be(
            new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task When_it_was_asked_for_is_separate_from_when_it_takes_effect()
    {
        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, "corr-1", CancellationToken.None);

        result.Value!.CanceledAtUtc.Should().NotBeNull();
        _transition!.EndedAtUtc.Should().BeNull(
            "it has not ended yet, and conflating the two loses the answer to most support " +
            "questions about cancellation");
    }

    [Fact]
    public async Task An_immediate_cancellation_ends_it_now()
    {
        var result = await Service().CancelAsync(
            "sub-1", immediately: true, "fraud", "corr-1", CancellationToken.None);

        _transition!.NewStatus.Should().Be(SubscriptionStatus.Canceled);
        _transition.EndedAtUtc.Should().NotBeNull();
        _transition.CancellationReason.Should().Be("fraud");
        result.Value!.Status.Should().Be(nameof(SubscriptionStatus.Canceled));
    }

    [Fact]
    public async Task An_immediate_cancellation_stops_the_usage_rating_sweep()
    {
        await Service().CancelAsync(
            "sub-1", immediately: true, null, "corr-1", CancellationToken.None);

        _transition!.ClearNextUsageBillingAt.Should().BeTrue(
            "nothing more will be metered once entitlement stops immediately");
    }

    [Fact]
    public async Task An_at_period_end_cancellation_leaves_usage_rating_untouched()
    {
        await Service().CancelAsync(
            "sub-1", immediately: false, null, "corr-1", CancellationToken.None);

        _transition!.ClearNextUsageBillingAt.Should().BeFalse(
            "the subscription keeps granting and metering until the period actually ends");
    }

    [Fact]
    public async Task Cancelling_drops_the_cached_entitlement_immediately()
    {
        await Service().CancelAsync(
            "sub-1", immediately: true, null, "corr-1", CancellationToken.None);

        _cache.Verify(
            cache => cache.Invalidate(TenantId, OrganizationId),
            Times.Once,
            "the cached snapshot decides what the customer may do");
    }

    [Fact]
    public async Task Cancelling_raises_an_event()
    {
        await Service().CancelAsync(
            "sub-1", immediately: false, null, "corr-1", CancellationToken.None);

        _transition!.Event!.EventType.Should()
            .Be(SubscriptionConstants.SubscriptionCancellationRequested);
        _transition.Event.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task Another_organizations_subscription_reports_as_missing()
    {
        _subscription = null;

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound,
            "a forbidden response would confirm the identifier exists somewhere else");
    }

    [Fact]
    public async Task Cancelling_an_ended_subscription_is_a_conflict()
    {
        _subscription!.Status = SubscriptionStatus.Canceled;

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, "corr-1", CancellationToken.None);

        result.ErrorCode.Should().Be("subscription_already_ended");
    }

    [Fact]
    public async Task Losing_the_transition_race_is_reported_as_a_conflict()
    {
        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                "sub-1",
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        _cache.Verify(
            cache => cache.Invalidate(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task A_trialing_subscription_can_be_cancelled_too()
    {
        _subscription!.Status = SubscriptionStatus.Trialing;

        var result = await Service().CancelAsync(
            "sub-1", immediately: false, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _transition!.ExpectedStatus.Should().Be(SubscriptionStatus.Trialing);
    }

    private SubscriptionCancellationService Service() => new(
        _subscriptions.Object,
        _contextResolver.Object,
        new SubscriptionOutboxEventFactory(),
        new SubscriptionResponseMapper(),
        _cache.Object,
        NullLogger<SubscriptionCancellationService>.Instance,
        _time);

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        CurrentPeriodEndUtc = new DateTime(2026, 8, 31, 21, 59, 59, DateTimeKind.Utc),
        NextFeeBillingAtUtc = new DateTime(2026, 8, 31, 21, 59, 59, DateTimeKind.Utc),
        Plan = new PlanSnapshot { Code = "professional" },
        Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 8900 }
    };
}
