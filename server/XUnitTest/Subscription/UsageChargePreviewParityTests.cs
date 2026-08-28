using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// The overage preview and period-end rating must never disagree: fed the same subscription and
/// the same eventual usage, they have to land on identical gross, discount, tax and total figures.
/// </summary>
public sealed class UsageChargePreviewParityTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    [Fact]
    public async Task Preview_and_final_rating_agree_on_gross_discount_tax_and_total()
    {
        var subscription = NewSubscription();

        // Period-end rating: the period has just ended with a counter balance of 700.
        var ratingUsage = new Mock<ISubscriptionUsageRepository>();
        var subscriptions = new Mock<ISubscriptionRepository>();
        var usageInvoices = new Mock<ISubscriptionUsageInvoiceRepository>();
        var billingAccounts = new Mock<IBillingAccountRepository>();
        var gateway = new Mock<ISubscriptionBillingGateway>();
        SubscriptionUsageInvoice? createdInvoice = null;

        subscriptions
            .Setup(repository => repository.ListDueForUsageRatingAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([subscription]);
        subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, subscription.ItemId, It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        usageInvoices
            .Setup(repository => repository.GetAsync(
                TenantId, subscription.ItemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionUsageInvoice?)null);
        usageInvoices
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionUsageInvoice>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionUsageInvoice, CancellationToken>((invoice, _) => createdInvoice = invoice)
            .ReturnsAsync(true);
        ratingUsage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, subscription.ItemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SubscriptionUsageCounter { MeterKey = "screening", Balance = 700 }]);

        var ratingProcessor = new SubscriptionUsageRatingProcessor(
            subscriptions.Object,
            ratingUsage.Object,
            usageInvoices.Object,
            billingAccounts.Object,
            gateway.Object,
            new SubscriptionOutboxEventFactory(),
            new OptionsStub(),
            NullLogger<SubscriptionUsageRatingProcessor>.Instance,
            new ControlledTimeProvider(new DateTimeOffset(
                subscription.CurrentUsagePeriodEndUtc, TimeSpan.Zero)));

        await ratingProcessor.CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        createdInvoice.Should().NotBeNull();

        // The overage preview: nothing recorded yet this period, and the same 700 units are asked
        // for as a single hypothetical addition.
        var previewUsage = new Mock<ISubscriptionUsageRepository>();
        previewUsage
            .Setup(repository => repository.SummariseLedgerAsync(
                TenantId, subscription.ItemId, "screening", It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0L, 0L));
        previewUsage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionUsageCounter?)null);

        var previewSubscriptions = new Mock<ISubscriptionRepository>();
        previewSubscriptions
            .Setup(repository => repository.GetLiveAsync(
                TenantId, OrganizationId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        var contextResolver = new Mock<ISubscriptionContextResolver>();
        contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-1")));

        var previewService = new SubscriptionUsageOveragePreviewService(
            previewSubscriptions.Object,
            previewUsage.Object,
            new MeterAllowanceResolver(previewUsage.Object),
            contextResolver.Object,
            new PreviewUsageOverageRequestValidator(),
            // A moment inside the same period the rating pass just closed.
            new ControlledTimeProvider(new DateTimeOffset(
                subscription.CurrentUsagePeriodStartUtc.AddDays(14), TimeSpan.Zero)));

        var previewResult = await previewService.PreviewAsync(
            new PreviewUsageOverageRequest { MeterKey = "screening", AdditionalQuantity = 700 },
            "corr-1",
            CancellationToken.None);

        previewResult.IsSuccess.Should().BeTrue();
        var projected = previewResult.Value!.ProjectedPeriodCharge;

        projected.GrossMinor.Should().Be(
            createdInvoice!.Lines.Sum(line => line.AmountMinor));
        projected.AutomaticDiscountMinor.Should().Be(createdInvoice.DiscountAmountMinor);
        projected.NetMinor.Should().Be(createdInvoice.NetAmountMinor);
        projected.TaxMinor.Should().Be(createdInvoice.TaxAmountMinor);
        projected.TotalMinor.Should().Be(createdInvoice.TotalAmountMinor);
    }

    private static SubscriptionDetail NewSubscription() => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
        Price = new PriceSnapshot
        {
            CurrencyCode = "CHF",
            AutomaticDiscountBasisPoints = 800,
            TaxRateBasisPoints = 770,
            TaxMode = TaxMode.Exclusive
        },
        CurrentUsagePeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentUsagePeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        UsageSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        },
        Plan = new PlanSnapshot
        {
            Meters =
            [
                new PlanMeter
                {
                    MeterKey = "screening",
                    UnitLabel = "screenings",
                    IncludedQuantity = 500,
                    OverageAllowed = true,
                    RateTables =
                    [
                        new MeterRateTable
                        {
                            CurrencyCode = "CHF",
                            Tiers = [new MeterTier { UpToQuantity = null, UnitAmountMinor = 10 }]
                        }
                    ]
                }
            ]
        }
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new();

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
