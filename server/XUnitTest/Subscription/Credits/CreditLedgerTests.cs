using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;

namespace XUnitTest.Subscription.Credits;

/// <summary>
/// The rules the credit grant ledger is read by: which grants can still be spent, and in what
/// order (spec 002, AC-13).
/// </summary>
/// <remarks>
/// Guards against a subscriber losing credits they paid for: spending the credits that never
/// expire while ones that expire tonight sit unused, or drawing on a grant that has already
/// expired or been forfeited.
/// </remarks>
public sealed class CreditLedgerTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Credits_that_expire_with_the_window_are_spent_before_credits_that_never_expire()
    {
        var never = Grant("never", MeterResetPolicy.Never, grantedAt: Now.AddDays(-30));
        var window = Grant("window", MeterResetPolicy.Periodic, expiresAt: Now.AddDays(5), grantedAt: Now);

        SubscriptionCreditGrant.InConsumptionOrder([never, window])
            .Select(grant => grant.ItemId)
            .Should().Equal(["window", "never"],
                "spending the never-expiring credits first would let the dated ones lapse unused");
    }

    [Fact]
    public void Carry_forward_credits_are_spent_before_never_expiring_ones()
    {
        var never = Grant("never", MeterResetPolicy.Never, grantedAt: Now.AddDays(-30));
        var carried = Grant("carried", MeterResetPolicy.CarryForward, grantedAt: Now);

        SubscriptionCreditGrant.InConsumptionOrder([never, carried])
            .Select(grant => grant.ItemId)
            .Should().Equal(["carried", "never"],
                "carry-forward credits are forfeited on the next plan change and never-expiring ones are not");
    }

    [Fact]
    public void Among_equal_expiry_the_oldest_grant_is_spent_first()
    {
        var newer = Grant("newer", MeterResetPolicy.Never, grantedAt: Now);
        var older = Grant("older", MeterResetPolicy.Never, grantedAt: Now.AddDays(-1));

        SubscriptionCreditGrant.InConsumptionOrder([newer, older])
            .Select(grant => grant.ItemId)
            .Should().Equal(["older", "newer"]);
    }

    [Fact]
    public void An_expired_grant_cannot_be_spent()
    {
        Grant("g", MeterResetPolicy.Periodic, expiresAt: Now).IsLiveAt(Now).Should().BeFalse(
            "a window's credits end exactly when the window does (AC-14)");
    }

    [Fact]
    public void A_forfeited_grant_cannot_be_spent_even_with_credits_left()
    {
        var grant = Grant("g", MeterResetPolicy.Never);
        grant.Status = CreditGrantStatus.Forfeited;

        grant.IsLiveAt(Now).Should().BeFalse();
    }

    [Fact]
    public void A_retry_of_one_purchase_addresses_the_same_record()
    {
        SubscriptionCreditPurchase.CreateId("sub-1", "key-1")
            .Should().Be(SubscriptionCreditPurchase.CreateId("sub-1", "key-1"),
                "the record's id is what stops a retried request from charging twice");
        SubscriptionCreditPurchase.CreateId("sub-2", "key-1")
            .Should().NotBe(SubscriptionCreditPurchase.CreateId("sub-1", "key-1"),
                "one client key reused on another subscription is a different purchase");
    }

    private static SubscriptionCreditGrant Grant(
        string id,
        MeterResetPolicy policy,
        DateTime? expiresAt = null,
        DateTime? grantedAt = null) => new()
    {
        ItemId = id,
        ExpiryPolicy = policy,
        ExpiresAtUtc = expiresAt,
        GrantedAtUtc = grantedAt ?? Now,
        Quantity = 10,
        Remaining = 10,
        Status = CreditGrantStatus.Active
    };
}
