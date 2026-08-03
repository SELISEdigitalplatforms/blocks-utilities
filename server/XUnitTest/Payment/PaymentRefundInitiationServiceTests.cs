using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.Refunds;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentRefundInitiationServiceTests
{
    private readonly Mock<IPaymentRefundProviderGatewayResolver> _gateways = new();
    private readonly Mock<IPaymentRefundProviderGateway> _gateway = new();
    private readonly Mock<IPaymentRefundRequestFactory> _requestFactory = new();
    private readonly Mock<IPaymentRefundRepository> _refunds = new();
    private readonly Mock<IPaymentRefundResponseMapper> _responses = new();
    private readonly Mock<IPaymentWorkDispatcher> _workDispatcher = new();

    private readonly PaymentDetail _payment = new() { ItemId = "pay-1", TenantId = "tenant" };
    private readonly PaymentRefund _refund = new()
    {
        RefundId = "ref-1",
        ProviderName = "provider",
        OriginalPaymentPspReference = "psp",
        IdempotencyKey = "idem",
        Amount = 10,
        ProviderOperation = "refund"
    };

    public PaymentRefundInitiationServiceTests()
    {
        _gateways.Setup(g => g.Resolve("provider")).Returns(_gateway.Object);
    }

    private PaymentRefundInitiationService CreateService() => new(
        _gateways.Object, _requestFactory.Object, _refunds.Object,
        new PaymentRefundOutboxEventFactory(), _responses.Object, _workDispatcher.Object,
        NullLogger<PaymentRefundInitiationService>.Instance);

    private void SetupSubmit(PaymentRefundProviderOutcome outcome, string? reference = null) =>
        _gateway.Setup(g => g.SubmitAsync(It.IsAny<PaymentProvider>(), "psp", It.IsAny<ProviderRefundRequest>(), "idem", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRefundProviderResult(outcome, reference, "provider-status"));

    private Task<PaymentRefundOperationResult> RunAsync() =>
        CreateService().SubmitAsync(_payment, _refund, new PaymentProvider(), "lease", 1000, "corr", CancellationToken.None);

    [Fact]
    public async Task SubmitAsync_GatewayNull_MarksUnknownUnavailable()
    {
        _gateways.Setup(g => g.Resolve("provider")).Returns((IPaymentRefundProviderGateway?)null);

        var result = await RunAsync();

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_provider_unavailable");
        _refunds.Verify(r => r.MarkInitiationUnknownAsync("tenant", "pay-1", "ref-1", "lease", "payment_provider_unavailable", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_ReversalOperation_UsesReversalGateway()
    {
        _refund.ProviderOperation = PaymentFundReturnOperations.Reversal;
        _gateway.Setup(g => g.SubmitReversalAsync(It.IsAny<PaymentProvider>(), "psp", It.IsAny<ProviderReversalRequest>(), "idem", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRefundProviderResult(PaymentRefundProviderOutcome.Submitted, "ref-ext", "provider-status"));
        _refunds.Setup(r => r.CompleteSubmissionAsync("tenant", "pay-1", "ref-1", "lease", "ref-ext", "provider-status", It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _responses.Setup(r => r.Map("pay-1", _refund)).Returns(new PaymentRefundResponse());

        var result = await RunAsync();

        result.IsSuccess.Should().BeTrue();
        _gateway.Verify(g => g.SubmitReversalAsync(It.IsAny<PaymentProvider>(), "psp", It.IsAny<ProviderReversalRequest>(), "idem", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_Submitted_ReturnsSuccessAndUpdatesRefund()
    {
        SetupSubmit(PaymentRefundProviderOutcome.Submitted, reference: "ref-ext");
        _refunds.Setup(r => r.CompleteSubmissionAsync("tenant", "pay-1", "ref-1", "lease", "ref-ext", "provider-status", It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _responses.Setup(r => r.Map("pay-1", _refund)).Returns(new PaymentRefundResponse());

        var result = await RunAsync();

        result.IsSuccess.Should().BeTrue();
        _refund.Status.Should().Be(PaymentRefundStatuses.Submitted);
        _refund.ProviderRefundReference.Should().Be("ref-ext");
        _workDispatcher.Verify(d => d.TryDispatchAsync("tenant", false, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Cancelling an uncaptured authorization creates no object at the provider, so the event
    /// it raises names the payment and can never identify this reversal. Leaving it submitted
    /// meant waiting forever, and the payment stayed authorised while the hold was released.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_ReversalSettled_CancelsThePaymentWithoutAwaitingAnEvent()
    {
        _refund.ProviderOperation = PaymentFundReturnOperations.Reversal;
        _gateway.Setup(g => g.SubmitReversalAsync(
                It.IsAny<PaymentProvider>(), "psp", It.IsAny<ProviderReversalRequest>(),
                "idem", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRefundProviderResult(
                PaymentRefundProviderOutcome.Settled, "pi_1", "canceled"));
        _refunds.Setup(r => r.ApplyProviderEventAsync(
                "tenant", "pay-1", "ref-1",
                It.IsAny<IReadOnlyCollection<string>>(),
                PaymentRefundStatuses.Succeeded,
                "pi_1",
                It.IsAny<DateTime>(),
                -10,
                // Nothing was ever captured, so nothing counts as refunded.
                0,
                PaymentStatuses.Cancelled,
                "cancel",
                null,
                null,
                It.IsAny<PaymentOutboxEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _responses.Setup(r => r.Map("pay-1", _refund)).Returns(new PaymentRefundResponse());

        var result = await RunAsync();

        result.IsSuccess.Should().BeTrue();
        _refund.Status.Should().Be(PaymentRefundStatuses.Succeeded);
        _refund.CompletedAtUtc.Should().NotBeNull();
        _refunds.Verify(r => r.CompleteSubmissionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PaymentOutboxEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_ReversalSettledButNotApplied_ReturnsConflict()
    {
        _refund.ProviderOperation = PaymentFundReturnOperations.Reversal;
        _gateway.Setup(g => g.SubmitReversalAsync(
                It.IsAny<PaymentProvider>(), "psp", It.IsAny<ProviderReversalRequest>(),
                "idem", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRefundProviderResult(
                PaymentRefundProviderOutcome.Settled, "pi_1", "canceled"));
        _refunds.Setup(r => r.ApplyProviderEventAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await RunAsync();

        result.ErrorCode.Should().Be("payment_refund_state_conflict");
    }

    [Fact]
    public async Task SubmitAsync_SubmittedButNotUpdated_ReturnsConflict()
    {
        SetupSubmit(PaymentRefundProviderOutcome.Submitted, reference: "ref-ext");
        _refunds.Setup(r => r.CompleteSubmissionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await RunAsync();

        result.ErrorCode.Should().Be("payment_refund_state_conflict");
    }

    [Fact]
    public async Task SubmitAsync_Rejected_ReturnsProviderRejected()
    {
        SetupSubmit(PaymentRefundProviderOutcome.Rejected);
        _refunds.Setup(r => r.CompleteRejectionAsync("tenant", "pay-1", "ref-1", "lease", 10, "payment_refund_provider_rejected", It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await RunAsync();

        result.FailureKind.Should().Be(PaymentFailureKind.ProviderRejected);
        result.ErrorCode.Should().Be("payment_refund_provider_rejected");
    }

    [Fact]
    public async Task SubmitAsync_RejectedButNotUpdated_ReturnsConflict()
    {
        SetupSubmit(PaymentRefundProviderOutcome.Rejected);
        _refunds.Setup(r => r.CompleteRejectionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await RunAsync();

        result.ErrorCode.Should().Be("payment_refund_state_conflict");
    }

    [Fact]
    public async Task SubmitAsync_Timeout_ReturnsTimeout()
    {
        SetupSubmit(PaymentRefundProviderOutcome.Timeout);

        var result = await RunAsync();

        result.FailureKind.Should().Be(PaymentFailureKind.Timeout);
        result.ErrorCode.Should().Be("payment_refund_initiation_unknown");
    }

    [Fact]
    public async Task SubmitAsync_OutcomeUnknown_ReturnsProviderFailure()
    {
        SetupSubmit(PaymentRefundProviderOutcome.OutcomeUnknown);

        var result = await RunAsync();

        result.FailureKind.Should().Be(PaymentFailureKind.ProviderFailure);
    }
}
