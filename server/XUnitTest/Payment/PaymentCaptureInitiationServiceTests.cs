using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.Captures;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentCaptureInitiationServiceTests
{
    private readonly Mock<IPaymentCaptureProviderGatewayResolver> _gateways = new();
    private readonly Mock<IPaymentCaptureProviderGateway> _gateway = new();
    private readonly Mock<IPaymentCaptureRequestFactory> _requests = new();
    private readonly Mock<IPaymentCaptureRepository> _captures = new();
    private readonly Mock<IPaymentCaptureResponseMapper> _responses = new();
    private readonly Mock<IPaymentWorkDispatcher> _workDispatcher = new();

    private readonly PaymentDetail _payment = new() { ItemId = "pay-1", TenantId = "tenant" };
    private readonly PaymentCapture _capture = new()
    {
        CaptureId = "cap-1",
        ProviderName = "provider",
        OriginalPaymentPspReference = "psp",
        IdempotencyKey = "idem",
        Amount = 10
    };

    public PaymentCaptureInitiationServiceTests()
    {
        _gateways.Setup(g => g.Resolve("provider")).Returns(_gateway.Object);
    }

    private PaymentCaptureInitiationService CreateService() => new(
        _gateways.Object, _requests.Object, _captures.Object,
        new PaymentCaptureOutboxEventFactory(), _responses.Object, _workDispatcher.Object);

    private void SetupSubmit(PaymentCaptureProviderOutcome outcome, string? reference = null, string? safeError = null) =>
        _gateway.Setup(g => g.SubmitAsync(It.IsAny<PaymentProvider>(), "psp", It.IsAny<ProviderCaptureRequest>(), "idem", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentCaptureProviderResult(outcome, reference, "provider-status", safeError));

    private Task<PaymentCaptureOperationResult> RunAsync() =>
        CreateService().SubmitAsync(_payment, _capture, new PaymentProvider(), "lease", 1000, "corr", CancellationToken.None);

    [Fact]
    public async Task SubmitAsync_GatewayNull_MarksUnknownUnavailable()
    {
        _gateways.Setup(g => g.Resolve("provider")).Returns((IPaymentCaptureProviderGateway?)null);

        var result = await RunAsync();

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_provider_unavailable");
        _captures.Verify(c => c.MarkInitiationUnknownAsync("tenant", "pay-1", "cap-1", "lease", "payment_provider_unavailable", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_Submitted_ReturnsSuccessAndUpdatesCapture()
    {
        SetupSubmit(PaymentCaptureProviderOutcome.Submitted, reference: "cap-ref");
        _captures.Setup(c => c.CompleteSubmissionAsync("tenant", "pay-1", "cap-1", "lease", "cap-ref", "provider-status", It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _responses.Setup(r => r.Map("pay-1", _capture)).Returns(new PaymentCaptureResponse());

        var result = await RunAsync();

        result.IsSuccess.Should().BeTrue();
        _capture.Status.Should().Be(PaymentCaptureStatuses.Submitted);
        _capture.ProviderCaptureReference.Should().Be("cap-ref");
        _workDispatcher.Verify(d => d.TryDispatchAsync("tenant", false, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_SubmittedButNotUpdated_ReturnsConflict()
    {
        SetupSubmit(PaymentCaptureProviderOutcome.Submitted, reference: "cap-ref");
        _captures.Setup(c => c.CompleteSubmissionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await RunAsync();

        result.ErrorCode.Should().Be("payment_capture_state_conflict");
    }

    [Fact]
    public async Task SubmitAsync_Rejected_ReturnsProviderRejectedWithSafeCode()
    {
        SetupSubmit(PaymentCaptureProviderOutcome.Rejected, safeError: "declined");
        _captures.Setup(c => c.CompleteRejectionAsync("tenant", "pay-1", "cap-1", "lease", 10, "declined", It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await RunAsync();

        result.FailureKind.Should().Be(PaymentFailureKind.ProviderRejected);
        result.ErrorCode.Should().Be("declined");
    }

    [Fact]
    public async Task SubmitAsync_RejectedButNotUpdated_ReturnsConflict()
    {
        SetupSubmit(PaymentCaptureProviderOutcome.Rejected);
        _captures.Setup(c => c.CompleteRejectionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await RunAsync();

        result.ErrorCode.Should().Be("payment_capture_state_conflict");
    }

    [Fact]
    public async Task SubmitAsync_Timeout_ReturnsTimeout()
    {
        SetupSubmit(PaymentCaptureProviderOutcome.Timeout);

        var result = await RunAsync();

        result.FailureKind.Should().Be(PaymentFailureKind.Timeout);
        result.ErrorCode.Should().Be("payment_capture_initiation_unknown");
    }

    [Fact]
    public async Task SubmitAsync_OutcomeUnknown_ReturnsProviderFailure()
    {
        SetupSubmit(PaymentCaptureProviderOutcome.OutcomeUnknown);

        var result = await RunAsync();

        result.FailureKind.Should().Be(PaymentFailureKind.ProviderFailure);
    }
}
