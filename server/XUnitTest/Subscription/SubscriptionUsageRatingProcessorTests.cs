using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>Closing a usage period and pricing its overage into an invoice.</summary>
public sealed class SubscriptionUsageRatingProcessorTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<ISubscriptionUsageInvoiceRepository> _usageInvoices = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 1, 0, 30, 0, TimeSpan.Zero));

    private IReadOnlyList<SubscriptionDetail> _due = [];
    private SubscriptionUsageInvoice? _createdInvoice;

    public SubscriptionUsageRatingProcessorTests()
    {
        _subscriptions
            .Setup(repository => repository.ListDueForUsageRatingAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _due);

        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                It.IsAny<string>(),
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _usageInvoices
            .Setup(repository => repository.GetAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionUsageInvoice?)null);

        _usageInvoices
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionUsageInvoice>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionUsageInvoice, CancellationToken>((invoice, _) => _createdInvoice = invoice)
            .ReturnsAsync(true);

        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Fact]
    public async Task A_period_with_no_overage_creates_a_no_charge_invoice()
    {
        _due = [NewSubscription("sub-1")];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCounter("screening", 100)]);

        await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        _createdInvoice!.State.Should().Be(SubscriptionUsageInvoiceState.NoCharge);
        _createdInvoice.TotalAmountMinor.Should().Be(0);
        _createdInvoice.NextAttemptAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task A_period_with_overage_creates_a_pending_invoice_priced_across_meters()
    {
        _due = [NewSubscription("sub-1")];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCounter("screening", 700), NewCounter("envelope", 50)]);

        await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        // screening: 200 overage * 10 = 2,000. envelope has no counter overage (under included).
        _createdInvoice!.State.Should().Be(SubscriptionUsageInvoiceState.Pending);
        _createdInvoice.TotalAmountMinor.Should().Be(2_000);
        _createdInvoice.Lines.Should().ContainSingle(line => line.MeterKey == "screening");
        _createdInvoice.NextAttemptAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task An_existing_invoice_is_not_recreated()
    {
        _due = [NewSubscription("sub-1")];
        _usageInvoices
            .Setup(repository => repository.GetAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageInvoice());

        await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        _usageInvoices.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionUsageInvoice>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // The period still advances even though the invoice already existed.
        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_worker_outage_spanning_three_periods_rates_all_three()
    {
        var subscription = NewSubscription("sub-1");
        subscription.CurrentUsagePeriodStartUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.CurrentUsagePeriodEndUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        _due = [subscription];

        var closed = await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        // June, July and August all close before the September "now" is reached.
        closed.Should().Be(3);
        _usageInvoices.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionUsageInvoice>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task Nothing_due_closes_nothing()
    {
        _due = [];

        var closed = await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        closed.Should().Be(0);
        _usageInvoices.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionUsageInvoice>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_lost_compare_and_set_stops_advancing_that_subscription()
    {
        _due = [NewSubscription("sub-1")];
        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId, "sub-1", It.IsAny<SubscriptionTransition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var closed = await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        closed.Should().Be(0);
    }

    private SubscriptionUsageRatingProcessor Processor() => new(
        _subscriptions.Object,
        _usage.Object,
        _usageInvoices.Object,
        new OptionsStub(),
        NullLogger<SubscriptionUsageRatingProcessor>.Instance,
        _time);

    private static SubscriptionDetail NewSubscription(string id) => new()
    {
        ItemId = id,
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        Status = SubscriptionStatus.Active,
        CurrencyCode = "CHF",
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
                },
                new PlanMeter
                {
                    MeterKey = "envelope",
                    IncludedQuantity = 100,
                    OverageAllowed = true,
                    RateTables =
                    [
                        new MeterRateTable
                        {
                            CurrencyCode = "CHF",
                            Tiers = [new MeterTier { UpToQuantity = null, UnitAmountMinor = 20 }]
                        }
                    ]
                }
            ]
        }
    };

    private static SubscriptionUsageCounter NewCounter(string meterKey, long balance) => new()
    {
        MeterKey = meterKey,
        Balance = balance
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new();

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
