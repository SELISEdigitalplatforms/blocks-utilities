using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Services;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// A subscription as its owner reads it back.
/// </summary>
/// <remarks>
/// What matters here is what the ordinary read carries. A quantity change that is only visible in
/// the response to the request that made it is invisible after a page reload, which leaves a
/// client showing the quantity in force with nothing to say a different one is already booked.
/// </remarks>
public sealed class SubscriptionResponseMapperTests
{
    private static readonly DateTime PeriodStart = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SubscriptionResponseMapper _mapper = new(
        new ControlledTimeProvider(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void A_scheduled_decrease_is_carried_on_the_ordinary_read()
    {
        var subscription = NewSubscription(10);
        subscription.PendingQuantityChange = new PendingQuantityChange
        {
            RequestedQuantities = [Item(9)],
            RequestedAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc),
            EffectiveAtUtc = PeriodEnd
        };

        var response = _mapper.ToResponse(subscription);

        response.Quantities.Single().Quantity.Should().Be(10, "the paid quantity still stands");
        response.PendingQuantityChange!.Quantities.Single().Quantity.Should().Be(9);
        response.PendingQuantityChange.EffectiveAtUtc.Should().Be(PeriodEnd);
    }

    [Fact]
    public void A_subscription_with_nothing_scheduled_says_so()
    {
        var response = _mapper.ToResponse(NewSubscription(10));

        response.PendingQuantityChange.Should().BeNull();
    }

    [Fact]
    public void The_band_and_the_recurring_amount_are_stated_rather_than_left_to_be_derived()
    {
        var response = _mapper.ToResponse(NewSubscription(10));

        // 10 users at CHF 145 with the 10% band: CHF 1,305.00.
        response.CurrentTier!.DiscountBasisPoints.Should().Be(1_000);
        response.RecurringAmountMinor.Should().Be(130_500);
        response.UnitAmountMinor.Should().Be(
            14_500,
            "the unit amount stays what the price says, undiscounted");
    }

    [Fact]
    public void A_subscription_nobody_has_cancelled_carries_no_cancellation()
    {
        var response = _mapper.ToResponse(NewSubscription(10));

        response.Cancellation.Should().BeNull();
        response.CancelAtPeriodEnd.Should().BeFalse();
        response.CanceledAtUtc.Should().BeNull();
    }

    [Fact]
    public void A_scheduled_cancellation_reports_the_request_and_period_end_separately()
    {
        var subscription = NewSubscription(10);
        subscription.CancelAtPeriodEnd = true;
        subscription.CanCancelImmediately = true;
        subscription.CanceledAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc);

        var response = _mapper.ToResponse(subscription);

        response.Cancellation.Should().NotBeNull();
        response.Cancellation!.State.Should().Be("Scheduled");
        response.Cancellation.RequestedAtUtc.Should().Be(subscription.CanceledAtUtc.Value);
        response.Cancellation.EffectiveAtUtc.Should().Be(PeriodEnd,
            "access continues to the paid period's end while the cancellation is only scheduled");
        response.Cancellation.CanCancelImmediately.Should().BeTrue();

        // Legacy fields keep reporting the same facts for clients that have not moved over.
        response.CancelAtPeriodEnd.Should().BeTrue();
        response.CanceledAtUtc.Should().Be(subscription.CanceledAtUtc);
    }

    [Fact]
    public void A_schedule_locked_to_a_prepaid_annual_term_reports_that_it_cannot_be_escalated()
    {
        var subscription = NewSubscription(10);
        subscription.CancelAtPeriodEnd = true;
        subscription.CanCancelImmediately = false;
        subscription.CanceledAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc);

        var response = _mapper.ToResponse(subscription);

        response.Cancellation!.CanCancelImmediately.Should().BeFalse();
    }

    [Fact]
    public void An_effective_cancellation_reports_when_access_actually_ended()
    {
        var subscription = NewSubscription(10);
        subscription.Status = SubscriptionStatus.Canceled;
        subscription.CanceledAtUtc = new DateTime(2026, 8, 16, 11, 0, 0, DateTimeKind.Utc);
        subscription.EndedAtUtc = new DateTime(2026, 8, 16, 11, 5, 0, DateTimeKind.Utc);

        var response = _mapper.ToResponse(subscription);

        response.Cancellation!.State.Should().Be("Effective");
        response.Cancellation.EffectiveAtUtc.Should().Be(subscription.EndedAtUtc.Value,
            "once cancellation has taken effect, EffectiveAtUtc is when it actually did — not " +
            "the period boundary it would otherwise have waited for");
    }

    private static SubscriptionQuantityItem Item(long quantity) => new()
    {
        ItemKey = "user",
        UnitLabel = "user",
        Quantity = quantity,
        UnitAmountMinor = 14_500
    };

    private static SubscriptionDetail NewSubscription(long quantity) => new()
    {
        ItemId = "sub-1",
        TenantId = "tenant-1",
        OrganizationId = "org-1",
        Status = SubscriptionStatus.Active,
        Version = 7,
        CurrencyCode = "CHF",
        CurrentPeriodStartUtc = PeriodStart,
        CurrentPeriodEndUtc = PeriodEnd,
        QuantityItems = [Item(quantity)],
        Price = new PriceSnapshot
        {
            UnitAmountMinor = 14_500,
            CurrencyCode = "CHF",
            QuantityItemKey = "user",
            Interval = BillingInterval.Month,
            IntervalCount = 1
        },
        Plan = new PlanSnapshot
        {
            Code = "team",
            DisplayName = "Team",
            QuantityItems =
            [
                new PlanQuantityItem
                {
                    ItemKey = "user",
                    UnitLabel = "user",
                    MinQuantity = 1,
                    QuantityDiscountTiers =
                    [
                        new QuantityDiscountTier
                        {
                            MinimumQuantity = 1,
                            MaximumQuantity = 9,
                            DiscountBasisPoints = 0
                        },
                        new QuantityDiscountTier
                        {
                            MinimumQuantity = 10,
                            MaximumQuantity = null,
                            DiscountBasisPoints = 1_000
                        }
                    ]
                }
            ]
        }
    };
}
