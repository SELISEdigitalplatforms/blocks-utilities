using FluentAssertions;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// The idempotency keys every subscription charge is raised under.
/// </summary>
/// <remarks>
/// The regression these guard: the keys read as "sub-init:{id}" and the payment module refuses
/// any idempotency key that does not parse as a UUID. Every charge this module raises — the
/// first one, every renewal, every dunning retry, every plan change and every overage invoice —
/// was rejected before it reached a provider. Nothing caught it because the subscription tests
/// mock the payment service, so the format rule sat on the far side of a boundary neither side
/// tested.
/// </remarks>
public sealed class SubscriptionConstantsTests
{
    private const string SubscriptionId = "8a7b6c5d-4e3f-2a1b-0c9d-8e7f6a5b4c3d";
    private const string PeriodKey = "M20260901T000000Z";

    public static TheoryData<string, string> EveryKey() => new()
    {
        { "initial charge", SubscriptionConstants.InitialChargeKeyFor(SubscriptionId) },
        { "renewal", SubscriptionConstants.RenewalKeyFor(SubscriptionId, PeriodKey, 1) },
        { "plan change", SubscriptionConstants.PlanChangeKeyFor(SubscriptionId, 3) },
        { "usage invoice", SubscriptionConstants.UsageInvoiceKeyFor(SubscriptionId, PeriodKey, 1) }
    };

    [Theory]
    [MemberData(nameof(EveryKey))]
    public void Every_charge_key_is_a_uuid_the_payment_module_will_accept(
        string description,
        string key)
    {
        Guid.TryParse(key, out _).Should().BeTrue(
            "the payment module rejects {0} keys that are not a UUID",
            description);

        key.Length.Should().BeLessThanOrEqualTo(64);
    }

    [Fact]
    public void A_key_is_the_same_every_time_it_is_derived()
    {
        SubscriptionConstants.InitialChargeKeyFor(SubscriptionId)
            .Should().Be(
                SubscriptionConstants.InitialChargeKeyFor(SubscriptionId),
                "the recovery sweep re-derives this to find a charge raised before a crash — a " +
                "key that moved would let the same period be charged twice");
    }

    [Fact]
    public void Two_subscriptions_never_share_a_key()
    {
        SubscriptionConstants.InitialChargeKeyFor(SubscriptionId)
            .Should().NotBe(SubscriptionConstants.InitialChargeKeyFor("another-subscription"));
    }

    [Fact]
    public void The_first_charge_and_a_renewal_of_one_subscription_are_separate_keys()
    {
        SubscriptionConstants.InitialChargeKeyFor(SubscriptionId)
            .Should().NotBe(
                SubscriptionConstants.RenewalKeyFor(SubscriptionId, PeriodKey, 1),
                "the readable name survives as the hash input precisely so these cannot collapse " +
                "onto one key and have the renewal replay the signup charge");
    }

    [Fact]
    public void Each_dunning_attempt_gets_its_own_key()
    {
        SubscriptionConstants.RenewalKeyFor(SubscriptionId, PeriodKey, 1)
            .Should().NotBe(
                SubscriptionConstants.RenewalKeyFor(SubscriptionId, PeriodKey, 2),
                "a retry is a new charge attempt, not a replay of the declined one");
    }

    [Fact]
    public void Each_period_gets_its_own_renewal_key()
    {
        SubscriptionConstants.RenewalKeyFor(SubscriptionId, PeriodKey, 1)
            .Should().NotBe(
                SubscriptionConstants.RenewalKeyFor(SubscriptionId, "M20261001T000000Z", 1));
    }

    [Fact]
    public void Each_plan_change_gets_its_own_key()
    {
        SubscriptionConstants.PlanChangeKeyFor(SubscriptionId, 3)
            .Should().NotBe(SubscriptionConstants.PlanChangeKeyFor(SubscriptionId, 4));
    }

    [Fact]
    public void Each_overage_attempt_gets_its_own_key()
    {
        SubscriptionConstants.UsageInvoiceKeyFor(SubscriptionId, PeriodKey, 1)
            .Should().NotBe(
                SubscriptionConstants.UsageInvoiceKeyFor(SubscriptionId, PeriodKey, 2));
    }

    /// <summary>
    /// Order ids are not UUIDs — the payment module only caps their length — so they keep the
    /// readable form that makes a subscription's charges findable by eye.
    /// </summary>
    [Fact]
    public void Order_ids_stay_readable_and_within_the_payment_modules_limit()
    {
        SubscriptionConstants.OrderIdFor(SubscriptionId)
            .Should().Be($"sub:{SubscriptionId}").And.HaveLength(40);

        SubscriptionConstants.RenewalOrderIdFor(SubscriptionId, PeriodKey)
            .Length.Should().BeLessThanOrEqualTo(80);

        SubscriptionConstants.UsageInvoiceOrderIdFor(SubscriptionId, PeriodKey)
            .Length.Should().BeLessThanOrEqualTo(80);

        SubscriptionConstants.PlanChangeOrderIdFor(SubscriptionId, 3)
            .Length.Should().BeLessThanOrEqualTo(80);
    }
}
