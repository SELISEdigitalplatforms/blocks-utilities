using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.Captures;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentCaptureProviderGatewayTests
{
    [Fact]
    public async Task Submit_uses_capture_endpoint_and_idempotency_key()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var request = new ProviderCaptureRequest
        {
            MerchantAccount = "merchant",
            Reference = $"c1.route.{Guid.NewGuid()}",
            Amount = new ProviderAmount
            {
                Currency = "EUR",
                Value = 1000
            }
        };
        var idempotencyKey = Guid.NewGuid().ToString();

        http.Setup(service => service.SendRequest<
                ProviderCaptureResponse>(
                HttpMethod.Post,
                "https://checkout-test.adyen.com/v72/payments/original%2Fpsp/captures",
                request,
                "application/json",
                It.Is<Dictionary<string, string>>(headers =>
                    headers["x-api-key"] == "secret" &&
                    headers["idempotency-key"] == idempotencyKey),
                It.IsAny<CancellationToken>(),
                15))
            .ReturnsAsync((
                new ProviderCaptureResponse
                {
                    PspReference = "capture-psp",
                    Reference = request.Reference,
                    Status = "received"
                },
                string.Empty));

        var result = await Gateway(http.Object).SubmitAsync(
            Provider(),
            "original/psp",
            request,
            idempotencyKey,
            CancellationToken.None);

        result.Outcome.Should().Be(
            PaymentCaptureProviderOutcome.Submitted);
        result.ProviderCaptureReference.Should().Be("capture-psp");
        http.VerifyAll();
    }

    private static CheckoutApiPaymentCaptureProviderGateway Gateway(
        IHttpService http)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions());

        return new CheckoutApiPaymentCaptureProviderGateway(
            http,
            new AdyenEndpointPolicy(),
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
}
