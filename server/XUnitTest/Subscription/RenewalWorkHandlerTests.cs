using FluentAssertions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Renewing what a work item names, and deciding whether it is still worth renewing.
/// </summary>
/// <remarks>
/// The scheduling document lives in another database with no transaction joining the two, so it is
/// a hint about the past rather than a statement about now. Everything here is about the handler
/// treating it that way: the subscription may have been cancelled, may already have renewed, may
/// not exist at all.
/// </remarks>
public sealed class RenewalWorkHandlerTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<ISubscriptionRenewalProcessor> _sweep = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionRenewalService> _renewals = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail? _stored = NewSubscription();

    public RenewalWorkHandlerTests() =>
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _stored);

    [Fact]
    public async Task Work_naming_no_subscription_falls_back_to_the_tenant_sweep()
    {
        // What the repair sweep schedules: it exists to find work nobody named.
        var outcome = await Handler().ExecuteAsync(Work(aggregateId: ""), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _sweep.Verify(sweep => sweep.ProcessDueAsync(TenantId, It.IsAny<CancellationToken>()), Times.Once);
        _renewals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Work_naming_a_due_subscription_renews_that_one_and_nothing_else()
    {
        var outcome = await Handler().ExecuteAsync(Work(), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _renewals.Verify(
            renewals => renewals.RenewAsync(
                It.Is<SubscriptionDetail>(subscription => subscription.ItemId == "sub-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // The point of naming the subscription: no pass over everything else the tenant owns.
        _sweep.Verify(
            sweep => sweep.ProcessDueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_subscription_that_no_longer_exists_is_dead_lettered_rather_than_retried()
    {
        _stored = null;

        var outcome = await Handler().ExecuteAsync(Work(), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Permanent);
        outcome.ErrorCode.Should().Be("subscription_not_found");
    }

    [Theory]
    [InlineData(SubscriptionStatus.Canceled)]
    [InlineData(SubscriptionStatus.Unpaid)]
    [InlineData(SubscriptionStatus.Incomplete)]
    [InlineData(SubscriptionStatus.Trialing)]
    public async Task A_subscription_the_state_moved_past_is_finished_without_renewing(
        SubscriptionStatus status)
    {
        // Not a failure. The item was scheduled when renewing made sense and no longer does, which
        // is the ordinary consequence of two databases that cannot be written together.
        _stored!.Status = status;

        var outcome = await Handler().ExecuteAsync(Work(), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _renewals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_subscription_already_renewed_by_something_else_is_left_alone()
    {
        // The sweep got there first, so the next fee instant has moved into the future.
        _stored!.NextFeeBillingAtUtc = _time.GetUtcNow().UtcDateTime.AddDays(30);

        var outcome = await Handler().ExecuteAsync(Work(), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _renewals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_subscription_with_no_next_fee_instant_is_left_alone()
    {
        // Cancellation pending clears it. Charging on the strength of a stale scheduling document
        // would bill somebody who has already been told they will not be.
        _stored!.NextFeeBillingAtUtc = null;

        var outcome = await Handler().ExecuteAsync(Work(), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _renewals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_past_due_subscription_is_still_renewable_so_dunning_can_retry()
    {
        _stored!.Status = SubscriptionStatus.PastDue;

        await Handler().ExecuteAsync(Work(), default);

        _renewals.Verify(
            renewals => renewals.RenewAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private RenewalWorkHandler Handler() => new(
        _sweep.Object,
        _subscriptions.Object,
        _renewals.Object,
        _time);

    private static SubscriptionBackgroundWork Work(string aggregateId = "sub-1") => new()
    {
        ItemId = "work-1",
        TenantId = TenantId,
        AggregateId = aggregateId,
        WorkType = SubscriptionWorkType.Renewal,
        WorkKey = "renewal:M20260901T000000Z",
        DueAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        CorrelationId = "corr-1"
    };

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = "org-1",
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        NextFeeBillingAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        Plan = new PlanSnapshot { Code = "professional" },
        Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 8_900 }
    };
}
