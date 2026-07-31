using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentCapturePreflightServiceTests
{
    private readonly Mock<IValidator<CreatePaymentCaptureRequest>> _validator = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _minorUnits = new();
    private readonly Mock<IPaymentRateLimiter> _rateLimiter = new();
    private readonly Mock<IPaymentCaptureRepository> _captures = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentProviderCache> _providers = new();
    private readonly PaymentExecutionContext _context = new("tenant", "actor", null);
    private readonly string _paymentId = Guid.NewGuid().ToString();

    public PaymentCapturePreflightServiceTests()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<CreatePaymentCaptureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
    }

    private PaymentCapturePreflightService CreateService() => new(
        _validator.Object, _minorUnits.Object, _rateLimiter.Object,
        _captures.Object, _payments.Object, _providers.Object);

    private static CreatePaymentCaptureRequest Request(decimal amount = 10) => new() { Amount = amount };

    private PaymentDetail CapturablePayment() => new()
    {
        ItemId = _paymentId,
        TenantId = "tenant",
        PaymentStatus = PaymentStatuses.Authorized,
        PspReference = "psp",
        CaptureMode = "MANUAL",
        CurrencyCode = "EUR",
        ProviderName = "provider",
        AuthorizedAmount = 100,
        CapturedAmount = 0,
        ReservedCaptureAmount = 0
    };

    private static PaymentProvider EnabledProvider() => new()
    {
        ProviderName = "provider",
        IsEnabled = true,
        ApiKey = "key",
        MerchantId = "merchant"
    };

    private void SetupConvert(bool ok, long minorUnits = 1000) =>
        _minorUnits.Setup(c => c.TryConvert(It.IsAny<decimal>(), It.IsAny<string>(), out It.Ref<long>.IsAny))
            .Callback(new TryConvertCallback((decimal _, string _, out long value) => value = minorUnits))
            .Returns(ok);

    private void SetupRateLimit(PaymentRateLimitResult result) =>
        _rateLimiter.Setup(r => r.CheckAsync("tenant", "actor", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private static PaymentRateLimitResult Allowed() => new() { IsAvailable = true, IsAllowed = true };

    private async Task<PaymentCapturePreflightResult> RunAsync(string? paymentId = null, CreatePaymentCaptureRequest? request = null, string? key = null) =>
        await CreateService().ExecuteAsync(
            paymentId ?? _paymentId,
            request ?? Request(),
            key ?? Guid.NewGuid().ToString(),
            _context,
            "corr",
            CancellationToken.None);

    [Fact]
    public async Task ExecuteAsync_InvalidPaymentId_ReturnsFailure()
    {
        var result = await RunAsync(paymentId: "not-a-guid");

        result.Failure!.ErrorCode.Should().Be("invalid_payment_id");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidRequest_ReturnsValidationFailure()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<CreatePaymentCaptureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Amount", "bad") }));

        var result = await RunAsync();

        result.Failure!.ErrorCode.Should().Be("payment_capture_validation_failed");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidIdempotencyKey_ReturnsFailure()
    {
        var result = await RunAsync(key: "not-a-guid");

        result.Failure!.ErrorCode.Should().Be("invalid_idempotency_key");
    }

    [Fact]
    public async Task ExecuteAsync_PaymentNotFound_ReturnsNotFound()
    {
        _captures.Setup(c => c.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        var result = await RunAsync();

        result.Failure!.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        result.Failure.ErrorCode.Should().Be("payment_not_found");
    }

    [Fact]
    public async Task ExecuteAsync_PaymentNotCapturable_ReturnsConflict()
    {
        var payment = CapturablePayment();
        payment.PaymentStatus = PaymentStatuses.Processing;
        _captures.Setup(c => c.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(payment);

        var result = await RunAsync();

        result.Failure!.ErrorCode.Should().Be("payment_not_capturable");
    }

    [Fact]
    public async Task ExecuteAsync_AutomaticCapture_ReturnsConflict()
    {
        var payment = CapturablePayment();
        payment.CaptureMode = PaymentCaptureModes.AutomaticImmediate;
        _captures.Setup(c => c.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(payment);

        var result = await RunAsync();

        result.Failure!.ErrorCode.Should().Be("payment_capture_is_automatic");
    }

    [Fact]
    public async Task ExecuteAsync_AmountUnavailable_ReturnsConflict()
    {
        var payment = CapturablePayment();
        payment.AuthorizedAmount = 5;
        _captures.Setup(c => c.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(payment);

        var result = await RunAsync(request: Request(10));

        result.Failure!.ErrorCode.Should().Be("payment_capture_amount_unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_CurrencyConversionFails_ReturnsValidationFailure()
    {
        _captures.Setup(c => c.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(CapturablePayment());
        SetupConvert(ok: false);

        var result = await RunAsync();

        result.Failure!.ErrorCode.Should().Be("unsupported_currency_or_precision");
    }

    [Fact]
    public async Task ExecuteAsync_RateLimitExceeded_ReturnsRateLimited()
    {
        _captures.Setup(c => c.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(CapturablePayment());
        SetupConvert(ok: true);
        SetupRateLimit(new PaymentRateLimitResult { IsAvailable = true, IsAllowed = false });

        var result = await RunAsync();

        result.Failure!.FailureKind.Should().Be(PaymentFailureKind.RateLimited);
        result.Failure.ErrorCode.Should().Be("payment_capture_rate_limit_exceeded");
    }

    [Fact]
    public async Task ExecuteAsync_ProviderUnavailable_ReturnsUnavailable()
    {
        _captures.Setup(c => c.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(CapturablePayment());
        SetupConvert(ok: true);
        SetupRateLimit(Allowed());
        _providers.Setup(p => p.GetAsync("tenant", It.IsAny<string>(), "provider", It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync((PaymentProvider?)null);

        var result = await RunAsync();

        result.Failure!.ErrorCode.Should().Be("payment_provider_unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_AllChecksPass_ReturnsSuccess()
    {
        var payment = CapturablePayment();
        _captures.Setup(c => c.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        SetupConvert(ok: true, minorUnits: 1000);
        SetupRateLimit(Allowed());
        var provider = EnabledProvider();
        _providers.Setup(p => p.GetAsync("tenant", It.IsAny<string>(), "provider", It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(provider);

        var result = await RunAsync();

        result.IsSuccess.Should().BeTrue();
        result.MinorUnits.Should().Be(1000);
        result.Payment.Should().BeSameAs(payment);
        result.Provider.Should().BeSameAs(provider);
    }

    private delegate void TryConvertCallback(decimal amount, string currency, out long value);
}
