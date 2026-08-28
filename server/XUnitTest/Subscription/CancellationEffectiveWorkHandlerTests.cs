using FluentAssertions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Finishing what a targeted work item names, and falling back to the tenant sweep otherwise.
/// </summary>
/// <remarks>
/// The scheduling document lives in another database with no transaction joining the two, so it is
/// a hint about the past rather than a statement about now. The subscription may have been
/// escalated to immediate, re-cancelled, or already finished by the tenant sweep itself.
/// </remarks>
public sealed class CancellationEffectiveWorkHandlerTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<ISubscriptionCancellationEffectiveProcessor> _cancellations = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail? _stored = NewSubscription();

    public CancellationEffectiveWorkHandlerTests() =>
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _stored);

    [Fact]
    public async Task Work_naming_no_subscription_falls_back_to_the_tenant_sweep()
    {
        var outcome = await Handler().ExecuteAsync(Work(aggregateId: ""), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _cancellations.Verify(
            cancellations => cancellations.ProcessDueAsync(TenantId, It.IsAny<CancellationToken>()),
            Times.Once);
        _cancellations.Verify(
            cancellations => cancellations.TryFinalizeAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Work_naming_a_due_subscription_finalizes_that_one_and_nothing_else()
    {
        var outcome = await Handler().ExecuteAsync(Work(), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _cancellations.Verify(
            cancellations => cancellations.TryFinalizeAsync(
                It.Is<SubscriptionDetail>(subscription => subscription.ItemId == "sub-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // The point of naming the subscription: no pass over everything else the tenant owns.
        _cancellations.Verify(
            cancellations => cancellations.ProcessDueAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
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

    [Fact]
    public async Task A_subscription_already_escalated_to_immediate_is_left_alone()
    {
        // The interactive request beat this item to it — CancelAtPeriodEnd is already false,
        // whether Status moved to Canceled or a fresh cancellation replaced the schedule.
        _stored!.CancelAtPeriodEnd = false;

        var outcome = await Handler().ExecuteAsync(Work(), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _cancellations.Verify(
            cancellations => cancellations.TryFinalizeAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_boundary_not_yet_due_is_left_alone()
    {
        _stored!.CurrentPeriodEndUtc = _time.GetUtcNow().UtcDateTime.AddDays(1);

        var outcome = await Handler().ExecuteAsync(Work(), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _cancellations.Verify(
            cancellations => cancellations.TryFinalizeAsync(
                It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private CancellationEffectiveWorkHandler Handler() => new(
        _cancellations.Object,
        _subscriptions.Object,
        _time);

    private static SubscriptionBackgroundWork Work(string aggregateId = "sub-1") => new()
    {
        ItemId = "work-1",
        TenantId = TenantId,
        AggregateId = aggregateId,
        WorkType = SubscriptionWorkType.CancellationEffective,
        WorkKey = "cancellation-effective:sub-1:1",
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
        CancelAtPeriodEnd = true,
        CanCancelImmediately = true,
        CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        Plan = new PlanSnapshot { Code = "professional" },
        Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 8_900 }
    };
}
