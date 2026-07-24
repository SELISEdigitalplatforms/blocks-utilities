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

public sealed class PaymentRefundReservationServiceTests
{
    private readonly Mock<IPaymentRefundRepository> _refunds = new();
    private readonly Mock<IPaymentRefundWebhookReferenceService> _references = new();
    private readonly Mock<IPaymentRefundResponseMapper> _responses = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();
    private readonly PaymentDetail _payment;
    private readonly PaymentProvider _provider = new() { ProviderName = "provider", MerchantId = "merchant" };
    private readonly CreatePaymentRefundRequest _request = new() { Amount = 10, Reason = "duplicate" };
    private readonly string _idempotencyKey = Guid.NewGuid().ToString();

    public PaymentRefundReservationServiceTests()
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

    private PaymentRefundReservationService CreateService() => new(
        _refunds.Object, _references.Object, _responses.Object, _options.Object);

    private Task<PaymentRefundReservationResult> RunAsync() =>
        CreateService().ReserveAsync(_payment, _provider, _request, "refund", _idempotencyKey, "corr", CancellationToken.None);

    private string ExpectedHash() => PaymentHashing.CreateRefundRequestHash(_payment.ItemId, _request);

    private PaymentDetail ExistingWith(PaymentRefund refund) => new()
    {
        ItemId = _payment.ItemId,
        TenantId = "tenant",
        Refunds = new List<PaymentRefund> { refund }
    };

    private void SetupReserve(bool ok) =>
        _refunds.Setup(r => r.TryReserveAsync("tenant", _payment.ItemId, It.IsAny<PaymentRefund>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ok);

    private void SetupExisting(PaymentDetail? existing) =>
        _refunds.Setup(r => r.GetPaymentByRefundIdempotencyKeyAsync("tenant", _idempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

    [Fact]
    public async Task ReserveAsync_ReferenceUnavailable_ReturnsTerminal()
    {
        _references.Setup(r => r.TryCreate("tenant", It.IsAny<string>(), out It.Ref<string>.IsAny)).Returns(false);

        var result = await RunAsync();

        result.CanSubmit.Should().BeFalse();
        result.TerminalResult!.ErrorCode.Should().Be("payment_refund_reference_unavailable");
    }

    [Fact]
    public async Task ReserveAsync_ReservationSucceeds_ReturnsSubmittableResult()
    {
        SetupReserve(true);

        var result = await RunAsync();

        result.CanSubmit.Should().BeTrue();
        result.Refund.Should().NotBeNull();
        result.Payment.Should().BeSameAs(_payment);
    }

    [Fact]
    public async Task ReserveAsync_ReserveFailsAndNoExisting_ReturnsConflict()
    {
        SetupReserve(false);
        SetupExisting(null);

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("payment_refund_not_available");
    }

    [Fact]
    public async Task ReserveAsync_ReserveFailsAndHashMismatch_ReturnsIdempotencyReuse()
    {
        SetupReserve(false);
        SetupExisting(ExistingWith(new PaymentRefund { RefundId = "ref", IdempotencyKey = _idempotencyKey, RequestHash = "different", Status = PaymentRefundStatuses.Initiating }));

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("idempotency_key_reused");
    }

    [Theory]
    [InlineData(PaymentRefundStatuses.Submitted)]
    [InlineData(PaymentRefundStatuses.Succeeded)]
    [InlineData(PaymentRefundStatuses.Reversed)]
    public async Task ReserveAsync_ReserveFailsAndAlreadyProcessed_ReturnsReplaySuccess(string status)
    {
        SetupReserve(false);
        var existingRefund = new PaymentRefund { RefundId = "ref", IdempotencyKey = _idempotencyKey, RequestHash = ExpectedHash(), Status = status };
        SetupExisting(ExistingWith(existingRefund));
        _responses.Setup(r => r.Map(_payment.ItemId, existingRefund)).Returns(new PaymentRefundResponse());

        var result = await RunAsync();

        result.TerminalResult!.IsSuccess.Should().BeTrue();
        result.TerminalResult.IsReplay.Should().BeTrue();
    }

    [Fact]
    public async Task ReserveAsync_ReserveFailsAndPreviousFailed_ReturnsProviderRejected()
    {
        SetupReserve(false);
        SetupExisting(ExistingWith(new PaymentRefund { RefundId = "ref", IdempotencyKey = _idempotencyKey, RequestHash = ExpectedHash(), Status = PaymentRefundStatuses.Failed, FailureCode = "declined" }));

        var result = await RunAsync();

        result.TerminalResult!.FailureKind.Should().Be(PaymentFailureKind.ProviderRejected);
        result.TerminalResult.ErrorCode.Should().Be("declined");
    }

    [Fact]
    public async Task ReserveAsync_ReserveFailsAndClaimFails_ReturnsInProgress()
    {
        SetupReserve(false);
        SetupExisting(ExistingWith(new PaymentRefund { RefundId = "ref", IdempotencyKey = _idempotencyKey, RequestHash = ExpectedHash(), Status = PaymentRefundStatuses.Initiating }));
        _refunds.Setup(r => r.TryClaimInitiationAsync("tenant", _payment.ItemId, "ref", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentRefund?)null);

        var result = await RunAsync();

        result.TerminalResult!.ErrorCode.Should().Be("payment_refund_in_progress");
    }

    [Fact]
    public async Task ReserveAsync_ReserveFailsAndClaimSucceeds_ReturnsRecoveredSubmittable()
    {
        SetupReserve(false);
        var existingPayment = ExistingWith(new PaymentRefund { RefundId = "ref", IdempotencyKey = _idempotencyKey, RequestHash = ExpectedHash(), Status = PaymentRefundStatuses.Initiating });
        SetupExisting(existingPayment);
        var claimed = new PaymentRefund { RefundId = "ref" };
        _refunds.Setup(r => r.TryClaimInitiationAsync("tenant", _payment.ItemId, "ref", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed);

        var result = await RunAsync();

        result.CanSubmit.Should().BeTrue();
        result.Payment.Should().BeSameAs(existingPayment);
        result.Refund.Should().BeSameAs(claimed);
    }

    private delegate void TryCreateCallback(string tenantId, string refundId, out string reference);
}
