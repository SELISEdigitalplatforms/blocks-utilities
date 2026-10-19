using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription.Credits;

/// <summary>
/// The two places a credit purchase is recognised after the fact: its order id, read back by the
/// invoice issuer, and its one-use code redemption, settled by the reconciliation sweep.
/// </summary>
/// <remarks>
/// Guards against a credit purchase being invoiced as a renewal of the plan, and against a one-use
/// code being held forever — or handed back and used twice — because the sweep looked for a
/// subscription where the row names a purchase.
/// </remarks>
public sealed class CreditRedemptionAndOrderIdTests
{
    private const string TenantId = "tenant-1";
    private const string PurchaseId = "sub-1:key-1";

    private readonly Mock<ICampaignRedemptionRepository> _redemptions = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionCreditPurchaseRepository> _purchases = new();
    private readonly ControlledTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));

    public CreditRedemptionAndOrderIdTests()
    {
        _redemptions
            .Setup(repository => repository.ListStaleAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new CampaignRedemption
                {
                    ItemId = "red-1",
                    TenantId = TenantId,
                    DiscountId = "disc-1",
                    SubscriptionId = PurchaseId,
                    CreditPurchaseId = PurchaseId,
                    OneUsePerOrganization = true
                }
            ]);
    }

    [Fact]
    public void A_credit_purchase_order_id_reads_back_as_a_credit_purchase_and_fits_the_limit()
    {
        var subscriptionId = Guid.NewGuid().ToString();
        var orderId = SubscriptionConstants.CreditPurchaseOrderIdFor(subscriptionId, Guid.NewGuid().ToString("N"));

        var charge = SubscriptionOrderId.Parse(orderId);

        charge.Kind.Should().Be(SubscriptionChargeKind.CreditPurchase,
            "read as anything else, the purchase would be invoiced as a renewal of the plan");
        charge.SubscriptionId.Should().Be(subscriptionId);
        orderId.Length.Should().BeLessThanOrEqualTo(80, "the payment module refuses longer order ids");
    }

    [Theory]
    [InlineData(CreditPurchaseStatus.Charged)]
    [InlineData(CreditPurchaseStatus.Finalized)]
    public async Task A_paid_purchases_code_is_marked_redeemed(CreditPurchaseStatus status)
    {
        PurchaseIs(status);

        await Reconciler().ReconcileAsync(TenantId, CancellationToken.None);

        _redemptions.Verify(repository => repository.TryMarkRedeemedAsync(
            TenantId, "disc-1", PurchaseId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _subscriptions.Verify(repository => repository.GetByIdAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "the row names a purchase, so looking it up as a subscription would find nothing");
    }

    [Fact]
    public async Task A_failed_purchases_code_is_given_back()
    {
        PurchaseIs(CreditPurchaseStatus.Failed);

        await Reconciler().ReconcileAsync(TenantId, CancellationToken.None);

        _redemptions.Verify(repository => repository.TryReleaseAsync(
            TenantId, "disc-1", PurchaseId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_purchase_still_in_flight_keeps_its_code_reserved()
    {
        PurchaseIs(CreditPurchaseStatus.Pending);

        var resolved = await Reconciler().ReconcileAsync(TenantId, CancellationToken.None);

        resolved.Should().Be(0);
        _redemptions.Verify(repository => repository.TryReleaseAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never,
            "the card may already have been charged, and releasing would let the code be used twice");
        _redemptions.Verify(repository => repository.DeferAsync(
            TenantId, "red-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private void PurchaseIs(CreditPurchaseStatus status) =>
        _purchases
            .Setup(repository => repository.GetAsync(TenantId, PurchaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionCreditPurchase { ItemId = PurchaseId, TenantId = TenantId, Status = status });

    private CampaignRedemptionReconciler Reconciler() => new(
        _redemptions.Object,
        _subscriptions.Object,
        new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<CampaignRedemptionReconciler>.Instance,
        _time,
        _purchases.Object);
}
