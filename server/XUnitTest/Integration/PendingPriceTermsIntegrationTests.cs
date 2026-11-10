using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Integration;

/// <summary>
/// The parts of extending a discount or tax edit to existing subscribers that only a real database
/// can demonstrate.
/// </summary>
/// <remarks>
/// Guards against the customer-visible failures a mock would hide: a subscriber on another price
/// handed this price's terms by a write that raced their plan change, a subscriber the operator
/// meant to reach left out of the listing, a notice email scheduled without the change it announces,
/// and a renewal that installs the terms but never forgets them.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class PendingPriceTermsIntegrationTests
{
    private const string PriceId = "price-1";

    private readonly SubscriptionRepository _subscriptions;

    public PendingPriceTermsIntegrationTests(MongoIntegrationFixture fixture) =>
        _subscriptions = new SubscriptionRepository(fixture.DbContextProvider);

    [Fact]
    public async Task Every_subscriber_who_can_still_renew_on_the_price_is_listed_and_nobody_else()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var active = await GivenAsync(tenantId, SubscriptionStatus.Active);
        var unpaid = await GivenAsync(tenantId, SubscriptionStatus.Unpaid);
        await GivenAsync(tenantId, SubscriptionStatus.Canceled);
        await GivenAsync(tenantId, SubscriptionStatus.Active, priceId: "price-other");

        var listed = await _subscriptions.ListRenewableOnPriceAsync(
            tenantId, PriceId, null, 50, CancellationToken.None);

        listed.Select(subscription => subscription.ItemId).Should().BeEquivalentTo(
            [active.ItemId, unpaid.ItemId],
            "an unpaid subscriber renews on this price when they come back with a card, and an " +
            "ended one or one on another price was never sold these terms");
    }

    [Fact]
    public async Task Terms_and_their_announcement_are_written_together()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await GivenAsync(tenantId, SubscriptionStatus.Active);
        var pending = Pending();

        var written = await _subscriptions.TrySetPendingPriceTermsAsync(
            tenantId, subscription.ItemId, subscription.Version, pending,
            new SubscriptionOutboxEventFactory().CreatePriceTermsChangeScheduled(subscription, pending, "corr-1"),
            CancellationToken.None);

        written.Should().BeTrue();
        var stored = await _subscriptions.GetByIdAsync(tenantId, subscription.ItemId, CancellationToken.None);
        stored!.PendingPriceTerms!.Terms.AutomaticDiscountBasisPoints.Should().Be(1500);
        stored.PendingPriceTerms.Terms.TaxMode.Should().Be(TaxMode.Inclusive);
        stored.OutboxEvents.Should().ContainSingle(outboxEvent =>
                outboxEvent.EventType == SubscriptionConstants.SubscriptionPriceTermsChangeScheduled,
            "a subscriber must never be repriced without the email that tells them");
    }

    [Fact]
    public async Task A_subscriber_who_moved_price_meanwhile_is_not_given_these_terms()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await GivenAsync(tenantId, SubscriptionStatus.Active, priceId: "price-other");
        var pending = Pending();

        var written = await _subscriptions.TrySetPendingPriceTermsAsync(
            tenantId, subscription.ItemId, subscription.Version, pending,
            new SubscriptionOutboxEventFactory().CreatePriceTermsChangeScheduled(subscription, pending, "corr-1"),
            CancellationToken.None);

        written.Should().BeFalse("the subscriber was quoted the terms of the price they are on now");
    }

    [Fact]
    public async Task The_renewal_that_installs_the_terms_forgets_them_in_the_same_write()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = await GivenAsync(tenantId, SubscriptionStatus.Active);
        var pending = Pending();
        await _subscriptions.TrySetPendingPriceTermsAsync(
            tenantId, subscription.ItemId, subscription.Version, pending,
            new SubscriptionOutboxEventFactory().CreatePriceTermsChangeScheduled(subscription, pending, "corr-1"),
            CancellationToken.None);

        var installed = subscription.Price;
        pending.Terms.ApplyTo(installed);

        (await _subscriptions.TryTransitionAsync(
            tenantId,
            subscription.ItemId,
            new SubscriptionTransition(SubscriptionStatus.Active, SubscriptionStatus.Active)
            {
                Price = installed,
                ClearPendingPriceTerms = true
            },
            CancellationToken.None)).Should().BeTrue();

        var stored = await _subscriptions.GetByIdAsync(tenantId, subscription.ItemId, CancellationToken.None);
        stored!.Price.AutomaticDiscountBasisPoints.Should().Be(1500);
        stored.PendingPriceTerms.Should().BeNull(
            "terms left pending after being installed would be applied again at the next renewal");
    }

    private static PendingPriceTerms Pending() => new()
    {
        PriceId = PriceId,
        Terms = new PriceTerms
        {
            AutomaticDiscountBasisPoints = 1500,
            QuantityDiscountCombination = AutomaticDiscountCombination.BestDiscount,
            TaxRateBasisPoints = 810,
            TaxMode = TaxMode.Inclusive
        },
        EffectiveAtUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        RequestedAtUtc = DateTime.UtcNow,
        RequestedByName = "Operator"
    };

    private async Task<SubscriptionDetail> GivenAsync(
        string tenantId,
        SubscriptionStatus status,
        string priceId = PriceId)
    {
        var id = Guid.NewGuid().ToString();
        var subscription = new SubscriptionDetail
        {
            ItemId = id,
            TenantId = tenantId,
            // One organization each, so the one-live-subscription reservation never decides the test.
            OrganizationId = $"org-{id}",
            BillingAccountId = "acct-1",
            Status = status,
            CurrencyCode = "CHF",
            OrderId = $"sub:{id}",
            Plan = new PlanSnapshot { Code = "basic", DisplayName = "Basic" },
            Price = new PriceSnapshot
            {
                PriceId = priceId,
                CurrencyCode = "CHF",
                UnitAmountMinor = 8_900,
                Interval = BillingInterval.Month,
                IntervalCount = 1,
                AutomaticDiscountBasisPoints = 800
            }
        };

        (await _subscriptions.TryCreateAsync(subscription, CancellationToken.None)).Should().BeTrue();

        return (await _subscriptions.GetByIdAsync(tenantId, id, CancellationToken.None))!;
    }
}
