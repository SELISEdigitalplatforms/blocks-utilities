using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentWebhookProcessorTests
{
    private readonly Mock<IPaymentWebhookInboxRepository> _inbox = new();
    private readonly Mock<IPaymentWebhookStateTransitionService> _transitions = new();
    private readonly Mock<IPaymentWorkDispatcher> _workDispatcher = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();

    public PaymentWebhookProcessorTests()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions());
    }

    private PaymentWebhookProcessor CreateService() => new(
        _inbox.Object, _transitions.Object, _workDispatcher.Object, _options.Object,
        NullLogger<PaymentWebhookProcessor>.Instance);

    private static PaymentWebhookInbox Candidate(int attemptCount = 0) => new()
    {
        WebhookId = "wh-1",
        TenantId = "tenant",
        WebhookType = "standard",
        EventCode = "AUTHORISATION",
        AttemptCount = attemptCount,
        Status = PaymentWebhookStatus.Pending,
        NormalizedPayload = new PaymentWebhookPayload { PaymentDetailId = "pay-1" }
    };

    private void SetupDue(params PaymentWebhookInbox[] due) =>
        _inbox.Setup(i => i.GetDueAsync("tenant", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(due.ToList());

    [Fact]
    public async Task ProcessDueAsync_NoDue_ReturnsZero()
    {
        SetupDue();

        var result = await CreateService().ProcessDueAsync("tenant", CancellationToken.None);

        result.ProcessedCount.Should().Be(0);
        _inbox.Verify(i => i.TryClaimAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessDueAsync_ClaimSkipped_DoesNotProcess()
    {
        SetupDue(Candidate());
        _inbox.Setup(i => i.TryClaimAsync("tenant", "wh-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentWebhookInbox?)null);

        var result = await CreateService().ProcessDueAsync("tenant", CancellationToken.None);

        result.ProcessedCount.Should().Be(0);
        _transitions.Verify(t => t.ApplyAsync(It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessDueAsync_TransitionSucceeds_MarksProcessed()
    {
        var candidate = Candidate();
        SetupDue(candidate);
        _inbox.Setup(i => i.TryClaimAsync("tenant", "wh-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var result = await CreateService().ProcessDueAsync("tenant", CancellationToken.None);

        result.ProcessedCount.Should().Be(1);
        _inbox.Verify(i => i.MarkProcessedAsync("tenant", "wh-1", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessDueAsync_TransitionThrows_SchedulesRetry()
    {
        var candidate = Candidate(attemptCount: 0);
        SetupDue(candidate);
        _inbox.Setup(i => i.TryClaimAsync("tenant", "wh-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        _transitions.Setup(t => t.ApplyAsync(candidate, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        var result = await CreateService().ProcessDueAsync("tenant", CancellationToken.None);

        result.ProcessedCount.Should().Be(0);
        _inbox.Verify(i => i.MarkFailedAsync("tenant", "wh-1", It.IsAny<string>(), PaymentWebhookStatus.RetryScheduled, 1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _workDispatcher.Verify(d => d.TryDispatchAsync("tenant", false, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessDueAsync_TransitionThrowsAtMaxAttempts_DeadLetters()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions { WebhookMaxAttempts = 3 });
        var candidate = Candidate(attemptCount: 2);
        SetupDue(candidate);
        _inbox.Setup(i => i.TryClaimAsync("tenant", "wh-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        _transitions.Setup(t => t.ApplyAsync(candidate, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        var result = await CreateService().ProcessDueAsync("tenant", CancellationToken.None);

        result.ProcessedCount.Should().Be(0);
        _inbox.Verify(i => i.MarkFailedAsync("tenant", "wh-1", It.IsAny<string>(), PaymentWebhookStatus.DeadLettered, 3, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _workDispatcher.Verify(d => d.TryDispatchAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Why the ids are reported at all: the worker joins them to the subscription waiting on the
    /// payment and activates it in this same tick, instead of leaving it to a repair sweep two
    /// minutes out.
    /// </summary>
    [Fact]
    public async Task ProcessDueAsync_TransitionSucceeds_ReportsTheTransitionedPayment()
    {
        var candidate = Candidate();
        SetupDue(candidate);
        _inbox.Setup(i => i.TryClaimAsync("tenant", "wh-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var result = await CreateService().ProcessDueAsync("tenant", CancellationToken.None);

        result.TransitionedPaymentDetailIds.Should().Equal("pay-1");
    }

    /// <summary>
    /// Two events for one payment in a single batch is ordinary — an authorisation and its
    /// capture, say — and the caller should look its link up once.
    /// </summary>
    [Fact]
    public async Task ProcessDueAsync_TwoEventsForOnePayment_ReportsItOnce()
    {
        var first = Candidate();
        var second = Candidate();
        second.WebhookId = "wh-2";
        second.EventCode = "CAPTURE";
        SetupDue(first, second);
        _inbox.Setup(i => i.TryClaimAsync("tenant", "wh-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(first);
        _inbox.Setup(i => i.TryClaimAsync("tenant", "wh-2", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(second);

        var result = await CreateService().ProcessDueAsync("tenant", CancellationToken.None);

        result.ProcessedCount.Should().Be(2);
        result.TransitionedPaymentDetailIds.Should().Equal("pay-1");
    }

    [Fact]
    public async Task ProcessDueAsync_ClaimSkipped_ReportsNoTransitionedPayments()
    {
        SetupDue(Candidate());
        _inbox.Setup(i => i.TryClaimAsync("tenant", "wh-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentWebhookInbox?)null);

        var result = await CreateService().ProcessDueAsync("tenant", CancellationToken.None);

        result.TransitionedPaymentDetailIds.Should().BeEmpty();
    }

    /// <summary>
    /// An id reported for a transition that failed would send the worker looking for a
    /// confirmation that does not exist.
    /// </summary>
    [Fact]
    public async Task ProcessDueAsync_TransitionThrows_ReportsNoTransitionedPayments()
    {
        var candidate = Candidate();
        SetupDue(candidate);
        _inbox.Setup(i => i.TryClaimAsync("tenant", "wh-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);
        _transitions.Setup(t => t.ApplyAsync(candidate, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));

        var result = await CreateService().ProcessDueAsync("tenant", CancellationToken.None);

        result.TransitionedPaymentDetailIds.Should().BeEmpty();
    }

    /// <summary>A webhook that names no payment — a dispute or account event — reports nothing.</summary>
    [Fact]
    public async Task ProcessDueAsync_WebhookWithoutAPaymentId_ReportsNoTransitionedPayments()
    {
        var candidate = Candidate();
        candidate.NormalizedPayload.PaymentDetailId = null;
        SetupDue(candidate);
        _inbox.Setup(i => i.TryClaimAsync("tenant", "wh-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidate);

        var result = await CreateService().ProcessDueAsync("tenant", CancellationToken.None);

        result.ProcessedCount.Should().Be(1);
        result.TransitionedPaymentDetailIds.Should().BeEmpty();
    }
}
