using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Outbox;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentRecoveryProcessorTests
{
    private const string TenantId = "tenant-1";

    private static PaymentRecoveryProcessor Create(
        Mock<IPaymentRepository> repository,
        Mock<IPaymentService> service)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(m => m.CurrentValue)
            .Returns(new PaymentOptions { OutboxBatchSize = 50 });
        return new PaymentRecoveryProcessor(
            repository.Object,
            service.Object,
            options.Object,
            NullLogger<PaymentRecoveryProcessor>.Instance);
    }

    [Fact]
    public async Task Recovers_every_stale_payment_and_counts_them()
    {
        var repository = new Mock<IPaymentRepository>();
        var stale = new List<PaymentDetail>
        {
            new() { ItemId = "payment-1", TenantId = TenantId },
            new() { ItemId = "payment-2", TenantId = TenantId }
        };
        repository.Setup(r => r.GetStaleInitiationsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale);
        var service = new Mock<IPaymentService>();
        service.Setup(s => s.RecoverAsync(It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processed = await Create(repository, service)
            .RecoverStaleAsync(TenantId, CancellationToken.None);

        processed.Should().Be(2);
        service.Verify(s => s.RecoverAsync(
            It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(2, 10, false)]     // 60s backoff, updated 10s ago: not due yet
    [InlineData(2, 70, true)]      // 60s backoff, updated 70s ago: due
    [InlineData(1, 31, true)]      // first retry waits 30s
    public async Task A_recurring_charge_with_an_unknown_outcome_waits_out_its_backoff(
        int attempts, int secondsSinceUpdate, bool retried)
    {
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(r => r.GetStaleInitiationsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentDetail>
            {
                new()
                {
                    ItemId = "payment-1",
                    TenantId = TenantId,
                    PaymentFlow = PaymentFlows.RecurringCharge,
                    PaymentStatus = PaymentStatuses.InitiationUnknown,
                    InitiationAttemptCount = attempts,
                    LastUpdatedDateUtc = DateTime.UtcNow.AddSeconds(-secondsSinceUpdate)
                }
            });
        var service = new Mock<IPaymentService>();

        await Create(repository, service).RecoverStaleAsync(TenantId, CancellationToken.None);

        service.Verify(s => s.RecoverAsync(
            It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()),
            retried ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task Another_flows_unknown_payment_is_not_held_to_the_recurring_backoff()
    {
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(r => r.GetStaleInitiationsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentDetail>
            {
                new()
                {
                    ItemId = "payment-1",
                    TenantId = TenantId,
                    PaymentFlow = PaymentFlows.HostedCheckout,
                    PaymentStatus = PaymentStatuses.InitiationUnknown,
                    InitiationAttemptCount = 5,
                    LastUpdatedDateUtc = DateTime.UtcNow
                }
            });
        var service = new Mock<IPaymentService>();

        await Create(repository, service).RecoverStaleAsync(TenantId, CancellationToken.None);

        service.Verify(s => s.RecoverAsync(
            It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Returns_zero_when_nothing_is_stale()
    {
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(r => r.GetStaleInitiationsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentDetail>());

        var processed = await Create(repository, new Mock<IPaymentService>())
            .RecoverStaleAsync(TenantId, CancellationToken.None);

        processed.Should().Be(0);
    }

    [Fact]
    public async Task Continues_after_a_recovery_failure_and_excludes_it_from_the_count()
    {
        var repository = new Mock<IPaymentRepository>();
        var failing = new PaymentDetail { ItemId = "payment-1", TenantId = TenantId };
        var succeeding = new PaymentDetail { ItemId = "payment-2", TenantId = TenantId };
        repository.Setup(r => r.GetStaleInitiationsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentDetail> { failing, succeeding });
        var service = new Mock<IPaymentService>();
        service.Setup(s => s.RecoverAsync(failing, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        service.Setup(s => s.RecoverAsync(succeeding, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processed = await Create(repository, service)
            .RecoverStaleAsync(TenantId, CancellationToken.None);

        processed.Should().Be(1);
    }

    [Fact]
    public async Task Honours_cancellation_between_items()
    {
        var repository = new Mock<IPaymentRepository>();
        using var cts = new CancellationTokenSource();
        repository.Setup(r => r.GetStaleInitiationsAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentDetail>
            {
                new() { ItemId = "payment-1", TenantId = TenantId }
            });
        var service = new Mock<IPaymentService>();
        service.Setup(s => s.RecoverAsync(It.IsAny<PaymentDetail>(), It.IsAny<CancellationToken>()))
            .Returns(async () => await cts.CancelAsync());

        await cts.CancelAsync();

        var act = () => Create(repository, service)
            .RecoverStaleAsync(TenantId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
