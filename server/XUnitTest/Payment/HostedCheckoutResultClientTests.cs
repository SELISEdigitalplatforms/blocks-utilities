using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class HostedCheckoutResultClientTests
{
    [Fact]
    public async Task Get_rejects_unsafe_provider_endpoint_without_calling_http()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://127.0.0.1/v72";

        var result = await Client(http.Object).GetAsync(
            provider, "session-1", "result-token", CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Unavailable);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_builds_session_url_and_maps_complete_response_to_success()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var response = new HostedCheckoutResult
        {
            Id = "session-1",
            Reference = "order-1",
            Status = "completed"
        };
        http.Setup(service => service.SendRequest<HostedCheckoutResult>(
                HttpMethod.Get,
                "https://checkout-test.adyen.com/v72/sessions/session-1?sessionResult=result%2Ftoken",
                null!,
                "application/json",
                It.Is<Dictionary<string, string>>(headers =>
                    headers["x-api-key"] == "secret"),
                It.IsAny<CancellationToken>(),
                15))
            .ReturnsAsync((response, string.Empty));

        var result = await Client(http.Object).GetAsync(
            Provider(), "session-1", "result/token", CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Success);
        result.Response.Should().BeSameAs(response);
        http.VerifyAll();
    }

    [Fact]
    public async Task Get_maps_error_code_response_to_rejected()
    {
        var result = await Client(HttpReturning(
                new HostedCheckoutResult { ErrorCode = "declined" }, string.Empty))
            .GetAsync(Provider(), "session-1", "token", CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Rejected);
    }

    [Theory]
    [InlineData("circuit breaker open")]
    [InlineData("provider unavailable")]
    public async Task Get_maps_transient_package_errors_to_unavailable(string error)
    {
        var result = await Client(HttpReturning(null, error))
            .GetAsync(Provider(), "session-1", "token", CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Unavailable);
    }

    [Fact]
    public async Task Get_maps_incomplete_response_to_failure()
    {
        var result = await Client(HttpReturning(
                new HostedCheckoutResult { Id = "session-1" }, string.Empty))
            .GetAsync(Provider(), "session-1", "token", CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Failure);
    }

    [Fact]
    public async Task Get_maps_client_cancellation_shim_to_timeout()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<HostedCheckoutResult>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await Client(http.Object).GetAsync(
            Provider(), "session-1", "token", CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Timeout);
    }

    [Fact]
    public async Task Get_propagates_cancellation_when_caller_requested_it()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<HostedCheckoutResult>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => Client(http.Object).GetAsync(
            Provider(), "session-1", "token", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Get_maps_unexpected_exception_to_failure()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<HostedCheckoutResult>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await Client(http.Object).GetAsync(
            Provider(), "session-1", "token", CancellationToken.None);

        result.Outcome.Should().Be(ProviderClientOutcome.Failure);
    }

    private static IHttpService HttpReturning(HostedCheckoutResult? response, string error)
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<HostedCheckoutResult>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync((response!, error));
        return http.Object;
    }

    private static HostedCheckoutResultClient Client(IHttpService http)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });

        return new HostedCheckoutResultClient(
            http,
            new AdyenEndpointPolicy(),
            options.Object,
            NullLogger<HostedCheckoutResultClient>.Instance);
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
