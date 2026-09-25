using FluentAssertions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Where a seat's carried-forward allowance comes from.
/// </summary>
/// <remarks>
/// Guards handing one person another's leftovers. A carry-forward meter opens each window with
/// whatever the window before it left behind, and "before it" has to mean the same seat's — read
/// from the subscription's instead, everybody on the plan would open with the same leftovers, and
/// on a five-seat subscription that is four allowances nobody paid for.
/// <para>
/// Nothing in the type system holds this. The seat is one argument among five on a counter
/// identity, and dropping it still compiles and still returns a plausible number.
/// </para>
/// </remarks>
public sealed class PerSeatCarryForwardTests
{
    private const string TenantId = "tenant-1";
    private const string SubscriptionId = "sub-1";
    private const string MeterKey = "ai_tokens";

    private readonly Mock<ISubscriptionUsageRepository> _usage = new();

    [Fact]
    public async Task A_seat_opens_with_what_its_own_previous_window_left()
    {
        var requested = new List<string>();

        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, id, _) => requested.Add(id))
            .ReturnsAsync((SubscriptionUsageCounter?)null);

        await Resolver().OpeningAllowanceAsync(
            Subscription(), Meter(), CurrentPeriod, CancellationToken.None, seat: 3);

        requested.Should().ContainSingle().Which.Should().EndWith(":s3",
            because: "seat three opens with what seat three left behind — reading the " +
                     "subscription's window would give every seat the same leftovers");
    }

    [Fact]
    public async Task The_subscriptions_own_window_is_read_when_there_is_no_seat()
    {
        var requested = new List<string>();

        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, id, _) => requested.Add(id))
            .ReturnsAsync((SubscriptionUsageCounter?)null);

        await Resolver().OpeningAllowanceAsync(
            Subscription(), Meter(), CurrentPeriod, CancellationToken.None, seat: null);

        requested.Should().ContainSingle().Which.Should().Be(
            SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, "M20260901T000000Z"),
            because: "an organization's own subscription has no seat, and its counter keeps the " +
                     "identity it has always had — any suffix at all would orphan every balance " +
                     "already stored");
    }

    [Fact]
    public async Task One_seats_leftovers_do_not_open_anothers_window()
    {
        // Seat one spent nothing last window; seat two spent all of it.
        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.Is<string>(id => id.EndsWith(":s1")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageCounter { Balance = 0, LimitSnapshot = 100 });

        _usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.Is<string>(id => id.EndsWith(":s2")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageCounter { Balance = 100, LimitSnapshot = 100 });

        var first = await Resolver().OpeningAllowanceAsync(
            Subscription(), Meter(), CurrentPeriod, CancellationToken.None, seat: 1);

        var second = await Resolver().OpeningAllowanceAsync(
            Subscription(), Meter(), CurrentPeriod, CancellationToken.None, seat: 2);

        first.Should().BeGreaterThan(second,
            because: "the one who saved carries it forward and the one who spent does not — the " +
                     "whole point of counting them apart");
    }

    private MeterAllowanceResolver Resolver() => new(_usage.Object);

    private static BillingPeriod CurrentPeriod => new(
        2,
        new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        "M20261001T000000Z");

    private static PlanMeter Meter() => new()
    {
        MeterKey = MeterKey,
        UnitLabel = "token",
        IncludedQuantity = 100,
        ResetPolicy = MeterResetPolicy.CarryForward
    };

    private static SubscriptionDetail Subscription() => new()
    {
        ItemId = SubscriptionId,
        TenantId = TenantId,
        OrganizationId = "org-1",
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        CurrentPeriodStartUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentPeriodEndUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            AnchorDayOfMonth = 1
        },
        Plan = new PlanSnapshot
        {
            Code = "starter",
            SubscriberScope = SubscriberScope.User,
            Meters = [Meter()]
        }
    };
}
