using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class StoredPaymentMethodRemovalServiceTests
{
    [Theory]
    [InlineData(PaymentMethodStatus.RemovalPending)]
    [InlineData(PaymentMethodStatus.RemovalOutcomeUnknown)]
    public async Task Repeated_removal_returns_pending_without_provider_call(
        PaymentMethodStatus status)
    {
        var fixture = new Fixture();
        fixture.ArrangeMethod(status);

        var result =
            await fixture.Service.RemoveStoredPaymentMethodAsync(
                "method-1",
                "correlation-1",
                CancellationToken.None);

        result.IsPending.Should().BeTrue();
        fixture.Gateway.Verify(
            gateway =>
                gateway.RemoveAsync(
                    It.IsAny<PaymentProvider>(),
                    It.IsAny<StoredPaymentMethod>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Methods.Verify(
            repository =>
                repository.TryClaimRemovalAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Method_owned_by_another_shopper_is_not_found()
    {
        var fixture = new Fixture();
        fixture.ArrangeMethod(
            PaymentMethodStatus.Active,
            shopperReference:
            "different-shopper-reference");

        var result =
            await fixture.Service.RemoveStoredPaymentMethodAsync(
                "method-1",
                "correlation-1",
                CancellationToken.None);

        result.FailureKind.Should()
            .Be(PaymentFailureKind.NotFound);
        result.ErrorCode.Should()
            .Be("payment_method_not_found");
    }

    [Fact]
    public async Task Confirmed_provider_removal_is_marked_removed()
    {
        var fixture = new Fixture();
        var method = fixture.ArrangeMethod(
            PaymentMethodStatus.Active);
        fixture.Methods
            .Setup(repository =>
                repository.TryClaimRemovalAsync(
                    Fixture.TenantId,
                    "method-1",
                    method.ShopperReference,
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(method);
        fixture.Gateway
            .Setup(gateway =>
                gateway.RemoveAsync(
                    It.IsAny<PaymentProvider>(),
                    method,
                    "provider-token",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                StoredPaymentMethodRemovalOutcome.Removed);
        fixture.Methods
            .Setup(repository =>
                repository.MarkRemovedAsync(
                    Fixture.TenantId,
                    "method-1",
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result =
            await fixture.Service.RemoveStoredPaymentMethodAsync(
                "method-1",
                "correlation-1",
                CancellationToken.None);

        result.IsRemoved.Should().BeTrue();
        fixture.Methods.Verify(
            repository =>
                repository.MarkRemovedAsync(
                    Fixture.TenantId,
                    "method-1",
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class Fixture
    {
        public const string TenantId =
            "de9fc4f4baa4c4cbc829b6059b372dc61";

        private const string ShopperKey =
            "0123456789abcdef0123456789abcdef";

        public Mock<IStoredPaymentMethodRepository> Methods
        {
            get;
        } = new();

        public Mock<IStoredPaymentMethodProviderGateway> Gateway
        {
            get;
        } = new();

        public StoredPaymentMethodRemovalService Service
        {
            get;
        }

        private readonly ShopperReferenceService
            _shopperReferences = new();

        public Fixture()
        {
            var contexts =
                new Mock<IPaymentExecutionContextResolver>();
            contexts.Setup(resolver =>
                    resolver.Resolve(
                        It.IsAny<string>()))
                .Returns(
                    new PaymentContextResolution(
                        new PaymentExecutionContext(
                            TenantId,
                            "actor-1",
                            null),
                        null));

            var provider = new PaymentProvider
            {
                ProviderName =
                    PaymentConstants.AdyenOnlineProvider,
                IsEnabled = true,
                ShopperReferenceHmacKey = ShopperKey
            };
            var providers =
                new Mock<IPaymentProviderCache>();
            providers.Setup(cache =>
                cache.GetAsync(
                        TenantId,
                        PaymentConstants
                            .AdyenOnlineProvider,
                        It.IsAny<
                            Func<Task<PaymentProvider?>>>()))
                .ReturnsAsync(provider);

            var rateLimiter =
                new Mock<
                    IStoredPaymentMethodRateLimiter>();
            rateLimiter.Setup(limiter =>
                limiter.CheckRemovalAsync(
                        TenantId,
                        "actor-1",
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    new PaymentRateLimitResult
                    {
                        IsAllowed = true,
                        Limit = 10,
                        Remaining = 9
                    });

            var gatewayResolver =
                new Mock<
                    IStoredPaymentMethodProviderGatewayResolver>();
            gatewayResolver.Setup(resolver =>
                    resolver.Resolve(
                        PaymentConstants
                            .AdyenOnlineProvider))
                .Returns(Gateway.Object);

            var tokenProtector =
                new Mock<IProviderTokenProtector>();
            tokenProtector.Setup(protector =>
                    protector.TryUnprotect(
                        It.IsAny<StoredPaymentMethod>(),
                        out It.Ref<string>.IsAny))
                .Callback(
                    new TryUnprotectCallback(
                        (
                            StoredPaymentMethod _,
                            out string token) =>
                        {
                            token = "provider-token";
                        }))
                .Returns(true);

            var options =
                new Mock<IOptionsMonitor<PaymentOptions>>();
            options.SetupGet(value => value.CurrentValue)
                .Returns(new PaymentOptions());
            var distributedLock =
                new Mock<IPaymentDistributedLock>();
            distributedLock.Setup(value =>
                    value.TryAcquireAsync(
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    (IPaymentLockHandle?)null);

            Service =
                new StoredPaymentMethodRemovalService(
                    contexts.Object,
                    Mock.Of<IPaymentRepository>(),
                    providers.Object,
                    _shopperReferences,
                    Methods.Object,
                    rateLimiter.Object,
                    distributedLock.Object,
                    gatewayResolver.Object,
                    tokenProtector.Object,
                    options.Object,
                    Mock.Of<
                        ILogger<
                            StoredPaymentMethodRemovalService>>());
        }

        public StoredPaymentMethod ArrangeMethod(
            PaymentMethodStatus status,
            string? shopperReference = null)
        {
            if (shopperReference == null)
            {
                _shopperReferences.TryCreate(
                    TenantId,
                    "actor-1",
                    ShopperKey,
                    out shopperReference);
            }

            var method = new StoredPaymentMethod
            {
                ItemId = "method-1",
                TenantId = Fixture.TenantId,
                ProviderName =
                    PaymentConstants.AdyenOnlineProvider,
                ShopperReference = shopperReference!,
                Status = status,
                ProviderTokenCiphertext =
                    "ciphertext"
            };

            Methods.Setup(repository =>
                repository.GetAsync(
                        TenantId,
                        "method-1",
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(method);

            return method;
        }

        private delegate void TryUnprotectCallback(
            StoredPaymentMethod method,
            out string providerToken);
    }
}
