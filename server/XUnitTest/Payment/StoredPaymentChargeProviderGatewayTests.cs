using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Models.StoredPayment;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class StoredPaymentChargeProviderGatewayTests
{
    [Fact]
    public void Supports_matches_adyen_online_provider()
    {
        var gateway = Gateway(new Mock<IHttpService>().Object);
        gateway.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeTrue();
        gateway.Supports("stripe").Should().BeFalse();
    }

    [Fact]
    public async Task Charge_rejects_unsafe_endpoint_with_invalid_endpoint_code()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://10.0.0.5/v72";

        var result = await Gateway(http.Object).ChargeAsync(
            provider, Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Unavailable);
        result.SafeErrorCode.Should().Be("provider_endpoint_invalid");
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Charge_maps_matching_response_to_accepted()
    {
        var request = Request();
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var idempotencyKey = Guid.NewGuid().ToString();
        http.Setup(service => service.SendRequest<StoredPaymentChargeResponse>(
                HttpMethod.Post,
                "https://checkout-test.adyen.com/v72/payments",
                request,
                "application/json",
                It.Is<Dictionary<string, string>>(headers =>
                    headers["x-api-key"] == "secret" &&
                    headers["idempotency-key"] == idempotencyKey),
                It.IsAny<CancellationToken>(),
                15))
            .ReturnsAsync((
                new StoredPaymentChargeResponse
                {
                    PspReference = "charge-psp",
                    MerchantReference = request.Reference,
                    ResultCode = "Authorised",
                    Amount = new ProviderAmount { Value = 1000, Currency = "EUR" }
                },
                string.Empty));

        var result = await Gateway(http.Object).ChargeAsync(
            Provider(), request, idempotencyKey, CancellationToken.None);

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Accepted);
        result.PspReference.Should().Be("charge-psp");
        http.VerifyAll();
    }

    [Fact]
    public async Task Charge_maps_error_code_response_to_rejected()
    {
        var result = await Charge(HttpReturning(
            new StoredPaymentChargeResponse { ErrorCode = "declined-42" }, string.Empty));

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Rejected);
        result.SafeErrorCode.Should().Be("declined-42");
    }

    [Fact]
    public async Task Charge_maps_client_error_status_to_rejected()
    {
        var result = await Charge(HttpReturning(
            new StoredPaymentChargeResponse { Status = 422 }, string.Empty));

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Rejected);
    }

    [Fact]
    public async Task Charge_maps_validation_package_error_to_rejected()
    {
        var result = await Charge(HttpReturning(
            null,
            "{\"status\":400,\"errorType\":\"validation\",\"errorCode\":\"amount_invalid\"}"));

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Rejected);
        result.SafeErrorCode.Should().Be("amount_invalid");
    }

    [Theory]
    [InlineData("circuit breaker open")]
    [InlineData("service unavailable")]
    public async Task Charge_maps_transient_package_error_to_unavailable(string error)
    {
        var result = await Charge(HttpReturning(null, error));
        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Unavailable);
    }

    [Fact]
    public async Task Charge_maps_unusable_response_to_outcome_unknown()
    {
        var result = await Charge(HttpReturning(null, "opaque provider noise"));
        result.Outcome.Should().Be(StoredPaymentChargeOutcome.OutcomeUnknown);
    }

    [Fact]
    public async Task Charge_maps_client_cancellation_shim_to_timeout()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<StoredPaymentChargeResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await Gateway(http.Object).ChargeAsync(
            Provider(), Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Timeout);
    }

    [Fact]
    public async Task Charge_propagates_cancellation_when_caller_requested_it()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<StoredPaymentChargeResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => Gateway(http.Object).ChargeAsync(
            Provider(), Request(), Guid.NewGuid().ToString(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Charge_maps_unexpected_exception_to_outcome_unknown()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<StoredPaymentChargeResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await Gateway(http.Object).ChargeAsync(
            Provider(), Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.OutcomeUnknown);
    }

    private static Task<StoredPaymentChargeProviderResult> Charge(IHttpService http) =>
        Gateway(http).ChargeAsync(
            Provider(), Request(), Guid.NewGuid().ToString(), CancellationToken.None);

    private static IHttpService HttpReturning(
        StoredPaymentChargeResponse? response, string error)
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<StoredPaymentChargeResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync((response!, error));
        return http.Object;
    }

    private static CheckoutApiStoredPaymentChargeProviderGateway Gateway(IHttpService http)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });

        return new CheckoutApiStoredPaymentChargeProviderGateway(
            http,
            new AdyenEndpointPolicy(),
            options.Object,
            NullLogger<CheckoutApiStoredPaymentChargeProviderGateway>.Instance);
    }

    private static PaymentProvider Provider() =>
        new()
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            ApiBaseUrl = "https://checkout-test.adyen.com/v72",
            ApiKey = "secret",
            MerchantId = "merchant"
        };

    private static StoredPaymentChargeRequest Request() =>
        new()
        {
            MerchantAccount = "merchant",
            Reference = $"c1.route.{Guid.NewGuid()}",
            Amount = new ProviderAmount { Currency = "EUR", Value = 1000 }
        };
}
