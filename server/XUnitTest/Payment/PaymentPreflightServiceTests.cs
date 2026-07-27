using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentPreflightServiceTests
{
    private readonly Mock<IValidator<MakePaymentRequest>> _validator = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _currencyResolver = new();
    private readonly Mock<IPaymentRateLimiter> _rateLimiter = new();
    private readonly PaymentExecutionContext _context = new("tenant", "actor", null);

    public PaymentPreflightServiceTests()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<MakePaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
    }

    private PaymentPreflightService CreateService() => new(_validator.Object, _currencyResolver.Object, _rateLimiter.Object);

    private static MakePaymentRequest Request() => new()
    {
        Amount = 10,
        CurrencyCode = "eur",
        OrderId = "order-1"
    };

    private static string ValidKey() => Guid.NewGuid().ToString();

    private void SetupConvert(bool ok, long minorUnits = 1000) =>
        _currencyResolver.Setup(c => c.TryConvert(It.IsAny<decimal>(), It.IsAny<string>(), out It.Ref<long>.IsAny))
            .Callback(new TryConvertCallback((decimal _, string _, out long value) => value = minorUnits))
            .Returns(ok);

    private void SetupRateLimit(PaymentRateLimitResult result) =>
        _rateLimiter.Setup(r => r.CheckAsync("tenant", "actor", "order-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task ExecuteAsync_ConflictingSavePreferences_ReturnsValidationFailure()
    {
        var request = Request();
        request.SavePaymentMethod = true;
        request.RememberCard = false;

        var result = await CreateService().ExecuteAsync(request, ValidKey(), _context, "corr", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Failure!.ErrorCode.Should().Be("conflicting_save_payment_preferences");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidRequest_ReturnsValidationFailedWithFields()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<MakePaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Amount", "must be positive") }));

        var result = await CreateService().ExecuteAsync(Request(), ValidKey(), _context, "corr", CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("payment_validation_failed");
        result.Failure.ValidationErrors.Should().ContainKey("Amount");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidIdempotencyKey_ReturnsValidationFailure()
    {
        var result = await CreateService().ExecuteAsync(Request(), "not-a-guid", _context, "corr", CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("invalid_idempotency_key");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedCurrency_ReturnsValidationFailure()
    {
        SetupConvert(ok: false);

        var result = await CreateService().ExecuteAsync(Request(), ValidKey(), _context, "corr", CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("unsupported_currency_or_precision");
    }

    [Fact]
    public async Task ExecuteAsync_RateLimiterUnavailable_ReturnsUnavailable()
    {
        SetupConvert(ok: true);
        SetupRateLimit(new PaymentRateLimitResult { IsAvailable = false, RetryAfterSeconds = 5 });

        var result = await CreateService().ExecuteAsync(Request(), ValidKey(), _context, "corr", CancellationToken.None);

        result.Failure!.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.Failure.ErrorCode.Should().Be("payment_rate_limiter_unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_RateLimitExceeded_ReturnsRateLimited()
    {
        SetupConvert(ok: true);
        SetupRateLimit(new PaymentRateLimitResult { IsAvailable = true, IsAllowed = false, Limit = 10, Remaining = 0, RetryAfterSeconds = 30, ResetAfterSeconds = 60 });

        var result = await CreateService().ExecuteAsync(Request(), ValidKey(), _context, "corr", CancellationToken.None);

        result.Failure!.FailureKind.Should().Be(PaymentFailureKind.RateLimited);
        result.Failure.ErrorCode.Should().Be("payment_rate_limit_exceeded");
        result.Failure.RateLimit.Should().Be(10);
    }

    [Fact]
    public async Task ExecuteAsync_AllChecksPass_ReturnsSuccessWithMinorUnits()
    {
        SetupConvert(ok: true, minorUnits: 1000);
        var rateLimit = new PaymentRateLimitResult { IsAvailable = true, IsAllowed = true, Limit = 10, Remaining = 9 };
        SetupRateLimit(rateLimit);

        var result = await CreateService().ExecuteAsync(Request(), ValidKey(), _context, "corr", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.MinorUnits.Should().Be(1000);
        result.RateLimit.Should().BeSameAs(rateLimit);
    }

    private delegate void TryConvertCallback(decimal amount, string currency, out long value);
}
