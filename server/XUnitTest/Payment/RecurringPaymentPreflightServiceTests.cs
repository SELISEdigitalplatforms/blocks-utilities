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

public sealed class RecurringPaymentPreflightServiceTests
{
    private readonly Mock<IValidator<CreateRecurringPaymentRequest>> _validator = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _minorUnits = new();
    private readonly Mock<IPaymentRateLimiter> _rateLimiter = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentProviderCache> _providers = new();
    private readonly Mock<IStoredPaymentMethodRepository> _storedMethods = new();
    private readonly Mock<IShopperReferenceService> _shopperReferences = new();
    private readonly PaymentExecutionContext _context = new("tenant", "actor", null);

    public RecurringPaymentPreflightServiceTests()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<CreateRecurringPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _shopperReferences.Setup(s => s.TryCreate("tenant", "actor", It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Callback(new TryCreateCallback((string _, string _, string _, out string reference) => reference = "shopper-ref"))
            .Returns(true);
    }

    private RecurringPaymentPreflightService CreateService() => new(
        _validator.Object, _minorUnits.Object, _rateLimiter.Object,
        _payments.Object, _providers.Object, _storedMethods.Object, _shopperReferences.Object);

    private static CreateRecurringPaymentRequest Request() => new()
    {
        ProviderName = "provider",
        StoredPaymentMethodId = "method-1",
        Amount = 10,
        CurrencyCode = "eur",
        OrderId = "order-1"
    };

    private static PaymentProvider EnabledProvider() => new()
    {
        ProviderName = "provider",
        IsEnabled = true,
        ApiKey = "key",
        MerchantId = "merchant"
    };

    private static StoredPaymentMethod ActiveMethod() => new()
    {
        ItemId = "method-1",
        ShopperReference = "shopper-ref",
        ProviderName = "provider",
        Status = PaymentMethodStatus.Active
    };

    private void SetupConvert(bool ok, long minorUnits = 1000) =>
        _minorUnits.Setup(c => c.TryConvert(It.IsAny<decimal>(), It.IsAny<string>(), out It.Ref<long>.IsAny))
            .Callback(new TryConvertCallback((decimal _, string _, out long value) => value = minorUnits))
            .Returns(ok);

    private void SetupRateLimit(PaymentRateLimitResult result) =>
        _rateLimiter.Setup(r => r.CheckAsync("tenant", "actor", "order-1", It.IsAny<CancellationToken>())).ReturnsAsync(result);

    private static PaymentRateLimitResult Allowed() => new() { IsAvailable = true, IsAllowed = true };

    private void SetupProvider(PaymentProvider? provider) =>
        _providers.Setup(p => p.GetAsync("tenant", It.IsAny<string>(), "provider", It.IsAny<Func<Task<PaymentProvider?>>>())).ReturnsAsync(provider);

    private void SetupStoredMethod(StoredPaymentMethod? method) =>
        _storedMethods.Setup(s => s.GetAsync("tenant", "method-1", It.IsAny<CancellationToken>())).ReturnsAsync(method);

    private Task<RecurringPaymentPreflightResult> RunAsync(string? key = null) =>
        CreateService().ExecuteAsync(Request(), key ?? Guid.NewGuid().ToString(), _context, "corr", CancellationToken.None);

    [Fact]
    public async Task ExecuteAsync_InvalidRequest_ReturnsValidationFailure()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<CreateRecurringPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[] { new ValidationFailure("Amount", "bad") }));

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("recurring_payment_validation_failed");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidIdempotencyKey_ReturnsFailure()
    {
        var result = await RunAsync(key: "not-a-guid");
        result.Failure!.ErrorCode.Should().Be("invalid_idempotency_key");
    }

    [Fact]
    public async Task ExecuteAsync_CurrencyConversionFails_ReturnsFailure()
    {
        SetupConvert(false);
        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("unsupported_currency_or_precision");
    }

    [Fact]
    public async Task ExecuteAsync_RateLimiterUnavailable_ReturnsUnavailable()
    {
        SetupConvert(true);
        SetupRateLimit(new PaymentRateLimitResult { IsAvailable = false });
        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("payment_rate_limiter_unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_RateLimitExceeded_ReturnsRateLimited()
    {
        SetupConvert(true);
        SetupRateLimit(new PaymentRateLimitResult { IsAvailable = true, IsAllowed = false });
        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("payment_rate_limit_exceeded");
    }

    [Fact]
    public async Task ExecuteAsync_ProviderNotFound_ReturnsNotFound()
    {
        SetupConvert(true);
        SetupRateLimit(Allowed());
        SetupProvider(null);
        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("payment_provider_not_found");
    }

    [Fact]
    public async Task ExecuteAsync_ShopperReferenceCreationFails_ReturnsMisconfigured()
    {
        SetupConvert(true);
        SetupRateLimit(Allowed());
        SetupProvider(EnabledProvider());
        _shopperReferences.Setup(s => s.TryCreate("tenant", "actor", It.IsAny<string>(), out It.Ref<string>.IsAny)).Returns(false);

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("payment_provider_misconfigured");
    }

    [Fact]
    public async Task ExecuteAsync_StoredMethodNotFound_ReturnsNotFound()
    {
        SetupConvert(true);
        SetupRateLimit(Allowed());
        SetupProvider(EnabledProvider());
        SetupStoredMethod(null);

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("stored_payment_method_not_found");
    }

    [Fact]
    public async Task ExecuteAsync_StoredMethodShopperMismatch_ReturnsNotFound()
    {
        SetupConvert(true);
        SetupRateLimit(Allowed());
        SetupProvider(EnabledProvider());
        var method = ActiveMethod();
        method.ShopperReference = "different";
        SetupStoredMethod(method);

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("stored_payment_method_not_found");
    }

    [Fact]
    public async Task ExecuteAsync_StoredMethodInactive_ReturnsConflict()
    {
        SetupConvert(true);
        SetupRateLimit(Allowed());
        SetupProvider(EnabledProvider());
        var method = ActiveMethod();
        method.Status = PaymentMethodStatus.Removed;
        SetupStoredMethod(method);

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("stored_payment_method_unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_StoredMethodProviderMismatch_ReturnsConflict()
    {
        SetupConvert(true);
        SetupRateLimit(Allowed());
        SetupProvider(EnabledProvider());
        var method = ActiveMethod();
        method.ProviderName = "other-provider";
        SetupStoredMethod(method);

        var result = await RunAsync();
        result.Failure!.ErrorCode.Should().Be("stored_payment_method_provider_mismatch");
    }

    [Fact]
    public async Task ExecuteAsync_AllChecksPass_ReturnsSuccess()
    {
        SetupConvert(true, 1000);
        SetupRateLimit(Allowed());
        var provider = EnabledProvider();
        SetupProvider(provider);
        var method = ActiveMethod();
        SetupStoredMethod(method);

        var result = await RunAsync();

        result.IsSuccess.Should().BeTrue();
        result.MinorUnits.Should().Be(1000);
        result.Provider.Should().BeSameAs(provider);
        result.StoredPaymentMethod.Should().BeSameAs(method);
        result.ShopperReference.Should().Be("shopper-ref");
    }

    private delegate void TryCreateCallback(string tenantId, string actorId, string key, out string reference);
    private delegate void TryConvertCallback(decimal amount, string currency, out long value);
}
