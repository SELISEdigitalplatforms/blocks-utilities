using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Models.Refunds;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentRefundProviderGatewayTests
{
    [Fact]
    public async Task Submit_uses_provider_payment_endpoint_and_same_idempotency_key()
    {
        var http = new Mock<IHttpService>(
            MockBehavior.Strict);
        var request = Request();
        var idempotencyKey =
            Guid.NewGuid().ToString();

        http.Setup(service =>
                service.SendRequest<
                    ProviderRefundResponse>(
                    HttpMethod.Post,
                    "https://checkout-test.adyen.com/v72/payments/original%2Fpsp/refunds",
                    request,
                    "application/json",
                    It.Is<Dictionary<string, string>>(
                        headers =>
                            headers["x-api-key"] ==
                            "secret" &&
                            headers["idempotency-key"] ==
                            idempotencyKey),
                    It.IsAny<CancellationToken>(),
                    15))
            .ReturnsAsync((
                new ProviderRefundResponse
                {
                    PspReference = "refund-psp",
                    Reference = request.Reference,
                    Status = "received"
                },
                string.Empty));

        var result = await CreateGateway(http.Object)
            .SubmitAsync(
                Provider(),
                "original/psp",
                request,
                idempotencyKey,
                CancellationToken.None);

        result.Outcome.Should().Be(
            PaymentRefundProviderOutcome.Submitted);
        result.ProviderRefundReference
            .Should()
            .Be("refund-psp");
        http.VerifyAll();
    }

    [Fact]
    public async Task Submit_rejects_mismatched_provider_reference_as_unknown()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service =>
                service.SendRequest<
                    ProviderRefundResponse>(
                    It.IsAny<HttpMethod>(),
                    It.IsAny<string>(),
                    It.IsAny<object>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<int?>()))
            .ReturnsAsync((
                new ProviderRefundResponse
                {
                    PspReference = "refund-psp",
                    Reference = "different-reference"
                },
                string.Empty));

        var result = await CreateGateway(http.Object)
            .SubmitAsync(
                Provider(),
                "original-psp",
                Request(),
                Guid.NewGuid().ToString(),
                CancellationToken.None);

        result.Outcome.Should().Be(
            PaymentRefundProviderOutcome
                .OutcomeUnknown);
    }

    [Fact]
    public async Task Reversal_uses_full_payment_reversal_endpoint()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var request = new ProviderReversalRequest
        {
            MerchantAccount = "merchant",
            Reference = $"r1.route.{Guid.NewGuid()}"
        };
        var idempotencyKey = Guid.NewGuid().ToString();

        http.Setup(service => service.SendRequest<
                ProviderRefundResponse>(
                HttpMethod.Post,
                "https://checkout-test.adyen.com/v72/payments/original%2Fpsp/reversals",
                request,
                "application/json",
                It.Is<Dictionary<string, string>>(headers =>
                    headers["idempotency-key"] == idempotencyKey),
                It.IsAny<CancellationToken>(),
                15))
            .ReturnsAsync((
                new ProviderRefundResponse
                {
                    PspReference = "reversal-psp",
                    Reference = request.Reference,
                    Status = "received"
                },
                string.Empty));

        var result = await CreateGateway(http.Object)
            .SubmitReversalAsync(
                Provider(),
                "original/psp",
                request,
                idempotencyKey,
                CancellationToken.None);

        result.Outcome.Should().Be(
            PaymentRefundProviderOutcome.Submitted);
        result.ProviderRefundReference.Should().Be("reversal-psp");
        http.VerifyAll();
    }

    [Fact]
    public async Task Submit_does_not_call_an_unsafe_provider_endpoint()
    {
        var http = new Mock<IHttpService>(
            MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://127.0.0.1/v72";

        var result = await CreateGateway(http.Object)
            .SubmitAsync(
                provider,
                "original-psp",
                Request(),
                Guid.NewGuid().ToString(),
                CancellationToken.None);

        result.Outcome.Should().Be(
            PaymentRefundProviderOutcome.Unavailable);
        http.VerifyNoOtherCalls();
    }

    private static CheckoutApiPaymentRefundProviderGateway
        CreateGateway(IHttpService http)
    {
        var options =
            new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions
            {
                ProviderTimeoutSeconds = 15
            });

        return new CheckoutApiPaymentRefundProviderGateway(
            http,
            new CheckoutUrlPolicy(),
            options.Object,
            NullLogger<
                CheckoutApiPaymentRefundProviderGateway>
                .Instance);
    }

    private static PaymentProvider Provider() =>
        new()
        {
            ProviderName =
                PaymentConstants.AdyenOnlineProvider,
            ApiBaseUrl =
                "https://checkout-test.adyen.com/v72",
            ApiKey = "secret",
            MerchantId = "merchant"
        };

    private static ProviderRefundRequest Request() =>
        new()
        {
            MerchantAccount = "merchant",
            Reference =
                $"r1.route.{Guid.NewGuid()}",
            Amount = new ProviderAmount
            {
                Currency = "EUR",
                Value = 1000
            }
        };
}
