using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Redeeming a campaign at activation, and releasing it if activation never happens.
/// </summary>
public sealed class SubscriptionActivationProcessorCampaignTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionPaymentLinkRepository> _links = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IStoredPaymentMethodRepository> _storedMethods = new();
    private readonly Mock<ICampaignRedemptionRepository> _redemptions = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

    private SubscriptionDetail _subscription = NewSubscription(campaign: true);

    public SubscriptionActivationProcessorCampaignTests()
    {
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _subscription);
        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _links
            .Setup(repository => repository.TrySettleAsync(
                TenantId, "link-1", It.IsAny<SubscriptionPaymentLinkState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _links
            .Setup(repository => repository.ListDueAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubscriptionPaymentLink
                {
                    ItemId = "link-1", TenantId = TenantId, OrganizationId = OrganizationId,
                    SubscriptionId = "sub-1", PaymentDetailId = "pay-1",
                    Purpose = SubscriptionPaymentPurpose.InitialCharge, CorrelationId = "corr-1"
                }
            ]);
        _payments
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "pay-1", TenantId = TenantId, PaymentStatus = PaymentStatuses.Captured,
                WebhookConfirmedAtUtc = DateTime.UtcNow, OrganizationId = "default"
            });
    }

    [Fact]
    public async Task Activation_marks_a_campaign_discount_redeemed()
    {
        var settled = await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        settled.Should().Be(1);
        _redemptions.Verify(
            repository => repository.TryMarkRedeemedAsync(
                TenantId, "discount-1", "sub-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_standard_discount_never_touches_the_redemption_repository_on_activation()
    {
        _subscription = NewSubscription(campaign: false);

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _redemptions.Verify(
            repository => repository.TryMarkRedeemedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task No_subscription_at_all_never_touches_the_redemption_repository()
    {
        _subscription = NewSubscription(campaign: false);
        _subscription.CurrencyCode = "CHF"; // untouched, just avoiding an unused-variable warning

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // "another worker got there first"

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _redemptions.Verify(
            repository => repository.TryMarkRedeemedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Expiry_of_an_unactivated_campaign_subscription_releases_it()
    {
        _subscription = NewSubscription(campaign: true);

        _payments
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "pay-1", TenantId = TenantId, PaymentStatus = PaymentStatuses.Refused,
                WebhookConfirmedAtUtc = DateTime.UtcNow, OrganizationId = "default"
            });

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                TenantId, "discount-1", "sub-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _redemptions.Verify(
            repository => repository.TryMarkRedeemedAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_standard_discount_never_touches_the_redemption_repository_on_expiry()
    {
        _subscription = NewSubscription(campaign: false);

        _payments
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "pay-1", TenantId = TenantId, PaymentStatus = PaymentStatuses.Refused,
                WebhookConfirmedAtUtc = DateTime.UtcNow, OrganizationId = "default"
            });

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Expiry_never_releases_when_the_expire_transition_itself_did_not_apply()
    {
        // "Another worker got there first" for the expiry transition too -- releasing here would
        // be racing whatever that other worker is doing with the same reservation.
        _subscription = NewSubscription(campaign: true);
        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _payments
            .Setup(repository => repository.GetByIdAsync(
                TenantId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "pay-1", TenantId = TenantId, PaymentStatus = PaymentStatuses.Refused,
                WebhookConfirmedAtUtc = DateTime.UtcNow, OrganizationId = "default"
            });

        await Processor().ProcessDueAsync(TenantId, CancellationToken.None);

        _redemptions.Verify(
            repository => repository.TryReleaseAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private SubscriptionActivationProcessor Processor() => new(
        _links.Object,
        _subscriptions.Object,
        _accounts.Object,
        new SubscriptionOutboxEventFactory(),
        _payments.Object,
        _storedMethods.Object,
        new SubscriptionOptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<SubscriptionActivationProcessor>.Instance,
        _time,
        redemptions: _redemptions.Object);

    private static SubscriptionDetail NewSubscription(bool campaign) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = SubscriptionStatus.Incomplete,
        CurrencyCode = "CHF",
        OrderId = "sub:sub-1",
        CorrelationId = "corr-1",
        Plan = new PlanSnapshot { Code = "professional" },
        Discount = campaign
            ? new DiscountTerms
            {
                Code = "free1",
                DiscountId = "discount-1",
                DiscountVersion = 1,
                Campaign = new CampaignTerms
                {
                    Kind = CampaignKind.FreeOpeningCalendarPeriod,
                    OneUsePerOrganization = true
                }
            }
            : new DiscountTerms { Code = "launch25", Kind = DiscountKind.Percent, PercentBasisPoints = 2500 }
    };
}
