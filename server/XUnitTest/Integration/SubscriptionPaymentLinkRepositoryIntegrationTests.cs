using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The ordering the card-setup endpoint decides from. Once a card has been replaced, a completed
/// link and its replacement coexist for one subscription, and only a sorted read on the real
/// collection proves the newest one comes back rather than whichever MongoDB stores first.
/// </summary>
[Collection(MongoIntegrationCollection.Name)]
public sealed class SubscriptionPaymentLinkRepositoryIntegrationTests
{
    private readonly SubscriptionPaymentLinkRepository _links;

    public SubscriptionPaymentLinkRepositoryIntegrationTests(MongoIntegrationFixture fixture) =>
        _links = new SubscriptionPaymentLinkRepository(fixture.DbContextProvider);

    private static SubscriptionPaymentLink NewLink(
        string tenantId,
        string subscriptionId,
        SubscriptionPaymentPurpose purpose,
        SubscriptionPaymentLinkState state,
        DateTime createdAtUtc) => new()
    {
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = subscriptionId,
        PaymentDetailId = Guid.NewGuid().ToString(),
        OrderId = $"sub:{subscriptionId}",
        Purpose = purpose,
        State = state,
        CreatedAtUtc = createdAtUtc
    };

    [Fact]
    public async Task The_newest_setup_link_is_returned_whatever_else_the_subscription_holds()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var t0 = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var newest = NewLink(tenantId, "sub-1", SubscriptionPaymentPurpose.PaymentMethodSetup,
            SubscriptionPaymentLinkState.Pending, t0.AddDays(2));

        // Inserted newest first, so an unordered first match would be wrong.
        await _links.TryCreateAsync(newest, CancellationToken.None);
        await _links.TryCreateAsync(NewLink(tenantId, "sub-1", SubscriptionPaymentPurpose.PaymentMethodSetup,
            SubscriptionPaymentLinkState.Applied, t0.AddDays(1)), CancellationToken.None);
        await _links.TryCreateAsync(NewLink(tenantId, "sub-1", SubscriptionPaymentPurpose.InitialCharge,
            SubscriptionPaymentLinkState.Applied, t0), CancellationToken.None);

        var latest = await _links.FindLatestSetupLinkAsync(tenantId, "sub-1", CancellationToken.None);

        latest.Should().NotBeNull();
        latest!.ItemId.Should().Be(newest.ItemId);
        latest.State.Should().Be(SubscriptionPaymentLinkState.Pending);
    }

    [Fact]
    public async Task A_subscription_with_only_a_charge_link_has_no_latest_setup()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await _links.TryCreateAsync(NewLink(tenantId, "sub-2", SubscriptionPaymentPurpose.InitialCharge,
            SubscriptionPaymentLinkState.Applied, DateTime.UtcNow), CancellationToken.None);

        (await _links.FindLatestSetupLinkAsync(tenantId, "sub-2", CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task Another_tenants_setup_link_is_never_returned()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var otherTenant = MongoIntegrationFixture.NewTenantId();
        await _links.TryCreateAsync(NewLink(otherTenant, "sub-3", SubscriptionPaymentPurpose.PaymentMethodSetup,
            SubscriptionPaymentLinkState.Applied, DateTime.UtcNow), CancellationToken.None);

        (await _links.FindLatestSetupLinkAsync(tenantId, "sub-3", CancellationToken.None))
            .Should().BeNull();
    }
}
