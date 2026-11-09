using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The writes offline subscriptions (spec 006) depend on, against a real database.
/// </summary>
/// <remarks>
/// Guards two ways a bank-transfer customer's record goes wrong: an overage invoice settled twice
/// or settled as if money moved, and a contract price installed without the record saying it is
/// one. Both are enforced by the update itself, which only the real collection can show.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class OfflineSubscriptionIntegrationTests
{
    private readonly SubscriptionUsageInvoiceRepository _usageInvoices;
    private readonly SubscriptionRepository _subscriptions;

    public OfflineSubscriptionIntegrationTests(MongoIntegrationFixture fixture)
    {
        _usageInvoices = new SubscriptionUsageInvoiceRepository(fixture.DbContextProvider);
        _subscriptions = new SubscriptionRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task An_offline_overage_is_settled_as_recorded_once_and_never_as_charged()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var invoice = new SubscriptionUsageInvoice
        {
            TenantId = tenantId,
            OrganizationId = "org-offline",
            SubscriptionId = "sub-offline",
            PeriodKey = "M20260801T000000Z",
            CurrencyCode = "CHF",
            TotalAmountMinor = 2_000,
            State = SubscriptionUsageInvoiceState.Pending,
            NextAttemptAtUtc = DateTime.UtcNow
        };

        (await _usageInvoices.TryCreateAsync(invoice, CancellationToken.None)).Should().BeTrue();

        (await _usageInvoices.TryMarkRecordedOfflineAsync(tenantId, invoice.ItemId, CancellationToken.None))
            .Should().BeTrue();
        (await _usageInvoices.TryMarkRecordedOfflineAsync(tenantId, invoice.ItemId, CancellationToken.None))
            .Should().BeFalse(because: "a second worker must find it already settled");
        (await _usageInvoices.TryMarkChargedAsync(tenantId, invoice.ItemId, "pay-1", CancellationToken.None))
            .Should().BeFalse(because: "a recorded invoice must never later be claimed as charged");

        var stored = await _usageInvoices.GetAsync(
            tenantId, "sub-offline", "M20260801T000000Z", CancellationToken.None);

        stored!.State.Should().Be(SubscriptionUsageInvoiceState.RecordedOffline);
        stored.NextAttemptAtUtc.Should().BeNull(because: "the charge sweep must never pick it up again");
    }

    [Fact]
    public async Task A_transition_installs_the_pricing_record_with_the_price_it_describes()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var subscription = new SubscriptionDetail
        {
            TenantId = tenantId,
            OrganizationId = "org-offline",
            Status = SubscriptionStatus.Active,
            CollectionMethod = SubscriptionCollectionMethod.OfflineInvoice,
            OfflinePricing = new OfflinePricing { Mode = OfflinePricingMode.Catalogue },
            CurrencyCode = "CHF",
            Plan = new PlanSnapshot { Code = "pro", DisplayName = "Pro" },
            Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 8_900 }
        };

        await _subscriptions.TryCreateAsync(subscription, CancellationToken.None);

        (await _subscriptions.TryTransitionAsync(
            tenantId,
            subscription.ItemId,
            new SubscriptionTransition(SubscriptionStatus.Active, SubscriptionStatus.Active)
            {
                Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 12_000 },
                OfflinePricing = new OfflinePricing
                {
                    Mode = OfflinePricingMode.Contract,
                    ContractAmountMinor = 12_000,
                    ContractCurrencyCode = "CHF"
                }
            },
            CancellationToken.None)).Should().BeTrue();

        var stored = await _subscriptions.GetByIdAsync(tenantId, subscription.ItemId, CancellationToken.None);

        stored!.Price.UnitAmountMinor.Should().Be(12_000);
        stored.OfflinePricing!.Mode.Should().Be(OfflinePricingMode.Contract);
        stored.OfflinePricing.ContractAmountMinor.Should().Be(
            12_000,
            because: "the record and the price it describes are written together or not at all");
        stored.CollectionMethod.Should().Be(SubscriptionCollectionMethod.OfflineInvoice);
    }
}
