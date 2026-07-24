using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class StoredPaymentMethodQueryServiceTests
{
    private readonly Mock<IPaymentExecutionContextResolver> _contexts = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentProviderCache> _providers = new();
    private readonly Mock<IShopperReferenceService> _shopperReferences = new();
    private readonly Mock<IStoredPaymentMethodRepository> _methods = new();
    private readonly Mock<IStoredPaymentMethodRateLimiter> _rateLimiter = new();
    private readonly PaymentExecutionContext _context = new("tenant", "actor", null);

    public StoredPaymentMethodQueryServiceTests()
    {
        _contexts.Setup(c => c.Resolve(It.IsAny<string>())).Returns(new PaymentContextResolution(_context, null));
        _rateLimiter.Setup(r => r.CheckListAsync("tenant", "actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRateLimitResult { IsAvailable = true, IsAllowed = true });
        _providers.Setup(p => p.GetAsync("tenant", "ADYEN-ONLINE", It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(new PaymentProvider { ProviderName = "ADYEN-ONLINE", IsEnabled = true });
        _shopperReferences.Setup(s => s.TryCreate("tenant", "actor", It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Callback(new ShopperCallback((string _, string _, string _, out string reference) => reference = "shopper-ref"))
            .Returns(true);
    }

    private StoredPaymentMethodQueryService CreateService() => new(
        _contexts.Object, _payments.Object, _providers.Object, _shopperReferences.Object, _methods.Object, _rateLimiter.Object);

    private Task<StoredPaymentMethodQueryResult> RunAsync() =>
        CreateService().GetStoredPaymentMethodsAsync("corr", CancellationToken.None);

    [Fact]
    public async Task GetStoredPaymentMethodsAsync_ContextFails_ReturnsFailure()
    {
        _contexts.Setup(c => c.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(null, PaymentOperationResult.Failure(PaymentFailureKind.Validation, "unauthorized", "no", "corr")));

        var result = await RunAsync();

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("unauthorized");
    }

    [Fact]
    public async Task GetStoredPaymentMethodsAsync_RateLimiterUnavailable_ReturnsUnavailable()
    {
        _rateLimiter.Setup(r => r.CheckListAsync("tenant", "actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRateLimitResult { IsAvailable = false });

        var result = await RunAsync();

        result.ErrorCode.Should().Be("payment_method_rate_limiter_unavailable");
    }

    [Fact]
    public async Task GetStoredPaymentMethodsAsync_RateLimitExceeded_ReturnsRateLimited()
    {
        _rateLimiter.Setup(r => r.CheckListAsync("tenant", "actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRateLimitResult { IsAvailable = true, IsAllowed = false });

        var result = await RunAsync();

        result.FailureKind.Should().Be(PaymentFailureKind.RateLimited);
        result.ErrorCode.Should().Be("payment_method_rate_limit_exceeded");
    }

    [Fact]
    public async Task GetStoredPaymentMethodsAsync_ProviderUnavailable_ReturnsUnavailable()
    {
        _providers.Setup(p => p.GetAsync("tenant", "ADYEN-ONLINE", It.IsAny<Func<Task<PaymentProvider?>>>())).ReturnsAsync((PaymentProvider?)null);

        var result = await RunAsync();

        result.ErrorCode.Should().Be("payment_provider_unavailable");
    }

    [Fact]
    public async Task GetStoredPaymentMethodsAsync_ShopperReferenceFails_ReturnsUnavailable()
    {
        _shopperReferences.Setup(s => s.TryCreate("tenant", "actor", It.IsAny<string>(), out It.Ref<string>.IsAny)).Returns(false);

        var result = await RunAsync();

        result.ErrorCode.Should().Be("payment_provider_unavailable");
    }

    [Fact]
    public async Task GetStoredPaymentMethodsAsync_Success_MapsMethods()
    {
        _methods.Setup(m => m.ListActiveAsync("tenant", "shopper-ref", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoredPaymentMethod>
            {
                new() { ItemId = "m1", Type = "scheme", Brand = "visa", LastFour = "4242" },
                new() { ItemId = "m2", Type = "scheme", Brand = "mc", LastFour = "1111" }
            });

        var result = await RunAsync();

        result.IsSuccess.Should().BeTrue();
        result.Methods.Should().HaveCount(2);
        result.Methods![0].PaymentMethodId.Should().Be("m1");
        result.Methods[0].Status.Should().Be("ACTIVE");
    }

    private delegate void ShopperCallback(string tenantId, string actorId, string key, out string reference);
}
