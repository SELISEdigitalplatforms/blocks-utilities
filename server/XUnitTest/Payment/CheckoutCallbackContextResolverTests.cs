using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class CheckoutCallbackContextResolverTests
{
    private const string TenantId = "tenant-1";
    private const string PaymentId = "payment-1";
    private const string ProviderName = "ADYEN-ONLINE";
    private const string Nonce = "nonce-value";
    private const string SessionId = "session-1";

    private delegate void TryReadCallback(string token, out CheckoutCallbackState state);
    private delegate void TryUnprotectCallback(
        string token, string activeKey, string? previousKey, out CheckoutCallbackState state);

    private static CheckoutCallbackState State() =>
        new(TenantId, PaymentId, ProviderName,
            DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10), Nonce);

    [Fact]
    public async Task Unreadable_state_is_invalid()
    {
        var fixture = new Fixture();
        fixture.StateProtector
            .Setup(p => p.TryRead(It.IsAny<string>(), out It.Ref<CheckoutCallbackState>.IsAny))
            .Returns(false);

        var result = await fixture.Resolver.ResolveAsync(
            "protected", SessionId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Failure!.ErrorCode.Should().Be("invalid_return_state");
    }

    [Fact]
    public async Task Missing_provider_is_payment_not_found()
    {
        var fixture = new Fixture();
        fixture.ArrangeReadableState();
        fixture.Providers
            .Setup(cache => cache.GetAsync(
                TenantId, It.IsAny<string>(), ProviderName, It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync((PaymentProvider?)null);

        var result = await fixture.Resolver.ResolveAsync(
            "protected", SessionId, CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("payment_not_found");
    }

    [Fact]
    public async Task Unverifiable_state_is_invalid()
    {
        var fixture = new Fixture();
        fixture.ArrangeReadableState();
        fixture.ArrangeProvider();
        fixture.StateProtector
            .Setup(p => p.TryUnprotect(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                out It.Ref<CheckoutCallbackState>.IsAny))
            .Returns(false);

        var result = await fixture.Resolver.ResolveAsync(
            "protected", SessionId, CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("invalid_return_state");
    }

    [Fact]
    public async Task Unknown_payment_is_payment_not_found()
    {
        var fixture = new Fixture();
        fixture.ArrangeReadableState();
        fixture.ArrangeProvider();
        fixture.ArrangeVerifiedState();
        fixture.Repository
            .Setup(r => r.GetByIdAsync(TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        var result = await fixture.Resolver.ResolveAsync(
            "protected", SessionId, CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("payment_not_found");
    }

    [Fact]
    public async Task Payment_with_invalid_nonce_is_invalid_state()
    {
        var fixture = new Fixture();
        fixture.ArrangeReadableState();
        fixture.ArrangeProvider();
        fixture.ArrangeVerifiedState();
        fixture.ArrangePayment(nonceHash: "different-hash");

        var result = await fixture.Resolver.ResolveAsync(
            "protected", SessionId, CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("invalid_return_state");
    }

    [Fact]
    public async Task Payment_with_mismatched_session_is_rejected()
    {
        var fixture = new Fixture();
        fixture.ArrangeReadableState();
        fixture.ArrangeProvider();
        fixture.ArrangeVerifiedState();
        fixture.ArrangePayment(sessionId: "another-session");

        var result = await fixture.Resolver.ResolveAsync(
            "protected", SessionId, CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("session_mismatch");
    }

    [Fact]
    public async Task Payment_with_unsafe_result_url_is_payment_not_found()
    {
        var fixture = new Fixture();
        fixture.ArrangeReadableState();
        fixture.ArrangeProvider();
        fixture.ArrangeVerifiedState();
        fixture.ArrangePayment(frontendResultUrl: "http://127.0.0.1/insecure");

        var result = await fixture.Resolver.ResolveAsync(
            "protected", SessionId, CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("payment_not_found");
    }

    [Fact]
    public async Task Valid_callback_resolves_to_success_context()
    {
        var fixture = new Fixture();
        fixture.ArrangeReadableState();
        fixture.ArrangeProvider();
        fixture.ArrangeVerifiedState();
        fixture.ArrangePayment();

        var result = await fixture.Resolver.ResolveAsync(
            "protected", SessionId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Context!.State.PaymentDetailId.Should().Be(PaymentId);
    }

    private sealed class Fixture
    {
        public Mock<ICheckoutCallbackStateProtector> StateProtector { get; } = new();
        public Mock<IPaymentRepository> Repository { get; } = new();
        public Mock<IPaymentProviderCache> Providers { get; } = new();
        public CheckoutCallbackContextResolver Resolver { get; }

        public Fixture()
        {
            Resolver = new CheckoutCallbackContextResolver(
                StateProtector.Object,
                Repository.Object,
                Providers.Object,
                new CheckoutUrlPolicy());
        }

        public void ArrangeReadableState()
        {
            var state = State();
            StateProtector
                .Setup(p => p.TryRead(It.IsAny<string>(), out It.Ref<CheckoutCallbackState>.IsAny))
                .Callback(new TryReadCallback((string _, out CheckoutCallbackState s) => s = state))
                .Returns(true);
        }

        public void ArrangeProvider()
        {
            var provider = new PaymentProvider
            {
                TenantId = TenantId,
                ProviderName = ProviderName,
                IsEnabled = true,
                ReturnStateHmacKey = "return-key"
            };
            Providers
                .Setup(cache => cache.GetAsync(
                    TenantId, It.IsAny<string>(), ProviderName, It.IsAny<Func<Task<PaymentProvider?>>>()))
                .ReturnsAsync(provider);
        }

        public void ArrangeVerifiedState()
        {
            var state = State();
            StateProtector
                .Setup(p => p.TryUnprotect(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    out It.Ref<CheckoutCallbackState>.IsAny))
                .Callback(new TryUnprotectCallback(
                    (string _, string _, string? _, out CheckoutCallbackState s) => s = state))
                .Returns(true);
        }

        public void ArrangePayment(
            string? nonceHash = null,
            string sessionId = SessionId,
            string frontendResultUrl = "https://merchant.example.com/result")
        {
            var payment = new PaymentDetail
            {
                ItemId = PaymentId,
                TenantId = TenantId,
                ProviderName = ProviderName,
                SessionId = sessionId,
                FrontendResultUrlSnapshot = frontendResultUrl,
                ReturnStateNonceHash =
                    nonceHash ?? PaymentHashing.HashSensitiveValue(Nonce)
            };
            Repository
                .Setup(r => r.GetByIdAsync(TenantId, PaymentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(payment);
        }
    }
}
