using FluentAssertions;
using Subscription.DomainService.Enums;

namespace XUnitTest.Subscription;

/// <summary>
/// Enum values are persisted, so their numbers are part of the storage format.
/// </summary>
/// <remarks>
/// Inserting a member without an explicit value renumbers everything after it, and every
/// document already written then means something else. Nothing throws — a canceled subscription
/// simply reads back as active. These tests pin the numbers so that change cannot pass review
/// silently.
/// </remarks>
public sealed class SubscriptionEnumStabilityTests
{
    [Fact]
    public void Subscription_status_numbers_are_fixed()
    {
        ((int)SubscriptionStatus.Incomplete).Should().Be(0);
        ((int)SubscriptionStatus.IncompleteExpired).Should().Be(1);
        ((int)SubscriptionStatus.Trialing).Should().Be(2);
        ((int)SubscriptionStatus.Active).Should().Be(3);
        ((int)SubscriptionStatus.PastDue).Should().Be(4);
        ((int)SubscriptionStatus.Unpaid).Should().Be(5);
        ((int)SubscriptionStatus.Canceled).Should().Be(6);
    }

    [Fact]
    public void Billing_interval_numbers_are_fixed()
    {
        ((int)BillingInterval.Day).Should().Be(0);
        ((int)BillingInterval.Week).Should().Be(1);
        ((int)BillingInterval.Month).Should().Be(2);
        ((int)BillingInterval.Year).Should().Be(3);
    }

    [Fact]
    public void Usage_entry_type_numbers_are_fixed()
    {
        ((int)UsageEntryType.Consumption).Should().Be(0);
        ((int)UsageEntryType.Reversal).Should().Be(1);
        ((int)UsageEntryType.Grant).Should().Be(2);
    }

    [Fact]
    public void Payment_link_state_numbers_are_fixed()
    {
        ((int)SubscriptionPaymentLinkState.Pending).Should().Be(0);
        ((int)SubscriptionPaymentLinkState.Applied).Should().Be(1);
        ((int)SubscriptionPaymentLinkState.Abandoned).Should().Be(2);
    }

    [Theory]
    [InlineData(typeof(SubscriptionStatus))]
    [InlineData(typeof(BillingInterval))]
    [InlineData(typeof(CatalogueStatus))]
    [InlineData(typeof(MeterAggregation))]
    [InlineData(typeof(EntitlementLimitKind))]
    [InlineData(typeof(EntitlementReason))]
    [InlineData(typeof(UsageEntryType))]
    [InlineData(typeof(SubscriptionPaymentPurpose))]
    [InlineData(typeof(SubscriptionPaymentLinkState))]
    [InlineData(typeof(SubscriptionOutboxStatus))]
    [InlineData(typeof(DiscountKind))]
    public void Every_persisted_enum_starts_at_zero_and_has_no_gaps(Type enumType)
    {
        var values = Enum.GetValues(enumType)
            .Cast<object>()
            .Select(value => (int)value)
            .Order()
            .ToArray();

        values.Should().Equal(
            Enumerable.Range(0, values.Length).ToArray(),
            "a gap or a non-zero start usually means a member was removed rather than " +
            "retired, and its number is now free to be reused by a later addition");
    }
}
