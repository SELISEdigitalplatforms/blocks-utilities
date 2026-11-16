using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The writes offline subscriptions depend on, against a real database.
/// </summary>
/// <remarks>
/// Guards an invoiced customer's overage from being settled twice or settled as if money moved.
/// That is enforced by the update itself, which only the real collection can show.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class OfflineSubscriptionIntegrationTests
{
    private readonly MongoIntegrationFixture _fixture;
    private readonly SubscriptionUsageInvoiceRepository _usageInvoices;

    public OfflineSubscriptionIntegrationTests(MongoIntegrationFixture fixture)
    {
        _fixture = fixture;
        _usageInvoices = new SubscriptionUsageInvoiceRepository(fixture.DbContextProvider);
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

        // Inserted directly rather than through TryCreateAsync, which builds the collection's unique
        // index across the whole shared fixture database — and the reporting tests deliberately
        // leave rows there that such a build refuses. This test is about settling, not creating.
        await _fixture.Collection<SubscriptionUsageInvoice>("SubscriptionUsageInvoices")
            .InsertOneAsync(invoice, cancellationToken: CancellationToken.None);

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
}
