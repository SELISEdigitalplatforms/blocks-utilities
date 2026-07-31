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

public sealed class PaymentRefundPreflightServiceTests
{
    private readonly Mock<IValidator<CreatePaymentRefundRequest>> _validator = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _minorUnits = new();
    private readonly Mock<IPaymentRateLimiter> _rateLimiter = new();
    private readonly Mock<IPaymentRefundRepository> _refunds = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentProviderCache> _providers = new();
    private readonly Mock<IPaymentFundReturnStrategyResolver> _strategies = new();
    private readonly PaymentExecutionContext _context = new("tenant", "actor", null);
    private readonly string _paymentId = Guid.NewGuid().ToString();

    public PaymentRefundPreflightServiceTests()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<CreatePaymentRefundRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _strategies.Setup(s => s.Resolve(It.IsAny<PaymentDetail>(), It.IsAny<decimal>()))
            .Returns(new PaymentFundReturnDecision(true, "refund"));
    }

    private PaymentRefundPreflightService CreateService() => new(
        _validator.Object, _minorUnits.Object, _rateLimiter.Object,
        _refunds.Object, _payments.Object, _providers.Object, _strategies.Object);

    private static CreatePaymentRefundRequest Request(decimal amount = 10) => new() { Amount = amount };

    private PaymentDetail RefundablePayment() => new()
    {
        ItemId = _paymentId,
        TenantId = "tenant",
        PaymentStatus = PaymentStatuses.Captured,
        PspReference = "psp",
        CurrencyCode = "EUR",
        ProviderName = "provider",
        PaymentDate = DateTime.UtcNow
    };

    private static PaymentProvider EnabledProvider(int maxRefundDays = 0) => new()
    {
        ProviderName = "provider",
        IsEnabled = true,
        ApiKey = "key",
        MerchantId = "merchant",
        MaxRefundDays = maxRefundDays
    };

    private void SetupConvert(bool ok, long minorUnits = 1000) =>
        _minorUnits.Setup(c => c.TryConvert(It.IsAny<decimal>(), It.IsAny<string>(), out It.Ref<long>.IsAny))
            .Callback(new TryConvertCallback((decimal _, string _, out long value) => value = minorUnits))
            .Returns(ok);

    private void SetupRateLimit(PaymentRateLimitResult result) =>
        _rateLimiter.Setup(r => r.CheckAsync("tenant", "actor", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private static PaymentRateLimitResult Allowed() => new() { IsAvailable = true, IsAllowed = true };

    private void SetupProvider(PaymentProvider? provider) =>
        _providers.Setup(p => p.GetAsync("tenant", It.IsAny<string>(), "provider", It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(provider);

    private async Task<PaymentRefundPreflightResult> RunAsync(string? paymentId = null, CreatePaymentRefundRequest? request = null, string? key = null) =>
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
        _validator.Setup(v => v.ValidateAsync(It.IsAny<CreatePaymentRefundRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Amount", "bad") }));

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("payment_refund_validation_failed");
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
        _refunds.Setup(r => r.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync((PaymentDetail?)null);

        var result = await RunAsync();
        result.Failure!.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        result.Failure.ErrorCode.Should().Be("payment_not_found");
    }

    [Fact]
    public async Task ExecuteAsync_PaymentNotRefundable_ReturnsConflict()
    {
        var payment = RefundablePayment();
        payment.PaymentStatus = PaymentStatuses.Processing;
        _refunds.Setup(r => r.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(payment);

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("payment_not_refundable");
    }

    [Fact]
    public async Task ExecuteAsync_StrategyNotAllowed_ReturnsConflict()
    {
        _refunds.Setup(r => r.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(RefundablePayment());
        _strategies.Setup(s => s.Resolve(It.IsAny<PaymentDetail>(), It.IsAny<decimal>()))
            .Returns(new PaymentFundReturnDecision(false, "none", "refund_amount_exceeds", "Amount exceeds"));

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("refund_amount_exceeds");
    }

    [Fact]
    public async Task ExecuteAsync_CurrencyConversionFails_ReturnsValidationFailure()
    {
        _refunds.Setup(r => r.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(RefundablePayment());
        SetupConvert(ok: false);

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("unsupported_currency_or_precision");
    }

    [Fact]
    public async Task ExecuteAsync_RateLimiterUnavailable_ReturnsUnavailable()
    {
        _refunds.Setup(r => r.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(RefundablePayment());
        SetupConvert(ok: true);
        SetupRateLimit(new PaymentRateLimitResult { IsAvailable = false });

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("payment_rate_limiter_unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_RateLimitExceeded_ReturnsRateLimited()
    {
        _refunds.Setup(r => r.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(RefundablePayment());
        SetupConvert(ok: true);
        SetupRateLimit(new PaymentRateLimitResult { IsAvailable = true, IsAllowed = false });

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("payment_refund_rate_limit_exceeded");
    }

    [Fact]
    public async Task ExecuteAsync_ProviderUnavailable_ReturnsUnavailable()
    {
        _refunds.Setup(r => r.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(RefundablePayment());
        SetupConvert(ok: true);
        SetupRateLimit(Allowed());
        SetupProvider(null);

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("payment_provider_unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_RefundWindowExpired_ReturnsConflict()
    {
        var payment = RefundablePayment();
        payment.PaymentDate = DateTime.UtcNow.AddDays(-40);
        _refunds.Setup(r => r.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        SetupConvert(ok: true);
        SetupRateLimit(Allowed());
        SetupProvider(EnabledProvider(maxRefundDays: 30));

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("payment_refund_window_expired");
    }

    [Fact]
    public async Task ExecuteAsync_AllChecksPass_ReturnsSuccess()
    {
        var payment = RefundablePayment();
        _refunds.Setup(r => r.GetPaymentAsync("tenant", _paymentId, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
        SetupConvert(ok: true, minorUnits: 1000);
        SetupRateLimit(Allowed());
        var provider = EnabledProvider();
        SetupProvider(provider);

        var result = await RunAsync();

        result.IsSuccess.Should().BeTrue();
        result.MinorUnits.Should().Be(1000);
        result.ProviderOperation.Should().Be("refund");
        result.Payment.Should().BeSameAs(payment);
        result.Provider.Should().BeSameAs(provider);
    }

    private delegate void TryConvertCallback(decimal amount, string currency, out long value);
}
