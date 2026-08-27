using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>Closing a usage period, pricing its overage, and charging the resulting invoice.</summary>
public sealed class SubscriptionUsageRatingProcessorTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionUsageRepository> _usage = new();
    private readonly Mock<ISubscriptionUsageInvoiceRepository> _usageInvoices = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new();
    private readonly Mock<IUsagePeriodClosureRepository> _closures = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 9, 1, 0, 30, 0, TimeSpan.Zero));

    private IReadOnlyList<SubscriptionDetail> _due = [];
    private IReadOnlyList<SubscriptionUsageInvoice> _dueInvoices = [];
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

        _subscriptions
            .Setup(repository => repository.TryRemovePendingUsagePeriodAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _subscriptions
            .Setup(repository => repository.GetByIdAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, string subscriptionId, CancellationToken _) =>
                NewSubscription(subscriptionId));

        _usageInvoices
            .Setup(repository => repository.GetAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionUsageInvoice?)null);

        _usageInvoices
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionUsageInvoice>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionUsageInvoice, CancellationToken>((invoice, _) => _createdInvoice = invoice)
            .ReturnsAsync(true);

        _usageInvoices
            .Setup(repository => repository.ListDueAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _dueInvoices);

        _usageInvoices
            .Setup(repository => repository.TryMarkChargedAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _usageInvoices
            .Setup(repository => repository.TryMarkAbandonedAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _billingAccounts
            .Setup(repository => repository.GetAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount
            {
                ProviderName = "STRIPE",
                DefaultPaymentMethodId = "pm-1",
                ProviderCustomerId = "cus_123"
            });

        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Success("in_1", "corr-1"));
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
    public async Task A_monthly_closeout_never_rates_a_lifetime_capacity_meter()
    {
        var subscription = NewSubscription("sub-1");
        subscription.Plan.Meters[0].ResetPolicy = MeterResetPolicy.Never;
        _due = [subscription];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCounter("screening", 700)]);

        await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        _createdInvoice!.State.Should().Be(SubscriptionUsageInvoiceState.NoCharge);
        _createdInvoice.Lines.Should().BeEmpty(
            "a lifetime capacity is enforced continuously, not sold again as monthly overage");
    }

    [Fact]
    public async Task A_plan_change_rates_the_detached_window_under_its_original_allowance()
    {
        var subscription = NewSubscription("sub-1");
        var oldPlan = subscription.Plan;
        subscription.Plan = new PlanSnapshot { Meters = [] };
        subscription.CurrentUsagePeriodEndUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        subscription.PendingUsagePeriods =
        [
            new PendingUsagePeriod
            {
                PeriodKey = "M20260801T000000Z",
                Plan = oldPlan,
                Price = subscription.Price,
                CurrencyCode = "CHF",
                CorrelationId = "change-1"
            }
        ];
        _due = [subscription];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCounter("screening", 700)]);

        var closed = await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        closed.Should().Be(1);
        _createdInvoice!.TotalAmountMinor.Should().Be(2_000);
        _subscriptions.Verify(repository => repository.TryRemovePendingUsagePeriodAsync(
            TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_canceled_subscriptions_final_window_is_rated_from_its_own_snapshot()
    {
        var subscription = NewSubscription("sub-1");
        subscription.Status = SubscriptionStatus.Canceled;
        var planAtCancellation = subscription.Plan;
        subscription.PendingUsagePeriods =
        [
            new PendingUsagePeriod
            {
                PeriodKey = "M20260801T000000Z",
                PeriodStartUtc = subscription.CurrentUsagePeriodStartUtc,
                PeriodEndUtc = subscription.CurrentUsagePeriodEndUtc,
                Plan = planAtCancellation,
                Price = subscription.Price,
                CurrencyCode = "CHF",
                CorrelationId = "cancel-1"
            }
        ];
        _due = [subscription];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCounter("screening", 700)]);

        var closed = await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        closed.Should().Be(1, "a canceled subscription's final overage must still be rated");
        _createdInvoice!.TotalAmountMinor.Should().Be(2_000);
        _subscriptions.Verify(repository => repository.TryRemovePendingUsagePeriodAsync(
            TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_canceled_subscriptions_final_window_within_the_allowance_charges_nothing()
    {
        var subscription = NewSubscription("sub-1");
        subscription.Status = SubscriptionStatus.Canceled;
        subscription.PendingUsagePeriods =
        [
            new PendingUsagePeriod
            {
                PeriodKey = "M20260801T000000Z",
                PeriodStartUtc = subscription.CurrentUsagePeriodStartUtc,
                PeriodEndUtc = subscription.CurrentUsagePeriodEndUtc,
                Plan = subscription.Plan,
                Price = subscription.Price,
                CurrencyCode = "CHF",
                CorrelationId = "cancel-1"
            }
        ];
        _due = [subscription];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCounter("screening", 100)]);

        await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        _createdInvoice!.State.Should().Be(SubscriptionUsageInvoiceState.NoCharge);
        _createdInvoice.TotalAmountMinor.Should().Be(0);
    }

    /// <summary>
    /// A Canceled subscription can only appear here because it still holds a queued snapshot —
    /// the live-status filter excludes it otherwise. Its own current-window fields must not be
    /// touched a second time: the snapshot already captured that window, and advancing it again
    /// would rate the same usage under two different rows.
    /// </summary>
    [Fact]
    public async Task A_canceled_subscriptions_own_window_is_not_also_advanced_by_the_live_clock()
    {
        var subscription = NewSubscription("sub-1");
        subscription.Status = SubscriptionStatus.Canceled;
        subscription.PendingUsagePeriods =
        [
            new PendingUsagePeriod
            {
                PeriodKey = "M20260801T000000Z",
                PeriodStartUtc = subscription.CurrentUsagePeriodStartUtc,
                PeriodEndUtc = subscription.CurrentUsagePeriodEndUtc,
                Plan = subscription.Plan,
                Price = subscription.Price,
                CurrencyCode = "CHF",
                CorrelationId = "cancel-1"
            }
        ];
        _due = [subscription];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                TenantId,
                "sub-1",
                It.Is<SubscriptionTransition>(t =>
                    t.CurrentUsagePeriodStartUtc != null || t.CurrentUsagePeriodEndUtc != null),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "advancing a canceled subscription's own usage clock would open a period nothing " +
            "will ever close");
    }

    /// <summary>
    /// Simulates a crash between the invoice being written and the pending period being cleared —
    /// the retry this sweep pass represents must finish the cleanup without billing twice.
    /// </summary>
    [Fact]
    public async Task A_retry_after_a_crash_does_not_duplicate_the_charge_but_still_clears_the_pending_period()
    {
        var subscription = NewSubscription("sub-1");
        subscription.Status = SubscriptionStatus.Canceled;
        subscription.PendingUsagePeriods =
        [
            new PendingUsagePeriod
            {
                PeriodKey = "M20260801T000000Z",
                PeriodStartUtc = subscription.CurrentUsagePeriodStartUtc,
                PeriodEndUtc = subscription.CurrentUsagePeriodEndUtc,
                Plan = subscription.Plan,
                Price = subscription.Price,
                CurrencyCode = "CHF",
                CorrelationId = "cancel-1"
            }
        ];
        _due = [subscription];
        _usageInvoices
            .Setup(repository => repository.GetAsync(
                TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageInvoice
            {
                TenantId = TenantId,
                SubscriptionId = "sub-1",
                PeriodKey = "M20260801T000000Z",
                CurrencyCode = "CHF",
                State = SubscriptionUsageInvoiceState.Pending,
                TotalAmountMinor = 2_000
            });

        var closed = await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        closed.Should().Be(1, "the sweep still finishes clearing the pointer left behind");
        _usageInvoices.Verify(
            repository => repository.TryCreateAsync(
                It.IsAny<SubscriptionUsageInvoice>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the invoice this periodKey owns already exists — a second one would double-charge");
        _subscriptions.Verify(repository => repository.TryRemovePendingUsagePeriodAsync(
            TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_pending_period_with_an_active_writer_is_left_for_the_next_pass()
    {
        var subscription = NewSubscription("sub-1");
        subscription.Status = SubscriptionStatus.Canceled;
        subscription.PendingUsagePeriods =
        [
            new PendingUsagePeriod
            {
                PeriodKey = "M20260801T000000Z",
                Plan = subscription.Plan,
                Price = subscription.Price,
                CurrencyCode = "CHF",
                CorrelationId = "cancel-1"
            }
        ];
        _due = [subscription];
        _closures
            .Setup(repository => repository.GetAsync(
                TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsagePeriodClosure
            {
                ItemId = "sub-1:M20260801T000000Z",
                TenantId = TenantId,
                SubscriptionId = "sub-1",
                PeriodKey = "M20260801T000000Z",
                State = UsagePeriodClosureState.Closing,
                ActiveWriterCount = 1
            });

        var closed = await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        closed.Should().Be(0,
            "an in-flight usage write could still change the balance this would invoice");
        _createdInvoice.Should().BeNull();
        _subscriptions.Verify(
            repository => repository.TryRemovePendingUsagePeriodAsync(
                TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()),
            Times.Never,
            "left in place so the next sweep pass finds it again");
    }

    [Fact]
    public async Task A_pending_period_with_no_active_writers_is_rated_and_marked_closed()
    {
        var subscription = NewSubscription("sub-1");
        subscription.Status = SubscriptionStatus.Canceled;
        subscription.PendingUsagePeriods =
        [
            new PendingUsagePeriod
            {
                PeriodKey = "M20260801T000000Z",
                Plan = subscription.Plan,
                Price = subscription.Price,
                CurrencyCode = "CHF",
                CorrelationId = "cancel-1"
            }
        ];
        _due = [subscription];
        _closures
            .Setup(repository => repository.GetAsync(
                TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsagePeriodClosure
            {
                ItemId = "sub-1:M20260801T000000Z",
                TenantId = TenantId,
                SubscriptionId = "sub-1",
                PeriodKey = "M20260801T000000Z",
                State = UsagePeriodClosureState.Closing,
                ActiveWriterCount = 0
            });

        var closed = await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        closed.Should().Be(1);
        _createdInvoice.Should().NotBeNull();
        _closures.Verify(
            repository => repository.TryMarkClosedAsync(
                TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_pending_period_with_a_claim_still_releasing_is_left_for_the_next_pass()
    {
        var subscription = NewSubscription("sub-1");
        subscription.Status = SubscriptionStatus.Canceled;
        subscription.PendingUsagePeriods =
        [
            new PendingUsagePeriod
            {
                PeriodKey = "M20260801T000000Z",
                Plan = subscription.Plan,
                Price = subscription.Price,
                CurrencyCode = "CHF",
                CorrelationId = "cancel-1"
            }
        ];
        _due = [subscription];
        // The counter already reached zero, but a claim is still mid-release (ReleasePending) —
        // the decrement landed, or is about to, and its own state has not yet caught up. Rating
        // must still wait: HasOutstandingClaimsAsync is the second signal that catches this.
        _closures
            .Setup(repository => repository.GetAsync(
                TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsagePeriodClosure
            {
                ItemId = "sub-1:M20260801T000000Z",
                TenantId = TenantId,
                SubscriptionId = "sub-1",
                PeriodKey = "M20260801T000000Z",
                State = UsagePeriodClosureState.Closing,
                ActiveWriterCount = 0
            });
        _closures
            .Setup(repository => repository.HasOutstandingClaimsAsync(
                TenantId, "sub-1", "M20260801T000000Z", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var closed = await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        closed.Should().Be(0,
            "a claim still mid-release could still be about to change the balance this would " +
            "invoice, even though the counter already reads zero");
        _createdInvoice.Should().BeNull();
    }

    [Fact]
    public async Task Tax_is_applied_once_to_the_aggregate_not_per_meter_line()
    {
        var subscription = NewSubscription("sub-1");
        subscription.Price.TaxRateBasisPoints = 1_000; // 10%
        _due = [subscription];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCounter("screening", 700), NewCounter("envelope", 300)]);

        await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        // screening: 200 * 10 = 2,000. envelope: 200 * 20 = 4,000. Subtotal 6,000, +10% tax = 6,600.
        _createdInvoice!.TaxAmountMinor.Should().Be(600);
        _createdInvoice.TotalAmountMinor.Should().Be(6_600);
        _createdInvoice.Lines.Sum(line => line.AmountMinor).Should().Be(6_000,
            "tax is on the aggregate, never split back across individual meter lines");
    }

    [Fact]
    public async Task Overage_on_an_inclusive_price_charges_the_configured_amount_and_finds_the_tax_inside_it()
    {
        // Overage is priced by the same price the subscription was sold on, so it is quoted the same
        // way. Adding tax on top of an inclusive plan's overage would charge more than the meter's
        // published rate says.
        var subscription = NewSubscription("sub-1");
        subscription.Price.TaxRateBasisPoints = 1_000;
        subscription.Price.TaxMode = TaxMode.Inclusive;
        _due = [subscription];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCounter("screening", 700)]);

        await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        // 200 units over at 10 each is 2,000, and 2,000 × 1,000 / 11,000 is 182 of tax inside it.
        _createdInvoice!.TotalAmountMinor.Should().Be(2_000);
        _createdInvoice.TaxAmountMinor.Should().Be(182);
        _createdInvoice.NetAmountMinor.Should().Be(1_818);
    }

    [Fact]
    public async Task An_invoice_records_the_rate_and_mode_it_was_raised_under()
    {
        // Recorded, not recomputed later. The catalogue can be edited the day after this invoice is
        // charged, and a charged invoice has to keep describing itself the way it was charged.
        var subscription = NewSubscription("sub-1");
        subscription.Price.TaxRateBasisPoints = 770;
        subscription.Price.TaxMode = TaxMode.Exclusive;
        _due = [subscription];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCounter("screening", 700)]);

        await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        _createdInvoice!.TaxRateBasisPoints.Should().Be(770);
        _createdInvoice.TaxMode.Should().Be(TaxMode.Exclusive);
        _createdInvoice.NetAmountMinor.Should().Be(2_000);
        _createdInvoice.TaxAmountMinor.Should().Be(154);
        _createdInvoice.TotalAmountMinor.Should().Be(2_154);
    }

    [Fact]
    public async Task Overage_is_discounted_by_the_prices_automatic_discount_before_tax()
    {
        // Overage is a charge the price produces, so the price's own discount reaches it. Before tax,
        // because tax is owed on what is actually charged.
        var subscription = NewSubscription("sub-1");
        subscription.Price.AutomaticDiscountBasisPoints = 800;
        subscription.Price.TaxRateBasisPoints = 1_000;
        subscription.Price.TaxMode = TaxMode.Exclusive;
        _due = [subscription];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCounter("screening", 700)]);

        await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        // 2,000 of overage, 8% off is 160, leaving 1,840 to tax at 10%.
        _createdInvoice!.Lines.Sum(line => line.AmountMinor).Should().Be(2_000,
            "the lines say what was used; the discount is on the invoice, not inside a meter");
        _createdInvoice.DiscountAmountMinor.Should().Be(160);
        _createdInvoice.AutomaticDiscountBasisPoints.Should().Be(800);
        _createdInvoice.NetAmountMinor.Should().Be(1_840);
        _createdInvoice.TaxAmountMinor.Should().Be(184);
        _createdInvoice.TotalAmountMinor.Should().Be(2_024);

        // No band takes part in metered usage, so the invoice records the rate that applied and no
        // combination — rather than naming one it never used.
        _createdInvoice.AutomaticDiscountBasisPoints.Should().Be(800);
    }

    [Fact]
    public async Task Overage_on_a_price_without_an_automatic_discount_is_unchanged()
    {
        // Every metered plan that exists today is this shape, and its overage invoices must come out
        // to the same figure they always did.
        var subscription = NewSubscription("sub-1");
        _due = [subscription];
        _usage
            .Setup(repository => repository.ListCountersAsync(
                TenantId, "sub-1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([NewCounter("screening", 700)]);

        await Processor().CloseDuePeriodsAsync(TenantId, CancellationToken.None);

        _createdInvoice!.TotalAmountMinor.Should().Be(2_000);
        _createdInvoice.DiscountAmountMinor.Should().Be(0);
        _createdInvoice.AutomaticDiscountBasisPoints.Should().BeNull();
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

    [Fact]
    public async Task A_successful_charge_marks_the_invoice_charged_and_emits_an_event()
    {
        _dueInvoices = [NewInvoice()];

        await Processor().ChargeDueInvoicesAsync(TenantId, CancellationToken.None);

        _usageInvoices.Verify(
            repository => repository.TryMarkChargedAsync(
                TenantId, "inv-1", "in_1", It.IsAny<CancellationToken>()),
            Times.Once);
        _subscriptions.Verify(
            repository => repository.TryAppendEventAsync(
                TenantId,
                "sub-1",
                It.Is<SubscriptionOutboxEvent>(e => e.EventType == SubscriptionConstants.UsageRated),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_declined_charge_reschedules_short_of_the_attempt_ceiling()
    {
        _dueInvoices = [NewInvoice()];
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected, "card_declined", "declined", "corr-1"));

        await Processor().ChargeDueInvoicesAsync(TenantId, CancellationToken.None);

        _usageInvoices.Verify(
            repository => repository.RescheduleAsync(
                TenantId, "inv-1", 1, It.IsAny<DateTime>(), "card_declined", It.IsAny<CancellationToken>()),
            Times.Once);
        _usageInvoices.Verify(
            repository => repository.TryMarkAbandonedAsync(
                TenantId, "inv-1", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_decline_at_the_attempt_ceiling_abandons_and_emits_a_failure_event()
    {
        _dueInvoices = [NewInvoice(attemptCount: 2)];
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected, "card_declined", "declined", "corr-1"));

        await Processor().ChargeDueInvoicesAsync(TenantId, CancellationToken.None);

        _usageInvoices.Verify(
            repository => repository.TryMarkAbandonedAsync(
                TenantId, "inv-1", It.IsAny<CancellationToken>()),
            Times.Once);
        _subscriptions.Verify(
            repository => repository.TryAppendEventAsync(
                TenantId,
                "sub-1",
                It.Is<SubscriptionOutboxEvent>(e => e.EventType == SubscriptionConstants.UsageRatingFailed),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task No_payment_method_reschedules_without_calling_the_gateway()
    {
        _dueInvoices = [NewInvoice()];
        _billingAccounts
            .Setup(repository => repository.GetAsync(
                TenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount { ProviderName = "STRIPE", DefaultPaymentMethodId = null });

        await Processor().ChargeDueInvoicesAsync(TenantId, CancellationToken.None);

        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _usageInvoices.Verify(
            repository => repository.RescheduleAsync(
                TenantId, "inv-1", 1, It.IsAny<DateTime>(), "no_payment_method", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_failed_overage_charge_never_touches_the_subscription_status()
    {
        _dueInvoices = [NewInvoice(attemptCount: 2)];
        _gateway
            .Setup(gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<string>.Failure(
                PaymentFailureKind.ProviderRejected, "card_declined", "declined", "corr-1"));

        await Processor().ChargeDueInvoicesAsync(TenantId, CancellationToken.None);

        _subscriptions.Verify(
            repository => repository.TryTransitionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static SubscriptionUsageInvoice NewInvoice(int attemptCount = 0) => new()
    {
        ItemId = "inv-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        SubscriptionId = "sub-1",
        PeriodKey = "M20260801T000000Z",
        CurrencyCode = "CHF",
        TotalAmountMinor = 2_000,
        AttemptCount = attemptCount,
        State = SubscriptionUsageInvoiceState.Pending,
        CorrelationId = "corr-1"
    };

    private SubscriptionUsageRatingProcessor Processor() => new(
        _subscriptions.Object,
        _usage.Object,
        _usageInvoices.Object,
        _billingAccounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        new OptionsStub(),
        NullLogger<SubscriptionUsageRatingProcessor>.Instance,
        _time,
        closures: _closures.Object);

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
