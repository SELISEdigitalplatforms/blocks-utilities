using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class HostedCheckoutInitiationServiceTests
{
    private readonly Mock<IPaymentRepository> _repository = new();
    private readonly Mock<IPaymentProviderCache> _providerCache = new();
    private readonly Mock<ICheckoutUrlPolicy> _checkoutUrlPolicy = new();
    private readonly Mock<IProviderEndpointPolicy> _endpointPolicy = new();
    private readonly Mock<IProviderEndpointPolicyResolver> _endpointPolicies = new();
    private readonly Mock<IPaymentSessionClient> _sessionClient = new();
    private readonly Mock<IPaymentSessionClientResolver> _sessionClients = new();
    private readonly Mock<IPaymentStateTransitionService> _stateTransitions = new();
    private readonly Mock<ICheckoutCallbackStateProtector> _callbackStateProtector = new();
    private readonly Mock<IShopperReferenceService> _shopperReferenceService = new();
    private readonly Mock<IPaymentWebhookReferenceService> _webhookReferenceService = new();
    private readonly Mock<IStoredPaymentMethodRepository> _storedPaymentMethods = new();
    private readonly Mock<IHostedCheckoutSessionRequestFactory> _sessionRequestFactory = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _options = new();

    private readonly PaymentExecutionContext _context = new("tenant", "actor", null);
    private readonly PaymentDetail _payment = new() { ItemId = "pay-1", TenantId = "tenant", ProviderName = "provider", IdempotencyKey = "idem" };
    private readonly PaymentOperationResult _applied = PaymentOperationResult.Success(new PaymentResponse(), "corr");

    public HostedCheckoutInitiationServiceTests()
    {
        _options.Setup(o => o.CurrentValue).Returns(new PaymentOptions());
        _stateTransitions.Setup(s => s.CompleteFailureAsync(
                It.IsAny<PaymentDetail>(), It.IsAny<string>(), It.IsAny<PaymentFailureKind>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail _, string _, PaymentFailureKind kind, string code, string msg, string corr, CancellationToken _) =>
                PaymentOperationResult.Failure(kind, code, msg, corr));
        _providerCache.Setup(c => c.GetAsync("tenant", "provider", It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(ValidProvider());
        _shopperReferenceService.Setup(s => s.TryCreate("tenant", "actor", It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Callback(new ShopperCallback((string _, string _, string _, out string reference) => reference = "shopper-ref"))
            .Returns(true);
        _webhookReferenceService.Setup(s => s.TryCreate("tenant", "pay-1", out It.Ref<string>.IsAny))
            .Callback(new WebhookCallback((string _, string _, out string reference) => reference = "provider-ref"))
            .Returns(true);
        _storedPaymentMethods.Setup(s => s.HasUnresolvedRemovalAsync("tenant", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _callbackStateProtector.Setup(p => p.Create("tenant", "pay-1", "provider", It.IsAny<TimeSpan>(), It.IsAny<string>()))
            .Returns(new ProtectedCheckoutCallbackState("token", new CheckoutCallbackState("tenant", "pay-1", "provider", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30), "nonce")));
        _sessionClients.Setup(r => r.Resolve("provider")).Returns(_sessionClient.Object);
        _endpointPolicy.Setup(p => p.IsAllowed(It.IsAny<string>())).Returns(true);
        _endpointPolicies.Setup(r => r.Resolve("provider")).Returns(_endpointPolicy.Object);
        _checkoutUrlPolicy.Setup(u => u.TryResolveHostedUrls(It.IsAny<PaymentProvider>(), "token", out It.Ref<string>.IsAny, out It.Ref<string>.IsAny))
            .Callback(new ResolveUrlsCallback((PaymentProvider _, string _, out string returnUrl, out string frontendUrl) =>
            {
                returnUrl = "https://return";
                frontendUrl = "https://frontend";
            }))
            .Returns(true);
        _sessionRequestFactory.Setup(f => f.Create(
                It.IsAny<MakePaymentRequest>(), It.IsAny<PaymentExecutionContext>(), It.IsAny<PaymentDetail>(),
                It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<long>()))
            .Returns(new HostedCheckoutSessionRequest());
        _repository.Setup(r => r.SaveInitiationRequestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HostedCheckoutSessionRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _sessionClient.Setup(c => c.CreateSessionAsync(It.IsAny<PaymentProvider>(), It.IsAny<HostedCheckoutSessionRequest>(), "idem", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderSessionCreationResult { Outcome = ProviderClientOutcome.Success });
        _stateTransitions.Setup(s => s.ApplyProviderResultAsync(
                It.IsAny<PaymentDetail>(), It.IsAny<ProviderSessionCreationResult>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_applied);
    }

    private static PaymentProvider ValidProvider() => new()
    {
        ProviderName = "provider",
        IsEnabled = true,
        ApiKey = "key",
        MerchantId = "merchant",
        ApiBaseUrl = "https://api.provider",
        ReturnStateHmacKey = "return-key",
        ShopperReferenceHmacKey = "shopper-key"
    };

    private HostedCheckoutInitiationService CreateService() => new(
        _repository.Object, _providerCache.Object, _checkoutUrlPolicy.Object, _endpointPolicies.Object, _sessionClients.Object,
        _stateTransitions.Object, _callbackStateProtector.Object, _shopperReferenceService.Object,
        _webhookReferenceService.Object, _storedPaymentMethods.Object, _sessionRequestFactory.Object, _options.Object);

    private Task<PaymentOperationResult> RunAsync(MakePaymentRequest? request = null) =>
        CreateService().InitiateAsync(request ?? new MakePaymentRequest { ProviderName = "provider" }, _context, _payment, "lease", 1000, "corr", CancellationToken.None);

    [Fact]
    public async Task InitiateAsync_ProviderNull_ReturnsNotFound()
    {
        _providerCache.Setup(c => c.GetAsync("tenant", "provider", It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync((PaymentProvider?)null);

        var result = await RunAsync();
        result.ErrorCode.Should().Be("payment_provider_not_found");
    }

    [Fact]
    public async Task InitiateAsync_ProviderMisconfigured_ReturnsMisconfigured()
    {
        var provider = ValidProvider();
        provider.ApiKey = "";
        _providerCache.Setup(c => c.GetAsync("tenant", "provider", It.IsAny<Func<Task<PaymentProvider?>>>())).ReturnsAsync(provider);

        var result = await RunAsync();
        result.ErrorCode.Should().Be("payment_provider_misconfigured");
    }

    [Fact]
    public async Task InitiateAsync_ProviderEndpointNotAllowed_ReturnsMisconfigured()
    {
        _endpointPolicy.Setup(p => p.IsAllowed(It.IsAny<string>())).Returns(false);

        var result = await RunAsync();
        result.ErrorCode.Should().Be("payment_provider_misconfigured");
    }

    [Fact]
    public async Task InitiateAsync_NoSessionClientForProvider_FailsClosed()
    {
        _sessionClients.Setup(r => r.Resolve("provider")).Returns((IPaymentSessionClient?)null);

        var result = await RunAsync();
        result.ErrorCode.Should().Be("payment_provider_misconfigured");
    }

    [Fact]
    public async Task InitiateAsync_NoEndpointPolicyForProvider_FailsClosed()
    {
        _endpointPolicies.Setup(r => r.Resolve("provider")).Returns((IProviderEndpointPolicy?)null);

        var result = await RunAsync();
        result.ErrorCode.Should().Be("payment_provider_misconfigured");
    }

    [Fact]
    public async Task InitiateAsync_ShopperReferenceFails_ReturnsMisconfigured()
    {
        _shopperReferenceService.Setup(s => s.TryCreate("tenant", "actor", It.IsAny<string>(), out It.Ref<string>.IsAny)).Returns(false);

        var result = await RunAsync();
        result.ErrorCode.Should().Be("payment_provider_misconfigured");
    }

    [Fact]
    public async Task InitiateAsync_WebhookReferenceFails_ReturnsRoutingUnavailable()
    {
        _webhookReferenceService.Setup(s => s.TryCreate("tenant", "pay-1", out It.Ref<string>.IsAny)).Returns(false);

        var result = await RunAsync();
        result.ErrorCode.Should().Be("payment_routing_unavailable");
    }

    [Fact]
    public async Task InitiateAsync_UnresolvedRemovalWithSave_ReturnsConflict()
    {
        _storedPaymentMethods.Setup(s => s.HasUnresolvedRemovalAsync("tenant", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await RunAsync(new MakePaymentRequest { ProviderName = "provider", SavePaymentMethod = true });
        result.ErrorCode.Should().Be("payment_method_removal_in_progress");
    }

    [Fact]
    public async Task InitiateAsync_ProtectorThrowsFormat_ReturnsMisconfigured()
    {
        _callbackStateProtector.Setup(p => p.Create("tenant", "pay-1", "provider", It.IsAny<TimeSpan>(), It.IsAny<string>()))
            .Throws(new FormatException());

        var result = await RunAsync();
        result.ErrorCode.Should().Be("payment_provider_misconfigured");
    }

    [Fact]
    public async Task InitiateAsync_UrlResolutionFails_ReturnsMisconfigured()
    {
        _checkoutUrlPolicy.Setup(u => u.TryResolveHostedUrls(It.IsAny<PaymentProvider>(), "token", out It.Ref<string>.IsAny, out It.Ref<string>.IsAny)).Returns(false);

        var result = await RunAsync();
        result.ErrorCode.Should().Be("payment_provider_misconfigured");
    }

    [Fact]
    public async Task InitiateAsync_SaveInitiationFails_ReturnsConflict()
    {
        _repository.Setup(r => r.SaveInitiationRequestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<HostedCheckoutSessionRequest>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await RunAsync();
        result.ErrorCode.Should().Be("payment_state_conflict");
    }

    [Fact]
    public async Task InitiateAsync_HappyPath_AppliesProviderResult()
    {
        var result = await RunAsync();

        result.Should().BeSameAs(_applied);
        _stateTransitions.Verify(s => s.ApplyProviderResultAsync(_payment, It.IsAny<ProviderSessionCreationResult>(), "lease", "corr", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecoverAsync_NoInitiationRequest_DoesNothing()
    {
        _payment.InitiationRequest = null;

        await CreateService().RecoverAsync(_payment, CancellationToken.None);

        _repository.Verify(r => r.TryClaimInitiationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecoverAsync_ClaimNull_DoesNothing()
    {
        _payment.InitiationRequest = new HostedCheckoutSessionRequest();
        _repository.Setup(r => r.TryClaimInitiationAsync("tenant", "pay-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        await CreateService().RecoverAsync(_payment, CancellationToken.None);

        _sessionClient.Verify(c => c.CreateSessionAsync(It.IsAny<PaymentProvider>(), It.IsAny<HostedCheckoutSessionRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecoverAsync_HappyPath_AppliesProviderResult()
    {
        _payment.InitiationRequest = new HostedCheckoutSessionRequest();
        var claimed = new PaymentDetail { ItemId = "pay-1", TenantId = "tenant", CorrelationId = "corr" };
        _repository.Setup(r => r.TryClaimInitiationAsync("tenant", "pay-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimed);

        await CreateService().RecoverAsync(_payment, CancellationToken.None);

        _stateTransitions.Verify(s => s.ApplyProviderResultAsync(claimed, It.IsAny<ProviderSessionCreationResult>(), It.IsAny<string>(), "corr", It.IsAny<CancellationToken>()), Times.Once);
    }

    private delegate void ShopperCallback(string tenantId, string actorId, string key, out string reference);
    private delegate void WebhookCallback(string tenantId, string paymentId, out string reference);
    private delegate void ResolveUrlsCallback(PaymentProvider provider, string signedState, out string returnUrl, out string frontendResultUrl);
}
