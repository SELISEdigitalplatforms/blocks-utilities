using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.Captures;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentCaptureProviderGatewayBranchTests
{
    [Fact]
    public void Supports_matches_adyen_online_provider()
    {
        var gateway = Gateway(new Mock<IHttpService>().Object);
        gateway.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeTrue();
        gateway.Supports("paypal").Should().BeFalse();
    }

    [Fact]
    public async Task Submit_rejects_unsafe_provider_endpoint()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://192.168.1.5/v72";

        var result = await Gateway(http.Object).SubmitAsync(
            provider, "psp", Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(PaymentCaptureProviderOutcome.Unavailable);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Submit_maps_error_code_response_to_rejected()
    {
        var result = await Submit(HttpReturning(
            new ProviderCaptureResponse { ErrorCode = "capture-declined" }, string.Empty));

        result.Outcome.Should().Be(PaymentCaptureProviderOutcome.Rejected);
        result.SafeErrorCode.Should().Be("capture-declined");
    }

    [Fact]
    public async Task Submit_maps_validation_package_error_to_rejected()
    {
        var result = await Submit(HttpReturning(
            null,
            "{\"status\":422,\"errorType\":\"validation\",\"errorCode\":\"capture_invalid\"}"));

        result.Outcome.Should().Be(PaymentCaptureProviderOutcome.Rejected);
        result.SafeErrorCode.Should().Be("capture_invalid");
    }

    [Theory]
    [InlineData("circuit breaker open")]
    [InlineData("provider unavailable now")]
    public async Task Submit_maps_transient_error_to_unavailable(string error)
    {
        var result = await Submit(HttpReturning(null, error));
        result.Outcome.Should().Be(PaymentCaptureProviderOutcome.Unavailable);
    }

    [Fact]
    public async Task Submit_maps_unusable_response_to_outcome_unknown()
    {
        var result = await Submit(HttpReturning(null, "opaque"));
        result.Outcome.Should().Be(PaymentCaptureProviderOutcome.OutcomeUnknown);
    }

    [Fact]
    public async Task Submit_maps_client_cancellation_shim_to_timeout()
    {
        var http = ThrowingHttp(new OperationCanceledException());

        var result = await Gateway(http).SubmitAsync(
            Provider(), "psp", Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(PaymentCaptureProviderOutcome.Timeout);
    }

    [Fact]
    public async Task Submit_propagates_cancellation_when_caller_requested_it()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var http = ThrowingHttp(new OperationCanceledException(cts.Token));

        var act = () => Gateway(http).SubmitAsync(
            Provider(), "psp", Request(), Guid.NewGuid().ToString(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Submit_maps_unexpected_exception_to_outcome_unknown()
    {
        var http = ThrowingHttp(new InvalidOperationException("boom"));

        var result = await Gateway(http).SubmitAsync(
            Provider(), "psp", Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(PaymentCaptureProviderOutcome.OutcomeUnknown);
    }

    private static Task<PaymentCaptureProviderResult> Submit(IHttpService http) =>
        Gateway(http).SubmitAsync(
            Provider(), "psp", Request(), Guid.NewGuid().ToString(), CancellationToken.None);

    private static IHttpService ThrowingHttp(Exception exception)
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<ProviderCaptureResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(exception);
        return http.Object;
    }

    private static IHttpService HttpReturning(
        ProviderCaptureResponse? response, string error)
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<ProviderCaptureResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync((response!, error));
        return http.Object;
    }

    private static CheckoutApiPaymentCaptureProviderGateway Gateway(IHttpService http)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });

        return new CheckoutApiPaymentCaptureProviderGateway(
            http,
            new CheckoutUrlPolicy(),
            options.Object,
            NullLogger<CheckoutApiPaymentCaptureProviderGateway>.Instance);
    }

    private static PaymentProvider Provider() =>
        new()
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            ApiBaseUrl = "https://checkout-test.adyen.com/v72",
            ApiKey = "secret",
            MerchantId = "merchant"
        };

    private static ProviderCaptureRequest Request() =>
        new()
        {
            MerchantAccount = "merchant",
            Reference = $"c1.route.{Guid.NewGuid()}",
            Amount = new ProviderAmount { Currency = "EUR", Value = 1000 }
        };
}
