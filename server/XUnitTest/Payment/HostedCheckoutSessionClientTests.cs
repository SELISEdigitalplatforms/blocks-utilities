using Blocks.Genesis;
using FluentAssertions;
using MongoDB.Bson;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class HostedCheckoutSessionClientTests
{
    [Fact]
    public async Task Create_session_uses_Genesis_client_with_required_headers_timeout_and_cancellation()
    {
        var response = new HostedCheckoutSessionResponse
        {
            Id = "session-id",
            SessionData = "session-data",
            Url = "https://checkoutshopper-test.adyen.com/session"
        };
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        using var source = new CancellationTokenSource();
        http.Setup(x => x.SendRequest<HostedCheckoutSessionResponse>(
                HttpMethod.Post,
                "https://checkout-test.adyen.com/v72/sessions",
                It.IsAny<object>(),
                "application/json",
                It.Is<Dictionary<string, string>>(headers =>
                    headers["x-api-key"] == "top-secret" &&
                    headers["idempotency-key"] == "4b82f20f-d96b-4078-a686-bd27843fae02"),
                source.Token,
                15))
            .ReturnsAsync((response, string.Empty));
        var client = CreateClient(http.Object);

        var result = await client.CreateSessionAsync(
            Provider(),
            Request(),
            "4b82f20f-d96b-4078-a686-bd27843fae02",
            source.Token);

        result.Outcome.Should().Be(ProviderClientOutcome.Success);
        result.Response.Should().BeSameAs(response);
        http.VerifyAll();
    }

    [Fact]
    public async Task Create_session_maps_recognizable_rejection_to_sanitized_code()
    {
        var http = new Mock<IHttpService>();
        http.Setup(x => x.SendRequest<HostedCheckoutSessionResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync((new HostedCheckoutSessionResponse
            {
                Status = 422,
                ErrorCode = "14_905<script>alert-secret</script>"
            }, string.Empty));

        var result = await CreateClient(http.Object).CreateSessionAsync(Provider(), Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Rejected);
        result.ProviderErrorCode.Should().Be("14_905scriptalert-secretscript");
    }

    [Theory]
    [InlineData("{\"status\":422,\"errorCode\":\"14_0408\",\"message\":\"There are no payment methods available for the given parameters.\",\"errorType\":\"validation\",\"pspReference\":\"sensitive-reference\"}")]
    [InlineData("HTTP request failed with status code 422. Error: {\"status\":422,\"errorCode\":\"14_0408\",\"errorType\":\"validation\"}")]
    public async Task Create_session_maps_package_validation_error_to_rejection(
        string packageError)
    {
        var http = new Mock<IHttpService>();
        http.Setup(x => x.SendRequest<HostedCheckoutSessionResponse>(
                It.IsAny<HttpMethod>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ReturnsAsync(((HostedCheckoutSessionResponse)null!, packageError));

        var result = await CreateClient(http.Object).CreateSessionAsync(
            Provider(),
            Request(),
            Guid.NewGuid().ToString(),
            CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Rejected);
        result.ProviderErrorCode.Should().Be("14_0408");
    }

    [Fact]
    public async Task Create_session_does_not_expose_raw_package_error()
    {
        var http = new Mock<IHttpService>();
        http.Setup(x => x.SendRequest<HostedCheckoutSessionResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync(((HostedCheckoutSessionResponse)null!, "secret package failure with credentials"));

        var result = await CreateClient(http.Object).CreateSessionAsync(Provider(), Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Failure);
        result.ProviderErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task Create_session_maps_package_circuit_result_to_unavailable()
    {
        var http = new Mock<IHttpService>();
        http.Setup(x => x.SendRequest<HostedCheckoutSessionResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync(((HostedCheckoutSessionResponse)null!, "Circuit is open"));

        var result = await CreateClient(http.Object).CreateSessionAsync(Provider(), Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Unavailable);
    }

    [Fact]
    public async Task Create_session_maps_internal_timeout_to_unknown_timeout()
    {
        var http = new Mock<IHttpService>();
        http.Setup(x => x.SendRequest<HostedCheckoutSessionResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await CreateClient(http.Object).CreateSessionAsync(Provider(), Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Timeout);
    }

    [Fact]
    public async Task Create_session_refuses_an_unsafe_provider_endpoint_without_calling_http()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://127.0.0.1/v72";

        var result = await CreateClient(http.Object).CreateSessionAsync(provider, Request(), Guid.NewGuid().ToString(), CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Unavailable);
        http.VerifyNoOtherCalls();
    }

    private static HostedCheckoutSessionClient CreateClient(IHttpService httpService)
    {
        var monitor = new Mock<IOptionsMonitor<PaymentOptions>>();
        monitor.SetupGet(x => x.CurrentValue).Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });
        return new HostedCheckoutSessionClient(httpService, new AdyenEndpointPolicy(), monitor.Object, NullLogger<HostedCheckoutSessionClient>.Instance);
    }

    private static PaymentProvider Provider() => new()
    {
        ProviderName = PaymentConstants.AdyenOnlineProvider,
        ApiBaseUrl = "https://checkout-test.adyen.com/v72",
        ApiKey = "top-secret",
        MerchantId = "merchant"
    };

    private static ProviderInitiationRequest Request()
    {
        var session = new HostedCheckoutSessionRequest
        {
            MerchantAccount = "merchant",
            Amount = new ProviderAmount { Currency = "USD", Value = 1000 },
            Reference = "payment-1",
            ReturnUrl = "https://merchant.example/return"
        };

        return new ProviderInitiationRequest
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            Reference = session.Reference,
            MerchantAccount = session.MerchantAccount,
            AmountMinorUnits = session.Amount.Value,
            CurrencyCode = session.Amount.Currency,
            ReturnUrl = session.ReturnUrl,
            Payload = session.ToBsonDocument()
        };
    }
}
