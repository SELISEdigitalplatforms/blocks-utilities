using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
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
        fixture.ArrangeClaim(method);
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

    [Fact]
    public async Task Context_resolution_failure_is_returned_directly()
    {
        var fixture = new Fixture();
        fixture.Contexts
            .Setup(resolver => resolver.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                null,
                PaymentOperationResult.Failure(
                    PaymentFailureKind.Unavailable,
                    "payment_context_missing",
                    "no context",
                    "correlation-1")));

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_context_missing");
    }

    [Fact]
    public async Task Rate_limiter_unavailable_fails_closed()
    {
        var fixture = new Fixture();
        fixture.RateLimiter
            .Setup(limiter => limiter.CheckRemovalAsync(
                Fixture.TenantId, "actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRateLimitResult
            {
                IsAvailable = false,
                IsAllowed = false
            });

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_method_rate_limiter_unavailable");
    }

    [Fact]
    public async Task Rate_limit_exceeded_returns_rate_limited()
    {
        var fixture = new Fixture();
        fixture.RateLimiter
            .Setup(limiter => limiter.CheckRemovalAsync(
                Fixture.TenantId, "actor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRateLimitResult
            {
                IsAvailable = true,
                IsAllowed = false,
                Limit = 5,
                Remaining = 0
            });

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.RateLimited);
        result.ErrorCode.Should().Be("payment_method_rate_limit_exceeded");
    }

    [Fact]
    public async Task Missing_method_returns_not_found()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        result.ErrorCode.Should().Be("payment_method_not_found");
    }

    [Fact]
    public async Task Disabled_provider_is_unavailable()
    {
        var fixture = new Fixture();
        fixture.Provider.IsEnabled = false;
        fixture.ArrangeMethod(PaymentMethodStatus.Active);

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_provider_unavailable");
    }

    [Fact]
    public async Task Already_removed_method_reports_removed()
    {
        var fixture = new Fixture();
        fixture.ArrangeMethod(PaymentMethodStatus.Removed);

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.IsRemoved.Should().BeTrue();
    }

    [Fact]
    public async Task Requires_attention_status_is_unavailable()
    {
        var fixture = new Fixture();
        fixture.ArrangeMethod(PaymentMethodStatus.RemovalRequiresAttention);

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_method_removal_requires_attention");
    }

    [Fact]
    public async Task Method_with_payment_in_progress_is_conflict()
    {
        var fixture = new Fixture();
        var method = fixture.ArrangeMethod(PaymentMethodStatus.Active);
        fixture.Payments
            .Setup(payments => payments.HasUnresolvedRecurringPaymentAsync(
                Fixture.TenantId, method.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("payment_method_in_use");
    }

    [Fact]
    public async Task Lost_claim_rechecks_state_and_returns_pending()
    {
        var fixture = new Fixture();
        var method = fixture.ArrangeMethod(PaymentMethodStatus.Active);
        // Claim returns null (another worker won). Re-read shows pending state.
        var pendingMethod = new StoredPaymentMethod
        {
            ItemId = method.ItemId,
            TenantId = method.TenantId,
            ProviderName = method.ProviderName,
            ShopperReference = method.ShopperReference,
            Status = PaymentMethodStatus.RemovalPending
        };
        var sequence = new Queue<StoredPaymentMethod?>(
            new StoredPaymentMethod?[] { method, pendingMethod });
        fixture.Methods
            .Setup(repository => repository.GetAsync(
                Fixture.TenantId, "method-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => sequence.Dequeue());

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.IsPending.Should().BeTrue();
    }

    [Fact]
    public async Task Token_that_cannot_be_unprotected_requires_attention()
    {
        var fixture = new Fixture();
        var method = fixture.ArrangeMethod(PaymentMethodStatus.Active);
        fixture.ArrangeClaim(method);
        fixture.TokenProtector
            .Setup(protector => protector.TryUnprotect(
                It.IsAny<StoredPaymentMethod>(), out It.Ref<string>.IsAny))
            .Returns(false);

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_method_token_unavailable");
        fixture.Methods.Verify(repository =>
            repository.MarkRemovalRequiresAttentionAsync(
                Fixture.TenantId, method.ItemId, It.IsAny<string>(),
                "provider_token_unavailable", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Missing_gateway_requires_attention()
    {
        var fixture = new Fixture();
        var method = fixture.ArrangeMethod(PaymentMethodStatus.Active);
        fixture.ArrangeClaim(method);
        fixture.GatewayResolver
            .Setup(resolver => resolver.Resolve(It.IsAny<string>()))
            .Returns((IStoredPaymentMethodProviderGateway?)null);

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_provider_unavailable");
        fixture.Methods.Verify(repository =>
            repository.MarkRemovalRequiresAttentionAsync(
                Fixture.TenantId, method.ItemId, It.IsAny<string>(),
                "provider_gateway_unavailable", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Provider_operational_failure_requires_attention()
    {
        var fixture = new Fixture();
        var method = fixture.ArrangeMethod(PaymentMethodStatus.Active);
        fixture.ArrangeClaim(method);
        fixture.Gateway
            .Setup(gateway => gateway.RemoveAsync(
                It.IsAny<PaymentProvider>(), method, "provider-token",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPaymentMethodRemovalOutcome.OperationalFailure);

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_method_removal_unavailable");
        fixture.Methods.Verify(repository =>
            repository.MarkRemovalRequiresAttentionAsync(
                Fixture.TenantId, method.ItemId, It.IsAny<string>(),
                "provider_operational_failure", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Unknown_provider_outcome_marks_pending_and_dispatches_recovery()
    {
        var fixture = new Fixture();
        var method = fixture.ArrangeMethod(PaymentMethodStatus.Active);
        fixture.ArrangeClaim(method);
        fixture.Gateway
            .Setup(gateway => gateway.RemoveAsync(
                It.IsAny<PaymentProvider>(), method, "provider-token",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPaymentMethodRemovalOutcome.OutcomeUnknown);

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.IsPending.Should().BeTrue();
        fixture.Methods.Verify(repository =>
            repository.MarkRemovalOutcomeUnknownAsync(
                Fixture.TenantId, method.ItemId, It.IsAny<string>(),
                It.IsAny<DateTime>(), "provider_outcome_unknown",
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.WorkDispatcher.Verify(dispatcher =>
            dispatcher.TryDispatchAsync(
                Fixture.TenantId, true, It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Confirmed_removal_that_does_not_persist_is_pending()
    {
        var fixture = new Fixture();
        var method = fixture.ArrangeMethod(PaymentMethodStatus.Active);
        fixture.ArrangeClaim(method);
        fixture.Gateway
            .Setup(gateway => gateway.RemoveAsync(
                It.IsAny<PaymentProvider>(), method, "provider-token",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPaymentMethodRemovalOutcome.Removed);
        fixture.Methods
            .Setup(repository => repository.MarkRemovedAsync(
                Fixture.TenantId, "method-1", It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.IsPending.Should().BeTrue();
    }

    [Fact]
    public async Task Legacy_token_without_ciphertext_is_migrated_before_removal()
    {
        var fixture = new Fixture();
        var method = fixture.ArrangeMethod(
            PaymentMethodStatus.Active, providerTokenCiphertext: null);
        fixture.ArrangeClaim(method);
        var reprotected = new ProtectedProviderToken(
            "reprotected", "fingerprint", "key-1");
        fixture.TokenProtector
            .Setup(protector => protector.TryProtect(
                "provider-token", out It.Ref<ProtectedProviderToken>.IsAny))
            .Callback(new TryProtectCallback(
                (string _, out ProtectedProviderToken protectedToken) =>
                    protectedToken = reprotected))
            .Returns(true);
        fixture.Gateway
            .Setup(gateway => gateway.RemoveAsync(
                It.IsAny<PaymentProvider>(), method, "provider-token",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StoredPaymentMethodRemovalOutcome.Removed);
        fixture.Methods
            .Setup(repository => repository.MarkRemovedAsync(
                Fixture.TenantId, "method-1", It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.Service.RemoveStoredPaymentMethodAsync(
            "method-1", "correlation-1", CancellationToken.None);

        result.IsRemoved.Should().BeTrue();
        fixture.Methods.Verify(repository =>
            repository.MigrateLegacyTokenAsync(
                method.TenantId, method.ItemId, reprotected,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private delegate void TryProtectCallback(
        string providerToken, out ProtectedProviderToken protectedToken);

    private sealed class Fixture
    {
        public const string TenantId =
            "de9fc4f4baa4c4cbc829b6059b372dc61";

        private const string ShopperKey =
            "0123456789abcdef0123456789abcdef";

        public Mock<IStoredPaymentMethodRepository> Methods { get; } = new();
        public Mock<IStoredPaymentMethodProviderGateway> Gateway { get; } = new();
        public Mock<IPaymentExecutionContextResolver> Contexts { get; } = new();
        public Mock<IPaymentRepository> Payments { get; } = new();
        public Mock<IStoredPaymentMethodRateLimiter> RateLimiter { get; } = new();
        public Mock<IProviderTokenProtector> TokenProtector { get; } = new();
        public Mock<IStoredPaymentMethodProviderGatewayResolver> GatewayResolver
        { get; } = new();
        public Mock<IPaymentWorkDispatcher> WorkDispatcher { get; } = new();
        public PaymentProvider Provider { get; }

        public StoredPaymentMethodRemovalService Service { get; }

        private readonly ShopperReferenceService _shopperReferences = new();

        public Fixture()
        {
            Contexts.Setup(resolver => resolver.Resolve(It.IsAny<string>()))
                .Returns(new PaymentContextResolution(
                    new PaymentExecutionContext(TenantId, "actor-1", null),
                    null));

            Provider = new PaymentProvider
            {
                ProviderName = PaymentConstants.AdyenOnlineProvider,
                IsEnabled = true,
                ShopperReferenceHmacKey = ShopperKey
            };
            var providers = new Mock<IPaymentProviderCache>();
            providers.Setup(cache => cache.GetAsync(
                    TenantId,
                    PaymentConstants.AdyenOnlineProvider,
                    It.IsAny<Func<Task<PaymentProvider?>>>()))
                .ReturnsAsync(() => Provider);

            RateLimiter.Setup(limiter => limiter.CheckRemovalAsync(
                    TenantId, "actor-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PaymentRateLimitResult
                {
                    IsAllowed = true,
                    Limit = 10,
                    Remaining = 9
                });

            GatewayResolver.Setup(resolver => resolver.Resolve(
                    PaymentConstants.AdyenOnlineProvider))
                .Returns(Gateway.Object);

            TokenProtector.Setup(protector => protector.TryUnprotect(
                    It.IsAny<StoredPaymentMethod>(), out It.Ref<string>.IsAny))
                .Callback(new TryUnprotectCallback(
                    (StoredPaymentMethod _, out string token) =>
                        token = "provider-token"))
                .Returns(true);

            var options = new Mock<IOptionsMonitor<PaymentOptions>>();
            options.SetupGet(value => value.CurrentValue)
                .Returns(new PaymentOptions());
            var distributedLock = new Mock<IPaymentDistributedLock>();
            distributedLock.Setup(value => value.TryAcquireAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IPaymentLockHandle?)null);

            Service = new StoredPaymentMethodRemovalService(
                Contexts.Object,
                Payments.Object,
                providers.Object,
                _shopperReferences,
                Methods.Object,
                RateLimiter.Object,
                distributedLock.Object,
                GatewayResolver.Object,
                TokenProtector.Object,
                WorkDispatcher.Object,
                options.Object,
                NullLogger<StoredPaymentMethodRemovalService>.Instance);
        }

        public StoredPaymentMethod ArrangeMethod(
            PaymentMethodStatus status,
            string? shopperReference = null,
            string? providerTokenCiphertext = "ciphertext")
        {
            if (shopperReference == null)
            {
                _shopperReferences.TryCreate(
                    TenantId, "actor-1", ShopperKey, out shopperReference);
            }

            var method = new StoredPaymentMethod
            {
                ItemId = "method-1",
                TenantId = TenantId,
                ProviderName = PaymentConstants.AdyenOnlineProvider,
                ShopperReference = shopperReference!,
                Status = status,
                ProviderTokenCiphertext = providerTokenCiphertext
            };

            Methods.Setup(repository => repository.GetAsync(
                    TenantId, "method-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(method);

            return method;
        }

        public void ArrangeClaim(StoredPaymentMethod method) =>
            Methods.Setup(repository => repository.TryClaimRemovalAsync(
                    TenantId, "method-1", method.ShopperReference,
                    It.IsAny<string>(), It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(method);

        private delegate void TryUnprotectCallback(
            StoredPaymentMethod method, out string providerToken);
    }
}
