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

    [Fact]
    public async Task CarryForward_meters_are_rated_by_final_rating_not_silently_skipped()
    {
        // CarryForward still resets, rates and reports per window — only Never sits outside the
        // per-period sweep. CarryForwardCap = 0 keeps the effective allowance equal to the plan's
        // own IncludedQuantity, so this test isolates the reset-policy filter itself rather than
        // also depending on the carried-in amount.
        var subscription = NewSubscription();
        subscription.Plan.Meters[0].ResetPolicy = MeterResetPolicy.CarryForward;
        subscription.Plan.Meters[0].CarryForwardCap = 0;

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
        ratingUsage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionUsageCounter?)null);

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

        // Before this fix, `.Where(meter => meter.ResetPolicy == MeterResetPolicy.Periodic)`
        // would have skipped this meter outright and produced no line at all.
        createdInvoice.Should().NotBeNull();
        createdInvoice!.Lines.Should().ContainSingle(line => line.MeterKey == "screening");
        createdInvoice.Lines[0].OverageQuantity.Should().Be(200, "700 balance − 500 allowance");
        createdInvoice.Lines[0].AmountMinor.Should().Be(2_000);
    }

    [Fact]
    public async Task Trial_grant_allowance_agrees_between_preview_and_final_rating()
    {
        // Plan allowance 150, trial grant 500, usage 300 — the example from the reviewed defect.
        // Final rating must resolve the same effective (trial) allowance the preview does, not
        // `PlanMeter.IncludedQuantity` directly, or it would bill 150 units nobody was ever quoted.
        var subscription = NewSubscription();
        subscription.Plan.Meters[0].IncludedQuantity = 150;
        subscription.Status = SubscriptionStatus.Trialing;
        subscription.Trial = new TrialTerms
        {
            StartsAtUtc = subscription.CurrentUsagePeriodStartUtc,
            EndsAtUtc = subscription.CurrentUsagePeriodEndUtc,
            Grants = [new TrialMeterGrant { MeterKey = "screening", IncludedQuantity = 500 }]
        };

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
            .ReturnsAsync([new SubscriptionUsageCounter { MeterKey = "screening", Balance = 300 }]);
        ratingUsage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionUsageCounter?)null);

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
        // The buggy computation — `IncludedQuantity` straight off the plan, ignoring the trial
        // grant — would have billed 300 − 150 = 150 units. Documented here as the wrong answer
        // this fix removes, not as behaviour under test.
        SubscriptionUsageRater.OverageAmountMinor(subscription.Plan.Meters[0], 300, "CHF")
            .Should().Be(1_500, "this is what the pre-fix computation would have billed");
        // A rated meter now always records a line, even fully within allowance — see
        // SubscriptionUsageRatingProcessor's own remarks on why the invoice must record what was
        // included and used, not just what it charged for. The charge itself is unaffected: a
        // zero-amount line contributes nothing to the total asserted below.
        createdInvoice!.Lines.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                MeterKey = "screening",
                IncludedQuantity = 500m,
                UsedQuantity = 300m,
                OverageQuantity = 0m,
                AmountMinor = 0L
            });
        createdInvoice.TotalAmountMinor.Should().Be(0);

        // The preview must agree: usage arrives as a hypothetical addition on top of nothing
        // recorded yet, landing on the same total balance (300) the rating pass just closed.
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
            new ControlledTimeProvider(new DateTimeOffset(
                subscription.CurrentUsagePeriodStartUtc.AddDays(1), TimeSpan.Zero)));

        var previewResult = await previewService.PreviewAsync(
            new PreviewUsageOverageRequest { MeterKey = "screening", AdditionalQuantity = 300 },
            "corr-1",
            CancellationToken.None);

        previewResult.IsSuccess.Should().BeTrue();
        previewResult.Value!.IncludedQuantity.Should().Be(500, "the trial grant, not the plan's 150");
        previewResult.Value.ProjectedOverage.Should().Be(0);
        previewResult.Value.ProjectedPeriodCharge.TotalMinor.Should().Be(
            createdInvoice.TotalAmountMinor);
    }

    [Fact]
    public async Task Two_overage_meters_at_a_rounding_boundary_agree_with_the_aggregate_not_a_slice()
    {
        // A 5% automatic discount and a 7.7% tax, at these particular gross figures, round
        // differently depending on whether they are applied to the whole invoice's overage or to
        // one meter's slice of it — the exact scenario the third defect describes. Rating the
        // requested meter's delta in isolation would have priced this preview at 338; rating the
        // whole aggregate — as the invoice actually will — prices it at 337.
        var subscription = NewSubscription();
        subscription.Price.AutomaticDiscountBasisPoints = 500;
        subscription.Price.TaxRateBasisPoints = 770;
        subscription.Plan.Meters.Add(new PlanMeter
        {
            MeterKey = "storage",
            UnitLabel = "GB",
            IncludedQuantity = 200,
            OverageAllowed = true,
            RateTables =
            [
                new MeterRateTable
                {
                    CurrencyCode = "CHF",
                    Tiers = [new MeterTier { UpToQuantity = null, UnitAmountMinor = 7 }]
                }
            ]
        });

        var usage = new Mock<ISubscriptionUsageRepository>();
        usage
            .Setup(repository => repository.SummariseLedgerAsync(
                TenantId, subscription.ItemId, "screening", It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0L, 0L));
        usage
            .Setup(repository => repository.SummariseLedgerAsync(
                TenantId, subscription.ItemId, "storage", It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((0L, 0L));
        usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.Is<string>(id => id.Contains("screening", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            // 600 usage − 500 included = 100 overage units × 10 = 1,000 gross.
            .ReturnsAsync(new SubscriptionUsageCounter { Balance = 600, LimitSnapshot = 500 });
        usage
            .Setup(repository => repository.GetCounterAsync(
                TenantId, It.Is<string>(id => id.Contains("storage", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            // 250 usage − 200 included = 50 overage units × 7 = 350 gross.
            .ReturnsAsync(new SubscriptionUsageCounter { Balance = 250, LimitSnapshot = 200 });

        var subscriptions = new Mock<ISubscriptionRepository>();
        subscriptions
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
            subscriptions.Object,
            usage.Object,
            new MeterAllowanceResolver(usage.Object),
            contextResolver.Object,
            new PreviewUsageOverageRequestValidator(),
            new ControlledTimeProvider(new DateTimeOffset(
                subscription.CurrentUsagePeriodStartUtc.AddDays(1), TimeSpan.Zero)));

        // 33 additional units on "screening" at 10/unit adds 330 gross (100 → 133 overage units).
        var result = await previewService.PreviewAsync(
            new PreviewUsageOverageRequest { MeterKey = "screening", AdditionalQuantity = 33 },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var response = result.Value!;

        response.CurrentCharge.GrossMinor.Should().Be(1_350, "screening 1,000 + storage 350");
        response.CurrentCharge.TotalMinor.Should().Be(1_382);
        response.ProjectedPeriodCharge.GrossMinor.Should().Be(1_680, "screening 1,330 + storage 350");
        response.ProjectedPeriodCharge.TotalMinor.Should().Be(1_719);
        response.AdditionalCharge.TotalMinor.Should().Be(337,
            "the aggregate difference — 338 is what discounting and taxing the requested meter " +
            "alone would have produced, and would disagree with the invoice");
    }

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new();

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
