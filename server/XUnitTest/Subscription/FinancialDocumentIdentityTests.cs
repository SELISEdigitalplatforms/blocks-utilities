using FluentAssertions;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// The values that make issuing a document exactly-once, and reading a charge back possible.
/// </summary>
/// <remarks>
/// Small, cheap tests over the two functions everything else in the feature trusts. If a source key
/// can collide, a subscriber gets one document for two events; if it is not reproducible, recovery
/// issues a second document for one event. Both are worse than any bug further up.
/// </remarks>
public sealed class FinancialDocumentIdentityTests
{
    [Fact]
    public void Every_kind_of_source_produces_a_key_no_other_kind_can()
    {
        // Deliberately fed the same identifier to all four. A shared id must not become a shared key:
        // one collision here folds a refund's credit note into the invoice it credits.
        var keys = new[]
        {
            FinancialDocumentSourceKey.ForPayment("same-id"),
            FinancialDocumentSourceKey.ForRefund("same-id"),
            FinancialDocumentSourceKey.ForDowngradeCredit("same-id", "same-id"),
            FinancialDocumentSourceKey.ForTrial("same-id", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        };

        keys.Distinct(StringComparer.Ordinal).Should().HaveCount(4);
    }

    [Fact]
    public void The_same_source_produces_the_same_key_on_every_call()
    {
        // What makes recovery possible at all: a sweep running months later, in another process, has
        // to compute the name the money path would have written.
        var trialStart = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        FinancialDocumentSourceKey.ForTrial("sub-1", trialStart)
            .Should().Be(FinancialDocumentSourceKey.ForTrial("sub-1", trialStart));
        FinancialDocumentSourceKey.ForPayment("pay-1")
            .Should().Be(FinancialDocumentSourceKey.ForPayment("pay-1"));
    }

    [Fact]
    public void Two_trials_on_one_subscription_are_two_documents()
    {
        // A subscription can trial more than once over its life — a re-subscribe, a trial granted
        // again by support. Keyed on the subscription alone, the second would silently reuse the
        // first document and the subscriber would never be told the new terms.
        var first = FinancialDocumentSourceKey.ForTrial(
            "sub-1",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var second = FinancialDocumentSourceKey.ForTrial(
            "sub-1",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        first.Should().NotBe(second);
    }

    [Fact]
    public void A_trial_instant_is_spelled_the_same_whatever_kind_the_DateTime_claims()
    {
        // Local and unspecified kinds reach this from deserialized documents. Two spellings of one
        // instant would be two keys for one trial.
        var utc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

        FinancialDocumentSourceKey.ForTrial("sub-1", utc)
            .Should().Be(FinancialDocumentSourceKey.ForTrial("sub-1", unspecified));
    }

    [Fact]
    public void An_empty_identifier_is_refused_rather_than_producing_a_key_everything_shares()
    {
        // The failure mode this guards: a blank id yields "payment:" for every payment, so the second
        // document ever issued collides with the first and is silently dropped.
        var act = () => FinancialDocumentSourceKey.ForPayment("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(SubscriptionChargeKind.Renewal)]
    [InlineData(SubscriptionChargeKind.PlanChange)]
    [InlineData(SubscriptionChargeKind.QuantityChange)]
    [InlineData(SubscriptionChargeKind.Usage)]
    [InlineData(SubscriptionChargeKind.Initial)]
    public void Every_order_id_this_module_writes_reads_back_as_the_kind_that_wrote_it(
        SubscriptionChargeKind expected)
    {
        // Built from the real helpers rather than hand-written strings, so a change to the format
        // cannot leave the reader and the writer disagreeing while both tests still pass.
        var orderId = expected switch
        {
            SubscriptionChargeKind.Renewal =>
                SubscriptionConstants.RenewalOrderIdFor("sub-1", "M20260801T000000Z"),
            SubscriptionChargeKind.PlanChange => SubscriptionConstants.SettlementOrderIdFor(
                "sub-1", SettlementReservationKind.PlanChange, "res-1"),
            SubscriptionChargeKind.QuantityChange => SubscriptionConstants.SettlementOrderIdFor(
                "sub-1", SettlementReservationKind.QuantityIncrease, "res-1"),
            SubscriptionChargeKind.Usage =>
                SubscriptionConstants.UsageInvoiceOrderIdFor("sub-1", "M20260801T000000Z"),
            _ => SubscriptionConstants.OrderIdFor("sub-1")
        };

        var reference = SubscriptionOrderId.Parse(orderId);

        reference.Kind.Should().Be(expected);
        reference.SubscriptionId.Should().Be("sub-1");
    }

    [Fact]
    public void A_renewal_and_a_usage_charge_carry_their_period_key_and_a_settlement_does_not()
    {
        // The defect this reader was extracted to prevent: a settlement's suffix is a reservation id,
        // and reporting it as a period key put a reservation id where a client expected a period.
        SubscriptionOrderId.Parse(
                SubscriptionConstants.RenewalOrderIdFor("sub-1", "M20260801T000000Z"))
            .PeriodKey.Should().Be("M20260801T000000Z");

        SubscriptionOrderId.Parse(
                SubscriptionConstants.UsageInvoiceOrderIdFor("sub-1", "M20260801T000000Z"))
            .PeriodKey.Should().Be("M20260801T000000Z");

        SubscriptionOrderId.Parse(
                SubscriptionConstants.SettlementOrderIdFor(
                    "sub-1", SettlementReservationKind.PlanChange, "res-1"))
            .PeriodKey.Should().BeNull();
    }

    [Fact]
    public void Both_legacy_settlement_spellings_are_still_read()
    {
        // Settled payments, and settled financial records do not get rewritten to tidy up a string.
        SubscriptionOrderId.Parse(
                $"sub:sub-1:{SubscriptionConstants.LegacyPlanChangeSegment}:7")
            .Kind.Should().Be(SubscriptionChargeKind.PlanChange);

        SubscriptionOrderId.Parse(
                $"sub:sub-1:{SubscriptionConstants.LegacySettlementSegment}:res-3")
            .Kind.Should().Be(SubscriptionChargeKind.QuantityChange);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-ours-at-all")]
    [InlineData("sub:")]
    [InlineData("sub::something")]
    public void A_foreign_or_malformed_order_id_names_no_subscription(string? orderId)
    {
        // What keeps another product's payment in the same tenant from being invoiced as somebody's
        // subscription.
        var reference = SubscriptionOrderId.Parse(orderId);

        reference.Kind.Should().Be(SubscriptionChargeKind.Unknown);
        reference.SubscriptionId.Should().BeNull();
    }

    [Fact]
    public void A_period_key_round_trips_through_its_own_encoding()
    {
        // The document's service period is derived from this, so a key that cannot be read back is a
        // document that has to fall back to whichever period the subscription happens to be in now.
        var start = new DateTime(2026, 8, 1, 6, 30, 0, DateTimeKind.Utc);
        var key = PeriodKey.Create(BillingInterval.Month, start);

        PeriodKey.TryDecodeStart(key, out var decoded).Should().BeTrue();
        decoded.Should().Be(start);
        decoded.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("M20260801T000000")]
    [InlineData("res-1")]
    [InlineData("Mnotadatetime00Z")]
    public void An_unrecognised_period_key_is_declined_rather_than_guessed(string? key)
    {
        PeriodKey.TryDecodeStart(key, out _).Should().BeFalse();
    }
}
