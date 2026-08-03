using Blocks.Genesis;
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

public sealed class PaymentRefundOutboxProcessorTests
{
    private readonly Mock<IPaymentRefundRepository> _refunds = new();
    private readonly Mock<IMessageClient> _messageClient = new();
    private readonly Mock<IPaymentWorkDispatcher> _workDispatcher = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();

    public PaymentRefundOutboxProcessorTests()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions());
    }

    private PaymentRefundOutboxProcessor CreateService() => new(
        _refunds.Object, _messageClient.Object, _workDispatcher.Object, _options.Object,
        NullLogger<PaymentRefundOutboxProcessor>.Instance);

    private static PaymentOutboxEvent PendingEvent(int attemptCount = 0) => new()
    {
        EventId = "evt-1",
        EventType = "PaymentRefundRequested",
        Status = PaymentOutboxStatus.Pending,
        AttemptCount = attemptCount,
        NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1)
    };

    private static PaymentDetail PaymentWith(PaymentOutboxEvent evt) => new()
    {
        ItemId = "pay-1",
        TenantId = "tenant",
        Refunds = new List<PaymentRefund>
        {
            new() { RefundId = "ref-1", OutboxEvents = new List<PaymentOutboxEvent> { evt } }
        }
    };

    private void SetupDue(params PaymentDetail[] payments) =>
        _refunds.Setup(r => r.GetPaymentsWithDueRefundOutboxEventsAsync("tenant", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(payments.ToList());

    private void SetupClaim(bool claimed) =>
        _refunds.Setup(r => r.TryClaimOutboxEventAsync("tenant", "pay-1", "ref-1", "evt-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed);

    [Fact]
    public async Task PublishDueAsync_NoPayments_ReturnsZero()
    {
        SetupDue();

        var published = await CreateService().PublishDueAsync("tenant", CancellationToken.None);

        published.Should().Be(0);
    }

    [Fact]
    public async Task PublishDueAsync_ClaimFails_DoesNotPublish()
    {
        SetupDue(PaymentWith(PendingEvent()));
        SetupClaim(false);

        var published = await CreateService().PublishDueAsync("tenant", CancellationToken.None);

        published.Should().Be(0);
        _messageClient.Verify(m => m.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<PaymentLifecycleEvent>>()), Times.Never);
    }

    [Fact]
    public async Task PublishDueAsync_SendSucceeds_MarksPublished()
    {
        SetupDue(PaymentWith(PendingEvent()));
        SetupClaim(true);
        _messageClient.Setup(m => m.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<PaymentLifecycleEvent>>())).Returns(Task.CompletedTask);

        var published = await CreateService().PublishDueAsync("tenant", CancellationToken.None);

        published.Should().Be(1);
        _refunds.Verify(r => r.MarkOutboxPublishedAsync("tenant", "pay-1", "ref-1", "evt-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishDueAsync_SendThrows_SchedulesRetry()
    {
        SetupDue(PaymentWith(PendingEvent(attemptCount: 0)));
        SetupClaim(true);
        _messageClient.Setup(m => m.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<PaymentLifecycleEvent>>())).ThrowsAsync(new InvalidOperationException("broker down"));

        var published = await CreateService().PublishDueAsync("tenant", CancellationToken.None);

        published.Should().Be(0);
        _refunds.Verify(r => r.MarkOutboxFailedAsync("tenant", "pay-1", "ref-1", "evt-1", It.IsAny<string>(), PaymentOutboxStatus.RetryScheduled, 1, It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _workDispatcher.Verify(d => d.TryDispatchAsync("tenant", false, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishDueAsync_SendThrowsAtMaxAttempts_DeadLetters()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions { OutboxMaxAttempts = 3 });
        SetupDue(PaymentWith(PendingEvent(attemptCount: 2)));
        SetupClaim(true);
        _messageClient.Setup(m => m.SendToMassConsumerAsync(It.IsAny<ConsumerMessage<PaymentLifecycleEvent>>())).ThrowsAsync(new InvalidOperationException("broker down"));

        var published = await CreateService().PublishDueAsync("tenant", CancellationToken.None);

        published.Should().Be(0);
        _refunds.Verify(r => r.MarkOutboxFailedAsync("tenant", "pay-1", "ref-1", "evt-1", It.IsAny<string>(), PaymentOutboxStatus.DeadLettered, 3, It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _workDispatcher.Verify(d => d.TryDispatchAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishDueAsync_EventNotDue_IsSkipped()
    {
        var evt = PendingEvent();
        evt.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(30);
        SetupDue(PaymentWith(evt));

        var published = await CreateService().PublishDueAsync("tenant", CancellationToken.None);

        published.Should().Be(0);
        _refunds.Verify(r => r.TryClaimOutboxEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
