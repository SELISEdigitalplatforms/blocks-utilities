using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.StoredPayment;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class RecurringPaymentInitiationServiceTests
{
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IStoredPaymentMethodRepository> _storedMethods = new();
    private readonly Mock<IPaymentProviderCache> _providers = new();
    private readonly Mock<IProviderTokenProtector> _tokenProtector = new();
    private readonly Mock<IPaymentWebhookReferenceService> _references = new();
    private readonly Mock<IStoredPaymentChargeRequestFactory> _requestFactory = new();
    private readonly Mock<IStoredPaymentChargeProviderGatewayResolver> _gatewayResolver = new();
    private readonly Mock<IStoredPaymentChargeProviderGateway> _gateway = new();
    private readonly Mock<IPaymentStateTransitionService> _stateTransitions = new();
    private readonly Mock<IPaymentResponseMapper> _responseMapper = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _minorUnits = new();
    private readonly Mock<IPaymentWorkDispatcher> _workDispatcher = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();

    private readonly StoredPaymentMethod _method = new() { ItemId = "method-1" };
    private readonly PaymentProvider _provider = new() { ProviderName = "provider", MerchantId = "merchant", IsEnabled = true };

    public RecurringPaymentInitiationServiceTests()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions());
        _stateTransitions.Setup(s => s.CompleteFailureAsync(
                It.IsAny<PaymentDetail>(), It.IsAny<string>(), It.IsAny<PaymentFailureKind>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail _, string _, PaymentFailureKind kind, string code, string msg, string corr, CancellationToken _) =>
                PaymentOperationResult.Failure(kind, code, msg, corr));
        _storedMethods.Setup(m => m.TryClaimForPaymentAsync(
                It.IsAny<string>(), "method-1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_method);
        _tokenProtector.Setup(t => t.UnprotectAsync(It.IsAny<StoredPaymentMethod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderTokenReadResult(true, "token"));
        _references.Setup(r => r.TryCreate(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Callback(new TryCreateCallback((string _, string _, out string reference) => reference = "provider-ref"))
            .Returns(true);
        _gatewayResolver.Setup(r => r.Resolve("provider")).Returns(_gateway.Object);
        _payments.Setup(p => p.SaveProviderRoutingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private RecurringPaymentInitiationService CreateService() => new(
        _payments.Object, _storedMethods.Object, _providers.Object, _tokenProtector.Object,
        _references.Object, _requestFactory.Object, _gatewayResolver.Object,
        new PaymentOutboxEventFactory(), _stateTransitions.Object, _responseMapper.Object,
        _minorUnits.Object, _workDispatcher.Object, _options.Object,
        NullLogger<RecurringPaymentInitiationService>.Instance);

    private static PaymentDetail Payment() => new()
    {
        ItemId = Guid.NewGuid().ToString(),
        TenantId = "tenant",
        CurrencyCode = "EUR",
        PreciseAmount = 10,
        ProviderName = "provider",
        ShopperReference = "shopper",
        StoredPaymentMethodPublicId = "public-1",
        IdempotencyKey = Guid.NewGuid().ToString()
    };

    private void SetupCharge(StoredPaymentChargeOutcome outcome, string? psp = null, string? resultCode = null) =>
        _gateway.Setup(g => g.ChargeAsync(It.IsAny<PaymentProvider>(), It.IsAny<StoredPaymentChargeRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredPaymentChargeProviderResult(outcome, psp, resultCode));

    private Task<PaymentOperationResult> InitiateAsync(PaymentDetail payment) =>
        CreateService().InitiateAsync(payment, _method, _provider, "lease", 1000, "corr", CancellationToken.None);

    [Fact]
    public async Task InitiateAsync_MethodClaimUnavailable_ReturnsConflict()
    {
        _storedMethods.Setup(m => m.TryClaimForPaymentAsync(
                It.IsAny<string>(), "method-1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoredPaymentMethod?)null);

        var result = await InitiateAsync(Payment());

        result.ErrorCode.Should().Be("stored_payment_method_in_use");
        _payments.Verify(p => p.MarkInitiationUnknownAsync("tenant", It.IsAny<string>(), "lease", "stored_payment_method_in_use", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiateAsync_TokenUnprotectFails_FailsAndReleasesClaim()
    {
        _tokenProtector.Setup(t => t.UnprotectAsync(It.IsAny<StoredPaymentMethod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProviderTokenReadResult.Failed);

        var result = await InitiateAsync(Payment());

        result.ErrorCode.Should().Be("stored_payment_method_token_unavailable");
        _storedMethods.Verify(m => m.ReleasePaymentClaimAsync("tenant", "method-1", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiateAsync_ReferenceUnavailable_Fails()
    {
        _references.Setup(r => r.TryCreate(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<string>.IsAny)).Returns(false);

        var result = await InitiateAsync(Payment());

        result.ErrorCode.Should().Be("payment_reference_unavailable");
    }

    [Fact]
    public async Task InitiateAsync_GatewayNull_Fails()
    {
        _gatewayResolver.Setup(r => r.Resolve("provider")).Returns((IStoredPaymentChargeProviderGateway?)null);

        var result = await InitiateAsync(Payment());

        result.ErrorCode.Should().Be("payment_provider_unavailable");
    }

    [Fact]
    public async Task InitiateAsync_SaveRoutingFails_ReturnsConflict()
    {
        _payments.Setup(p => p.SaveProviderRoutingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await InitiateAsync(Payment());

        result.ErrorCode.Should().Be("payment_state_conflict");
    }

    [Fact]
    public async Task InitiateAsync_ChargeAccepted_ReturnsSuccess()
    {
        var payment = Payment();
        SetupCharge(StoredPaymentChargeOutcome.Accepted, psp: "psp-1", resultCode: "ok");
        _payments.Setup(p => p.CompleteStoredPaymentChargeInitiationAsync(
                "tenant", payment.ItemId, "lease", "psp-1", "ok", It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _responseMapper.Setup(m => m.Map(payment)).Returns(new PaymentResponse());

        var result = await InitiateAsync(payment);

        result.IsSuccess.Should().BeTrue();
        payment.PaymentStatus.Should().Be(PaymentStatuses.Processing);
        payment.PspReference.Should().Be("psp-1");
        _workDispatcher.Verify(d => d.TryDispatchAsync("tenant", false, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiateAsync_ChargeAcceptedButNotUpdated_ReturnsConflict()
    {
        SetupCharge(StoredPaymentChargeOutcome.Accepted, psp: "psp-1");
        _payments.Setup(p => p.CompleteStoredPaymentChargeInitiationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PaymentOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await InitiateAsync(Payment());

        result.ErrorCode.Should().Be("payment_state_conflict");
    }

    [Fact]
    public async Task InitiateAsync_ChargeRejected_Fails()
    {
        SetupCharge(StoredPaymentChargeOutcome.Rejected);

        var result = await InitiateAsync(Payment());

        result.FailureKind.Should().Be(PaymentFailureKind.ProviderRejected);
        result.ErrorCode.Should().Be("recurring_payment_provider_rejected");
    }

    [Fact]
    public async Task InitiateAsync_ChargeUnavailable_MarksUnknownAndReturnsUnavailable()
    {
        SetupCharge(StoredPaymentChargeOutcome.Unavailable);

        var result = await InitiateAsync(Payment());

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_provider_unavailable");
        _payments.Verify(p => p.MarkInitiationUnknownAsync("tenant", It.IsAny<string>(), "lease", "payment_provider_unavailable", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiateAsync_ChargeTimeout_ReturnsTimeout()
    {
        SetupCharge(StoredPaymentChargeOutcome.Timeout);

        var result = await InitiateAsync(Payment());

        result.FailureKind.Should().Be(PaymentFailureKind.Timeout);
        result.ErrorCode.Should().Be("recurring_payment_initiation_unknown");
    }

    [Fact]
    public async Task InitiateAsync_ChargeUnknown_ReturnsProviderFailure()
    {
        SetupCharge(StoredPaymentChargeOutcome.OutcomeUnknown);

        var result = await InitiateAsync(Payment());

        result.FailureKind.Should().Be(PaymentFailureKind.ProviderFailure);
    }

    [Fact]
    public async Task RecoverAsync_MissingData_DoesNothing()
    {
        var payment = Payment();
        payment.StoredPaymentMethodPublicId = null;

        await CreateService().RecoverAsync(payment, CancellationToken.None);

        _payments.Verify(p => p.TryClaimInitiationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecoverAsync_ClaimNull_DoesNothing()
    {
        var payment = Payment();
        SetupMinorConvert(true);
        _payments.Setup(p => p.TryClaimInitiationAsync("tenant", payment.ItemId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        await CreateService().RecoverAsync(payment, CancellationToken.None);

        _providers.Verify(p => p.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<Task<PaymentProvider?>>>()), Times.Never);
    }

    [Fact]
    public async Task RecoverAsync_ProviderUnavailable_Fails()
    {
        var payment = Payment();
        SetupMinorConvert(true);
        var claimed = Payment();
        claimed.ProviderName = "provider";
        _payments.Setup(p => p.TryClaimInitiationAsync("tenant", payment.ItemId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed);
        _providers.Setup(p => p.GetAsync("tenant", It.IsAny<string>(), "provider", It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync((PaymentProvider?)null);

        await CreateService().RecoverAsync(payment, CancellationToken.None);

        _stateTransitions.Verify(s => s.CompleteFailureAsync(
            claimed, It.IsAny<string>(), PaymentFailureKind.Unavailable,
            "recurring_payment_recovery_unavailable", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupMinorConvert(bool ok) =>
        _minorUnits.Setup(m => m.TryConvert(It.IsAny<decimal>(), It.IsAny<string>(), out It.Ref<long>.IsAny))
            .Callback(new TryConvertCallback((decimal _, string _, out long value) => value = 1000))
            .Returns(ok);
    private delegate void TryCreateCallback(string tenantId, string paymentId, out string reference);
    private delegate void TryConvertCallback(decimal amount, string currency, out long value);
}
