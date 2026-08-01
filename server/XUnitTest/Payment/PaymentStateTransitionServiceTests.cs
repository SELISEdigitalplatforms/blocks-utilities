using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentStateTransitionServiceTests
{
    private readonly Mock<IPaymentRepository> _repository = new();
    private readonly Mock<IPaymentResponseMapper> _responseMapper = new();
    private readonly Mock<IPaymentWorkDispatcher> _workDispatcher = new();

    private PaymentStateTransitionService CreateService() => new(
        _repository.Object,
        new PaymentOutboxEventFactory(),
        _responseMapper.Object,
        _workDispatcher.Object,
        NullLogger<PaymentStateTransitionService>.Instance);

    private static PaymentDetail Payment() => new()
    {
        ItemId = "pay-1",
        TenantId = "tenant",
        CurrencyCode = "EUR",
        PreciseAmount = 10
    };

    [Fact]
    public async Task ApplyProviderResultAsync_Success_CompletesInitiationAndMapsResponse()
    {
        var payment = Payment();
        var response = new PaymentResponse();
        var providerResult = new ProviderSessionCreationResult
        {
            Outcome = ProviderClientOutcome.Success,
            Response = new HostedCheckoutSessionResponse
            {
                Id = "sess-1",
                Url = "https://pay",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            }
        };
        _repository.Setup(r => r.CompleteInitiationAsync(
                "tenant", "pay-1", "lease", PaymentStatuses.Processing,
                "sess-1", null, "https://pay", It.IsAny<DateTime?>(), null,
                It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _responseMapper.Setup(m => m.Map(It.IsAny<PaymentDetail>())).Returns(response);

        var result = await CreateService().ApplyProviderResultAsync(payment, providerResult, "lease", "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Payment.Should().BeSameAs(response);
        payment.PaymentStatus.Should().Be(PaymentStatuses.Processing);
        payment.SessionId.Should().Be("sess-1");
        payment.RedirectUrl.Should().Be("https://pay");
        _workDispatcher.Verify(d => d.TryDispatchAsync("tenant", false, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyProviderResultAsync_Success_WhenNotUpdated_ReturnsConflict()
    {
        var providerResult = new ProviderSessionCreationResult
        {
            Outcome = ProviderClientOutcome.Success,
            Response = new HostedCheckoutSessionResponse { Id = "sess-1" }
        };
        _repository.Setup(r => r.CompleteInitiationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(),
                It.IsAny<string?>(), It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateService().ApplyProviderResultAsync(Payment(), providerResult, "lease", "corr", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("payment_state_conflict");
    }

    [Fact]
    public async Task ApplyProviderResultAsync_Rejected_CompletesFailure()
    {
        var providerResult = new ProviderSessionCreationResult { Outcome = ProviderClientOutcome.Rejected };
        _repository.Setup(r => r.CompleteInitiationAsync(
                "tenant", "pay-1", "lease", PaymentStatuses.MakePaymentFailed,
                null, null, null, null, "payment_provider_rejected",
                It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateService().ApplyProviderResultAsync(Payment(), providerResult, "lease", "corr", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.ProviderRejected);
        result.ErrorCode.Should().Be("payment_provider_rejected");
        _workDispatcher.Verify(d => d.TryDispatchAsync("tenant", false, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteFailureAsync_WhenNotUpdated_ReturnsConflict()
    {
        _repository.Setup(r => r.CompleteInitiationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(),
                It.IsAny<string?>(), It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateService().CompleteFailureAsync(
            Payment(), "lease", PaymentFailureKind.ProviderRejected, "code", "message", "corr", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        _workDispatcher.Verify(d => d.TryDispatchAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyProviderResultAsync_Unavailable_MarksUnknownAndReturnsUnavailable()
    {
        var providerResult = new ProviderSessionCreationResult { Outcome = ProviderClientOutcome.Unavailable };

        var result = await CreateService().ApplyProviderResultAsync(Payment(), providerResult, "lease", "corr", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_provider_unavailable");
        _repository.Verify(r => r.MarkInitiationUnknownAsync("tenant", "pay-1", "lease", "payment_provider_unavailable", It.IsAny<CancellationToken>()), Times.Once);
        _workDispatcher.Verify(d => d.TryDispatchAsync("tenant", true, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyProviderResultAsync_Timeout_MarksUnknownAndReturnsTimeout()
    {
        var providerResult = new ProviderSessionCreationResult { Outcome = ProviderClientOutcome.Timeout };

        var result = await CreateService().ApplyProviderResultAsync(Payment(), providerResult, "lease", "corr", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Timeout);
        result.ErrorCode.Should().Be("payment_initiation_unknown");
        _repository.Verify(r => r.MarkInitiationUnknownAsync("tenant", "pay-1", "lease", "payment_initiation_unknown", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyProviderResultAsync_Failure_MarksUnknownAndReturnsProviderFailure()
    {
        var providerResult = new ProviderSessionCreationResult { Outcome = ProviderClientOutcome.Failure };

        var result = await CreateService().ApplyProviderResultAsync(Payment(), providerResult, "lease", "corr", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.ProviderFailure);
        result.ErrorCode.Should().Be("payment_initiation_unknown");
    }
}
