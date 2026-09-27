using FluentAssertions;
using Subscription.DomainService.Entities;

namespace XUnitTest.Subscription;

/// <summary>
/// That a seat's usage counts against a window of its own.
/// </summary>
/// <remarks>
/// This is what makes a per-seat allowance mean anything. Five seats on a plan including ten
/// million tokens is ten million each, because each seat addresses a different counter against the
/// same included quantity — one person cannot spend what the other four were bought.
/// <para>
/// The identity is also what stops the allowance being minted. It names the seat, not the person,
/// so releasing somebody who has spent their window and assigning somebody else returns to the same
/// counter rather than a fresh one.
/// </para>
/// </remarks>
public sealed class PerSeatCounterTests
{
    private const string SubscriptionId = "sub-1";
    private const string MeterKey = "ai_tokens";
    private const string PeriodKey = "M20260901T000000Z";

    [Fact]
    public void Two_seats_on_one_subscription_count_separately()
    {
        SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, PeriodKey, 1)
            .Should().NotBe(
                SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, PeriodKey, 2),
                because: "sharing a counter is sharing an allowance, and the seats were bought " +
                         "one per person");
    }

    [Fact]
    public void Whoever_takes_a_seat_next_returns_to_the_same_counter()
    {
        SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, PeriodKey, 3)
            .Should().Be(
                SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, PeriodKey, 3),
                because: "the identity names the seat rather than the person, so cycling people " +
                         "through one paid seat mints no allowance");
    }

    /// <remarks>
    /// The reason this change needs no migration. Every counter already stored was written for a
    /// subscription with no seats, and composing a null seat has to reproduce its identity exactly
    /// or those balances would be orphaned and every organization would appear to start afresh.
    /// </remarks>
    [Fact]
    public void A_subscription_with_no_seats_keeps_the_identity_it_already_had()
    {
        SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, PeriodKey, seatNumber: null)
            .Should().Be(SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, PeriodKey));
    }

    [Fact]
    public void A_seated_counter_is_never_mistaken_for_the_subscriptions_own()
    {
        SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, PeriodKey, 1)
            .Should().NotBe(SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, PeriodKey),
                because: "an organization's own usage and one person's are different pools, and " +
                         "a collision would let a seat spend what the organization shares");
    }

    [Fact]
    public void A_seats_windows_are_told_apart_by_period()
    {
        SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, "M20260901T000000Z", 1)
            .Should().NotBe(
                SubscriptionUsageCounter.CreateId(SubscriptionId, MeterKey, "M20261001T000000Z", 1),
                because: "a seat's allowance renews with the period, and one counter spanning " +
                         "both would carry September's usage into October");
    }
}
