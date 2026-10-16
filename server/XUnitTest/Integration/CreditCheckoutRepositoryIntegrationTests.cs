using FluentAssertions;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;

namespace XUnitTest.Integration;

/// <summary>
/// The reads and the one conditional write behind checkout on first top-up (spec 003), against a
/// real MongoDB.
/// </summary>
/// <remarks>
/// Guards against a card the subscriber chose being replaced by the one a top-up checkout saved,
/// a second checkout being opened beside one still open, and a paid checkout's webhook finding no
/// purchase -- each of which only the database's own filters can show.
/// </remarks>
[Collection(MongoIntegrationCollection.Name)]
public sealed class CreditCheckoutRepositoryIntegrationTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly SubscriptionCreditPurchaseRepository _purchases;
    private readonly BillingAccountRepository _accounts;

    public CreditCheckoutRepositoryIntegrationTests(MongoIntegrationFixture fixture)
    {
        _purchases = new SubscriptionCreditPurchaseRepository(fixture.DbContextProvider);
        _accounts = new BillingAccountRepository(fixture.DbContextProvider);
    }

    [Fact]
    public async Task A_top_up_card_becomes_the_default_only_on_an_account_that_has_none()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var empty = await Account(tenantId, "org-empty", defaultCard: null);
        var chosen = await Account(tenantId, "org-chosen", defaultCard: "card-chosen");

        var adoptedOnEmpty = await _accounts.TrySetDefaultPaymentMethodIfNoneAsync(
            tenantId, empty.ItemId, "cus_1", "card-topup", "merchant-org", CancellationToken.None);
        var adoptedOnChosen = await _accounts.TrySetDefaultPaymentMethodIfNoneAsync(
            tenantId, chosen.ItemId, "cus_2", "card-topup", "merchant-org", CancellationToken.None);

        adoptedOnEmpty.Should().BeTrue();
        (await _accounts.GetAsync(tenantId, empty.ItemId, CancellationToken.None))!
            .DefaultPaymentMethodId.Should().Be("card-topup");

        adoptedOnChosen.Should().BeFalse();
        var kept = await _accounts.GetAsync(tenantId, chosen.ItemId, CancellationToken.None);
        kept!.DefaultPaymentMethodId.Should().Be("card-chosen", "a card added while the checkout was open is kept (AC-10)");
        kept.ProviderCustomerId.Should().NotBe("cus_2");
    }

    [Fact]
    public async Task Only_a_checkout_still_waiting_counts_as_open_and_a_payment_finds_its_checkout()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Insert(Purchase(tenantId, "sub-1:done", CreditPaymentPath.Checkout, CreditPurchaseStatus.Finalized, "pay-done"));
        await Insert(Purchase(tenantId, "sub-1:card", CreditPaymentPath.StoredCard, CreditPurchaseStatus.Pending, null));

        (await _purchases.FindOpenCheckoutAsync(tenantId, "sub-1", CancellationToken.None))
            .Should().BeNull("neither a finished checkout nor a stored-card purchase blocks a new checkout");

        await Insert(Purchase(tenantId, "sub-1:open", CreditPaymentPath.Checkout, CreditPurchaseStatus.AwaitingPayment, "pay-open"));

        (await _purchases.FindOpenCheckoutAsync(tenantId, "sub-1", CancellationToken.None))!
            .ItemId.Should().Be("sub-1:open");
        (await _purchases.FindOpenCheckoutAsync(tenantId, "sub-2", CancellationToken.None)).Should().BeNull();

        (await _purchases.FindCheckoutByPaymentAsync(tenantId, "pay-open", CancellationToken.None))!
            .ItemId.Should().Be("sub-1:open");
        (await _purchases.FindCheckoutByPaymentAsync(tenantId, "pay-unrelated", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Card_adoption_is_retried_only_for_recent_paid_checkouts_still_without_a_card()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        await Insert(Purchase(tenantId, "sub-1:due", CreditPaymentPath.Checkout, CreditPurchaseStatus.Finalized, "pay-1"));

        var adopted = Purchase(tenantId, "sub-1:adopted", CreditPaymentPath.Checkout, CreditPurchaseStatus.Finalized, "pay-2");
        adopted.CardAdoptedAtUtc = Now;
        await Insert(adopted);

        var old = Purchase(tenantId, "sub-1:old", CreditPaymentPath.Checkout, CreditPurchaseStatus.Finalized, "pay-3");
        old.CreatedAtUtc = Now.AddDays(-3);
        await Insert(old);

        await Insert(Purchase(tenantId, "sub-1:stored", CreditPaymentPath.StoredCard, CreditPurchaseStatus.Finalized, "pay-4"));

        var due = await _purchases.ListCardAdoptionDueAsync(
            tenantId, Now.AddHours(-24), Now.AddMinutes(10), 10, CancellationToken.None);

        due.Select(purchase => purchase.ItemId).Should().Equal("sub-1:due");
    }

    private async Task<BillingAccount> Account(string tenantId, string organizationId, string? defaultCard)
    {
        var account = await _accounts.GetOrCreateAndReconcileAsync(
            new BillingAccount { TenantId = tenantId, OrganizationId = organizationId, ProviderName = "Stripe" },
            CancellationToken.None);

        if (defaultCard is not null)
        {
            await _accounts.TrySetProviderCustomerAsync(
                tenantId, account.ItemId, "cus_chosen", defaultCard, null, CancellationToken.None);
        }

        return account;
    }

    private async Task Insert(SubscriptionCreditPurchase purchase) =>
        (await _purchases.TryCreateAsync(purchase, CancellationToken.None)).Should().BeTrue();

    private static SubscriptionCreditPurchase Purchase(
        string tenantId,
        string id,
        CreditPaymentPath path,
        CreditPurchaseStatus status,
        string? paymentId) => new()
    {
        ItemId = id,
        TenantId = tenantId,
        OrganizationId = "org-1",
        SubscriptionId = "sub-1",
        MeterKey = "ai-credit",
        Quantity = 100,
        CurrencyCode = "USD",
        TotalMinor = 1_000,
        PaymentPath = path,
        Status = status,
        PaymentId = paymentId,
        CreatedAtUtc = Now,
        LastUpdatedAtUtc = Now
    };
}
