using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Following an applied charge until its money is captured or it fails (spec 009).
/// </summary>
public sealed class SubscriptionChargeSettlementWatcherTests
{
    private const string TenantId = "tenant-1";
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<ISubscriptionUsageInvoiceRepository> _usageInvoices = new();
    private readonly Mock<ISubscriptionRenewalService> _renewals = new();
    private readonly Mock<ISubscriptionWorkScheduler> _scheduler = new();
    private readonly Mock<IOptionsMonitor<SubscriptionOptions>> _options = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly SubscriptionDetail _subscription = new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = "org-1",
        Status = SubscriptionStatus.Active
    };

    public SubscriptionChargeSettlementWatcherTests()
    {
        _options.Setup(options => options.CurrentValue).Returns(new SubscriptionOptions());
        _subscriptions
            .Setup(repository => repository.GetByIdAsync(TenantId, "sub-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_subscription);
        _usageInvoices
            .Setup(repository => repository.ListBySubscriptionAsync(
                TenantId, "sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    [Theory]
    [InlineData(PaymentStatuses.Captured)]
    [InlineData(PaymentStatuses.PartiallyCaptured)]
    [InlineData(PaymentStatuses.Refunded)]
    public async Task A_settled_payment_ends_the_watch(string status)
    {
        Payment(status);

        var outcome = await Watcher().CheckAsync(Work(1), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _scheduler.VerifyNoOtherCalls();
        _renewals.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PaymentStatuses.Processing)]
    [InlineData(PaymentStatuses.Authorized)]
    public async Task A_pending_payment_schedules_the_next_check(string status)
    {
        Payment(status);

        var outcome = await Watcher().CheckAsync(Work(3), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _scheduler.Verify(scheduler => scheduler.ScheduleChargeSettlementWatchAsync(
            _subscription, "pay-1", 4, "corr-1", It.IsAny<CancellationToken>()), Times.Once);
        _renewals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_watch_gives_up_after_the_last_check()
    {
        Payment(PaymentStatuses.Authorized);

        var outcome = await Watcher().CheckAsync(
            Work(new SubscriptionOptions().ChargeSettlementWatchMaxChecks), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _scheduler.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PaymentStatuses.Expired)]
    [InlineData(PaymentStatuses.Cancelled)]
    [InlineData(PaymentStatuses.Refused)]
    public async Task A_failed_charge_is_handed_to_dunning(string status)
    {
        var payment = Payment(status);

        var outcome = await Watcher().CheckAsync(Work(2), default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _renewals.Verify(renewals => renewals.RecordLateChargeFailureAsync(
            _subscription, payment, It.IsAny<CancellationToken>()), Times.Once);
        _scheduler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_failed_usage_charge_reopens_its_invoice_and_never_touches_the_subscription()
    {
        Payment(PaymentStatuses.Expired);
        _usageInvoices
            .Setup(repository => repository.ListBySubscriptionAsync(
                TenantId, "sub-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubscriptionUsageInvoice
                {
                    ItemId = "inv-1",
                    PeriodKey = "2026-10",
                    State = SubscriptionUsageInvoiceState.Charged,
                    PaymentDetailId = "pay-1"
                }
            ]);
        _usageInvoices
            .Setup(repository => repository.TryReopenChargedAsync(
                TenantId, "inv-1", "pay-1",
                _time.GetUtcNow().UtcDateTime.AddHours(new SubscriptionOptions().UsageRatingRetryHours),
                PaymentStatuses.Expired,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Verifiable();

        await Watcher().CheckAsync(Work(2), default);

        _usageInvoices.Verify();
        _scheduler.Verify(scheduler => scheduler.ScheduleUsageInvoiceChargeAsync(
            _subscription, "2026-10", "corr-1", It.IsAny<CancellationToken>()), Times.Once);
        _renewals.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_key_that_names_no_payment_is_dead_lettered()
    {
        var outcome = await Watcher().CheckAsync(
            new SubscriptionBackgroundWork { TenantId = TenantId, AggregateId = "sub-1", WorkKey = "nonsense" },
            default);

        outcome.Result.Should().Be(SubscriptionWorkResult.Permanent);
    }

    [Fact]
    public void The_work_key_round_trips_and_spaces_the_checks_out()
    {
        ChargeSettlementWatchKey.TryParse(ChargeSettlementWatchKey.For("pay:odd", 12), out var paymentId, out var check)
            .Should().BeTrue();
        paymentId.Should().Be("pay:odd");
        check.Should().Be(12);
        ChargeSettlementWatchKey.TryParse("settlement-watch:pay-1:x", out _, out _).Should().BeFalse();
        ChargeSettlementWatchKey.DelayBefore(1).Should().Be(TimeSpan.FromHours(1));
        ChargeSettlementWatchKey.DelayBefore(2).Should().Be(TimeSpan.FromHours(4));
        ChargeSettlementWatchKey.DelayBefore(9).Should().Be(TimeSpan.FromDays(1));
    }

    private PaymentDetail Payment(string status)
    {
        var payment = new PaymentDetail { ItemId = "pay-1", TenantId = TenantId, PaymentStatus = status };
        _payments
            .Setup(repository => repository.GetByIdAsync(TenantId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        return payment;
    }

    private static SubscriptionBackgroundWork Work(int checkNumber) => new()
    {
        TenantId = TenantId,
        AggregateId = "sub-1",
        WorkType = SubscriptionWorkType.ChargeSettlementWatch,
        WorkKey = ChargeSettlementWatchKey.For("pay-1", checkNumber),
        CorrelationId = "corr-1"
    };

    private SubscriptionChargeSettlementWatcher Watcher() => new(
        _subscriptions.Object,
        _payments.Object,
        _usageInvoices.Object,
        _renewals.Object,
        _scheduler.Object,
        _options.Object,
        NullLogger<SubscriptionChargeSettlementWatcher>.Instance,
        _time);
}
