using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Entities;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class HostedCheckoutStoredPaymentMethodProviderGatewayTests
{
    [Fact]
    public void Supports_matches_adyen_online_provider_ignoring_case()
    {
        var gateway = Gateway(new Mock<IHttpService>().Object);
        gateway.Supports(PaymentConstants.AdyenOnlineProvider.ToUpperInvariant())
            .Should().BeTrue();
        gateway.Supports("other").Should().BeFalse();
    }

    [Fact]
    public async Task Remove_rejects_unsafe_provider_endpoint_without_calling_http()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://127.0.0.1/v72";

        var result = await Gateway(http.Object).RemoveAsync(
            provider, Method(), "token", CancellationToken.None);

        result.Should().Be(StoredPaymentMethodRemovalOutcome.OperationalFailure);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Remove_builds_scoped_delete_url_and_treats_empty_error_as_removed()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        http.Setup(service => service.Delete<object>(
                "https://checkout-test.adyen.com/v72/storedPaymentMethods/tok%2Fen?merchantAccount=merchant&shopperReference=shopper-1",
                It.Is<Dictionary<string, string>>(headers =>
                    headers["x-api-key"] == "secret"),
                It.IsAny<CancellationToken>(),
                15))
            .ReturnsAsync((new object(), string.Empty));

        var result = await Gateway(http.Object).RemoveAsync(
            Provider(), Method(), "tok/en", CancellationToken.None);

        result.Should().Be(StoredPaymentMethodRemovalOutcome.Removed);
        http.VerifyAll();
    }

    [Theory]
    [InlineData("HTTP 404 not found")]
    [InlineData("resource does not exist")]
    public async Task Remove_treats_already_gone_errors_as_removed(string error)
    {
        var result = await Gateway(HttpReturning(error)).RemoveAsync(
            Provider(), Method(), "token", CancellationToken.None);

        result.Should().Be(StoredPaymentMethodRemovalOutcome.Removed);
    }

    [Theory]
    [InlineData("401 Unauthorized")]
    [InlineData("403 forbidden request")]
    public async Task Remove_maps_authentication_errors_to_operational_failure(string error)
    {
        var result = await Gateway(HttpReturning(error)).RemoveAsync(
            Provider(), Method(), "token", CancellationToken.None);

        result.Should().Be(StoredPaymentMethodRemovalOutcome.OperationalFailure);
    }

    [Theory]
    [InlineData("circuit breaker open")]
    [InlineData("provider timeout waiting")]
    [InlineData("unexpected provider failure")]
    public async Task Remove_maps_transient_errors_to_outcome_unknown(string error)
    {
        var result = await Gateway(HttpReturning(error)).RemoveAsync(
            Provider(), Method(), "token", CancellationToken.None);

        result.Should().Be(StoredPaymentMethodRemovalOutcome.OutcomeUnknown);
    }

    [Fact]
    public async Task Remove_maps_client_cancellation_shim_to_outcome_unknown()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.Delete<object>(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await Gateway(http.Object).RemoveAsync(
            Provider(), Method(), "token", CancellationToken.None);

        result.Should().Be(StoredPaymentMethodRemovalOutcome.OutcomeUnknown);
    }

    [Fact]
    public async Task Remove_propagates_cancellation_when_caller_requested_it()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var http = new Mock<IHttpService>();
        http.Setup(service => service.Delete<object>(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => Gateway(http.Object).RemoveAsync(
            Provider(), Method(), "token", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Remove_maps_unexpected_exception_to_outcome_unknown()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.Delete<object>(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await Gateway(http.Object).RemoveAsync(
            Provider(), Method(), "token", CancellationToken.None);

        result.Should().Be(StoredPaymentMethodRemovalOutcome.OutcomeUnknown);
    }

    private static IHttpService HttpReturning(string error)
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.Delete<object>(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ReturnsAsync((new object(), error));
        return http.Object;
    }

    private static HostedCheckoutStoredPaymentMethodProviderGateway Gateway(
        IHttpService http)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });

        return new HostedCheckoutStoredPaymentMethodProviderGateway(
            http,
            new AdyenEndpointPolicy(),
            options.Object,
            NullLogger<HostedCheckoutStoredPaymentMethodProviderGateway>.Instance);
    }

    private static PaymentProvider Provider() =>
        new()
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            ApiBaseUrl = "https://checkout-test.adyen.com/v72",
            ApiKey = "secret",
            MerchantId = "merchant"
        };

    private static StoredPaymentMethod Method() =>
        new() { ShopperReference = "shopper-1" };
}
