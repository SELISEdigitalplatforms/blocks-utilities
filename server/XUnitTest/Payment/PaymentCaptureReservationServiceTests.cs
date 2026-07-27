using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentCaptureReservationServiceTests
{
    private readonly Mock<IPaymentCaptureRepository> _captures = new();
    private readonly Mock<IPaymentCaptureWebhookReferenceService> _references = new();
    private readonly Mock<IPaymentCaptureResponseMapper> _responses = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();
    private readonly PaymentDetail _payment;
    private readonly PaymentProvider _provider = new() { ProviderName = "provider", MerchantId = "merchant" };
    private readonly CreatePaymentCaptureRequest _request = new() { Amount = 10 };
    private readonly string _idempotencyKey = Guid.NewGuid().ToString();

    public PaymentCaptureReservationServiceTests()
    {
        _payment = new PaymentDetail
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = "tenant",
            CurrencyCode = "EUR",
            PspReference = "psp"
        };
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions());
        _references.Setup(r => r.TryCreate("tenant", It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Callback(new TryCreateCallback((string _, string _, out string reference) => reference = "provider-ref"))
            .Returns(true);
    }

    private PaymentCaptureReservationService CreateService() => new(
        _captures.Object, _references.Object, _responses.Object, _options.Object);

    private Task<PaymentCaptureReservationResult> RunAsync() =>
        CreateService().ReserveAsync(_payment, _provider, _request, _idempotencyKey, "corr", CancellationToken.None);

    private string ExpectedHash() => PaymentHashing.CreateCaptureRequestHash(_payment.ItemId, _request);

    private PaymentDetail ExistingWith(PaymentCapture capture) => new()
    {
        ItemId = _payment.ItemId,
        TenantId = "tenant",
        Captures = new List<PaymentCapture> { capture }
    };

    [Fact]
    public async Task ReserveAsync_ReferenceUnavailable_ReturnsTerminal()
    {
        _references.Setup(r => r.TryCreate("tenant", It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Returns(false);

        var result = await RunAsync();

        result.CanSubmit.Should().BeFalse();
        result.TerminalResult!.ErrorCode.Should().Be("payment_capture_reference_unavailable");
    }

    [Fact]
    public async Task ReserveAsync_ReservationSucceeds_ReturnsSubmittableResult()
    {
        _captures.Setup(c => c.TryReserveAsync("tenant", _payment.ItemId, It.IsAny<PaymentCapture>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await RunAsync();

        result.CanSubmit.Should().BeTrue();
        result.Capture.Should().NotBeNull();
        result.LeaseId.Should().NotBeNullOrEmpty();
        result.Payment.Should().BeSameAs(_payment);
    }

    [Fact]
    public async Task ReserveAsync_ReserveFailsAndNoExisting_ReturnsConflict()
    {
        _captures.Setup(c => c.TryReserveAsync("tenant", _payment.ItemId, It.IsAny<PaymentCapture>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _captures.Setup(c => c.GetPaymentByIdempotencyKeyAsync("tenant", _idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("payment_capture_not_available");
    }

    [Fact]
    public async Task ReserveAsync_ReserveFailsAndHashMismatch_ReturnsIdempotencyReuse()
    {
        _captures.Setup(c => c.TryReserveAsync("tenant", _payment.ItemId, It.IsAny<PaymentCapture>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var existingCapture = new PaymentCapture { CaptureId = "cap", IdempotencyKey = _idempotencyKey, RequestHash = "different", Status = PaymentCaptureStatuses.Initiating };
        _captures.Setup(c => c.GetPaymentByIdempotencyKeyAsync("tenant", _idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingWith(existingCapture));

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("idempotency_key_reused");
    }

    [Theory]
    [InlineData(PaymentCaptureStatuses.Submitted)]
    [InlineData(PaymentCaptureStatuses.Succeeded)]
    public async Task ReserveAsync_ReserveFailsAndAlreadyProcessed_ReturnsReplaySuccess(string status)
    {
        _captures.Setup(c => c.TryReserveAsync("tenant", _payment.ItemId, It.IsAny<PaymentCapture>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var existingCapture = new PaymentCapture { CaptureId = "cap", IdempotencyKey = _idempotencyKey, RequestHash = ExpectedHash(), Status = status };
        _captures.Setup(c => c.GetPaymentByIdempotencyKeyAsync("tenant", _idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingWith(existingCapture));
        _responses.Setup(r => r.Map(_payment.ItemId, existingCapture)).Returns(new PaymentCaptureResponse());

        var result = await RunAsync();

        result.CanSubmit.Should().BeFalse();
        result.TerminalResult!.IsSuccess.Should().BeTrue();
        result.TerminalResult.IsReplay.Should().BeTrue();
    }

    [Fact]
    public async Task ReserveAsync_ReserveFailsAndPreviousFailed_ReturnsProviderRejected()
    {
        _captures.Setup(c => c.TryReserveAsync("tenant", _payment.ItemId, It.IsAny<PaymentCapture>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var existingCapture = new PaymentCapture { CaptureId = "cap", IdempotencyKey = _idempotencyKey, RequestHash = ExpectedHash(), Status = PaymentCaptureStatuses.Failed, FailureCode = "declined" };
        _captures.Setup(c => c.GetPaymentByIdempotencyKeyAsync("tenant", _idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingWith(existingCapture));

        var result = await RunAsync();

        result.TerminalResult!.FailureKind.Should().Be(PaymentFailureKind.ProviderRejected);
        result.TerminalResult.ErrorCode.Should().Be("declined");
    }

    [Fact]
    public async Task ReserveAsync_ReserveFailsAndClaimFails_ReturnsInProgress()
    {
        _captures.Setup(c => c.TryReserveAsync("tenant", _payment.ItemId, It.IsAny<PaymentCapture>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var existingCapture = new PaymentCapture { CaptureId = "cap", IdempotencyKey = _idempotencyKey, RequestHash = ExpectedHash(), Status = PaymentCaptureStatuses.Initiating };
        _captures.Setup(c => c.GetPaymentByIdempotencyKeyAsync("tenant", _idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingWith(existingCapture));
        _captures.Setup(c => c.TryClaimInitiationAsync("tenant", _payment.ItemId, "cap", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentCapture?)null);

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("payment_capture_in_progress");
    }

    [Fact]
    public async Task ReserveAsync_ReserveFailsAndClaimSucceeds_ReturnsRecoveredSubmittable()
    {
        _captures.Setup(c => c.TryReserveAsync("tenant", _payment.ItemId, It.IsAny<PaymentCapture>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var existingCapture = new PaymentCapture { CaptureId = "cap", IdempotencyKey = _idempotencyKey, RequestHash = ExpectedHash(), Status = PaymentCaptureStatuses.Initiating };
        var existingPayment = ExistingWith(existingCapture);
        _captures.Setup(c => c.GetPaymentByIdempotencyKeyAsync("tenant", _idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPayment);
        var claimed = new PaymentCapture { CaptureId = "cap" };
        _captures.Setup(c => c.TryClaimInitiationAsync("tenant", _payment.ItemId, "cap", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed);

        var result = await RunAsync();

        result.CanSubmit.Should().BeTrue();
        result.Payment.Should().BeSameAs(existingPayment);
        result.Capture.Should().BeSameAs(claimed);
    }

    private delegate void TryCreateCallback(string tenantId, string captureId, out string reference);
}
