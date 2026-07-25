using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.Refunds;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentRefundProviderGatewayBranchTests
{
    [Fact]
    public void Supports_matches_adyen_online_provider()
    {
        var gateway = Gateway(new Mock<IHttpService>().Object);
        gateway.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeTrue();
        gateway.Supports("worldpay").Should().BeFalse();
    }

    [Fact]
    public async Task Submit_maps_error_code_response_to_rejected()
    {
        var result = await Submit(HttpReturning(
            new ProviderRefundResponse { ErrorCode = "refund-declined" }, string.Empty));

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Rejected);
        result.SafeErrorCode.Should().Be("refund-declined");
    }

    [Fact]
    public async Task Submit_maps_validation_package_error_to_rejected()
    {
        var result = await Submit(HttpReturning(
            null,
            "{\"status\":400,\"errorType\":\"validation\",\"errorCode\":\"refund_invalid\"}"));

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Rejected);
        result.SafeErrorCode.Should().Be("refund_invalid");
    }

    [Fact]
    public async Task Submit_maps_transient_error_to_unavailable()
    {
        var result = await Submit(HttpReturning(null, "circuit open"));
        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Unavailable);
    }

    [Fact]
    public async Task Submit_maps_client_cancellation_shim_to_timeout()
    {
        var result = await Gateway(ThrowingHttp(new OperationCanceledException()))
            .SubmitAsync(Provider(), "psp", Request(), Key(), CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Timeout);
    }

    [Fact]
    public async Task Submit_maps_unexpected_exception_to_outcome_unknown()
    {
        var result = await Gateway(ThrowingHttp(new InvalidOperationException("boom")))
            .SubmitAsync(Provider(), "psp", Request(), Key(), CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.OutcomeUnknown);
    }

    [Fact]
    public async Task Submit_propagates_cancellation_when_caller_requested_it()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Gateway(ThrowingHttp(new OperationCanceledException(cts.Token)))
            .SubmitAsync(Provider(), "psp", Request(), Key(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Reversal_rejects_unsafe_provider_endpoint()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://10.1.1.1/v72";

        var result = await Gateway(http.Object).SubmitReversalAsync(
            provider, "psp", Reversal(), Key(), CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Unavailable);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reversal_maps_error_code_response_to_rejected()
    {
        var result = await Reverse(HttpReturning(
            new ProviderRefundResponse { ErrorCode = "reversal-declined" }, string.Empty));

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Rejected);
        result.SafeErrorCode.Should().Be("reversal-declined");
    }

    [Fact]
    public async Task Reversal_maps_validation_package_error_to_rejected()
    {
        var result = await Reverse(HttpReturning(
            null,
            "{\"status\":400,\"errorType\":\"validation\",\"errorCode\":\"reversal_invalid\"}"));

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Rejected);
        result.SafeErrorCode.Should().Be("reversal_invalid");
    }

    [Fact]
    public async Task Reversal_maps_transient_error_to_unavailable()
    {
        var result = await Reverse(HttpReturning(null, "service unavailable"));
        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Unavailable);
    }

    [Fact]
    public async Task Reversal_maps_unusable_response_to_outcome_unknown()
    {
        var result = await Reverse(HttpReturning(null, "opaque"));
        result.Outcome.Should().Be(PaymentRefundProviderOutcome.OutcomeUnknown);
    }

    [Fact]
    public async Task Reversal_maps_client_cancellation_shim_to_timeout()
    {
        var result = await Gateway(ThrowingHttp(new OperationCanceledException()))
            .SubmitReversalAsync(Provider(), "psp", Reversal(), Key(), CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Timeout);
    }

    [Fact]
    public async Task Reversal_maps_unexpected_exception_to_outcome_unknown()
    {
        var result = await Gateway(ThrowingHttp(new InvalidOperationException("boom")))
            .SubmitReversalAsync(Provider(), "psp", Reversal(), Key(), CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.OutcomeUnknown);
    }

    [Fact]
    public async Task Reversal_propagates_cancellation_when_caller_requested_it()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Gateway(ThrowingHttp(new OperationCanceledException(cts.Token)))
            .SubmitReversalAsync(Provider(), "psp", Reversal(), Key(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static string Key() => Guid.NewGuid().ToString();

    private static Task<PaymentRefundProviderResult> Submit(IHttpService http) =>
        Gateway(http).SubmitAsync(
            Provider(), "psp", Request(), Key(), CancellationToken.None);

    private static Task<PaymentRefundProviderResult> Reverse(IHttpService http) =>
        Gateway(http).SubmitReversalAsync(
            Provider(), "psp", Reversal(), Key(), CancellationToken.None);

    private static IHttpService ThrowingHttp(Exception exception)
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<ProviderRefundResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(exception);
        return http.Object;
    }

    private static IHttpService HttpReturning(
        ProviderRefundResponse? response, string error)
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendRequest<ProviderRefundResponse>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync((response!, error));
        return http.Object;
    }

    private static CheckoutApiPaymentRefundProviderGateway Gateway(IHttpService http)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });

        return new CheckoutApiPaymentRefundProviderGateway(
            http,
            new CheckoutUrlPolicy(),
            options.Object,
            NullLogger<CheckoutApiPaymentRefundProviderGateway>.Instance);
    }

    private static PaymentProvider Provider() =>
        new()
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            ApiBaseUrl = "https://checkout-test.adyen.com/v72",
            ApiKey = "secret",
            MerchantId = "merchant"
        };

    private static ProviderRefundRequest Request() =>
        new()
        {
            MerchantAccount = "merchant",
            Reference = $"r1.route.{Guid.NewGuid()}",
            Amount = new ProviderAmount { Currency = "EUR", Value = 1000 }
        };

    private static ProviderReversalRequest Reversal() =>
        new()
        {
            MerchantAccount = "merchant",
            Reference = $"rv1.route.{Guid.NewGuid()}"
        };
}
