using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.Captures;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Models.Refunds;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class StripeRefundAndCaptureTests
{
    [Fact]
    public void Gateways_support_only_stripe()
    {
        RefundGateway(Mock.Of<IHttpService>())
            .Supports(PaymentConstants.StripeProvider).Should().BeTrue();
        RefundGateway(Mock.Of<IHttpService>())
            .Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();
        CaptureGateway(Mock.Of<IHttpService>())
            .Supports(PaymentConstants.StripeProvider).Should().BeTrue();
        CaptureGateway(Mock.Of<IHttpService>())
            .Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();
    }

    [Fact]
    public async Task Refund_rejects_unsafe_provider_endpoint_without_calling_http()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://127.0.0.1";

        var result = await RefundGateway(http.Object).SubmitAsync(
            provider, "pi_1", RefundRequest(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Unavailable);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refund_posts_the_intent_amount_and_routing_metadata()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        http.Setup(service => service.SendFormUrlEncoded<StripeRefund>(
                HttpMethod.Post,
                It.Is<Dictionary<string, string>>(form =>
                    form["payment_intent"] == "pi_1" &&
                    form["amount"] == "2500" &&
                    form["metadata[tenant_reference]"] == "r1.token.refund-id" &&
                    // Intake authorizes every event against the merchant recorded on the
                    // payment, and the event carries nothing but this metadata.
                    form["metadata[merchant_account]"] == "merchant"),
                "https://api.stripe.com/v1/refunds",
                It.Is<Dictionary<string, string>>(headers =>
                    headers["Authorization"] == "Bearer secret" &&
                    headers["Idempotency-Key"] == "idem-1"),
                It.IsAny<CancellationToken>(),
                15))
            .ReturnsAsync((new StripeRefund
            {
                Id = "re_1",
                Status = "succeeded",
                PaymentIntent = "pi_1"
            }, string.Empty));

        var result = await RefundGateway(http.Object).SubmitAsync(
            Provider(), "pi_1", RefundRequest(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Submitted);
        result.ProviderRefundReference.Should().Be("re_1");
        http.VerifyAll();
    }

    /// <summary>
    /// A synchronously succeeded refund still settles from the webhook, so it is reported as
    /// submitted rather than short-circuiting the refund state machine.
    /// </summary>
    [Theory]
    [InlineData("succeeded")]
    [InlineData("pending")]
    [InlineData("requires_action")]
    public async Task Refund_treats_every_non_terminal_status_as_submitted(string status)
    {
        var result = await RefundGateway(RefundReturning(
                new StripeRefund { Id = "re_1", Status = status }, string.Empty))
            .SubmitAsync(Provider(), "pi_1", RefundRequest(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Submitted);
        result.ProviderStatus.Should().Be(status);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("canceled")]
    public async Task Refund_treats_terminal_status_as_rejected(string status)
    {
        var result = await RefundGateway(RefundReturning(
                new StripeRefund
                {
                    Id = "re_1",
                    Status = status,
                    FailureReason = "insufficient_funds"
                },
                string.Empty))
            .SubmitAsync(Provider(), "pi_1", RefundRequest(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Rejected);
        result.SafeErrorCode.Should().Be("insufficient_funds");
    }

    [Fact]
    public async Task Refund_maps_stripe_api_errors_to_unavailable_so_the_refund_stays_recoverable()
    {
        var result = await RefundGateway(RefundReturning(
                new StripeRefund { Error = new StripeError { Type = "api_error" } },
                string.Empty))
            .SubmitAsync(Provider(), "pi_1", RefundRequest(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Unavailable);
    }

    [Fact]
    public async Task Refund_maps_invalid_request_errors_to_rejected()
    {
        var result = await RefundGateway(RefundReturning(
                new StripeRefund
                {
                    Error = new StripeError
                    {
                        Type = "invalid_request_error",
                        Code = "charge_already_refunded"
                    }
                },
                string.Empty))
            .SubmitAsync(Provider(), "pi_1", RefundRequest(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Rejected);
        result.SafeErrorCode.Should().Be("charge_already_refunded");
    }

    [Theory]
    [InlineData("circuit breaker open")]
    [InlineData("provider unavailable")]
    public async Task Refund_maps_transient_transport_errors_to_unavailable(string error)
    {
        var result = await RefundGateway(RefundReturning(null, error))
            .SubmitAsync(Provider(), "pi_1", RefundRequest(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Unavailable);
    }

    [Fact]
    public async Task Refund_maps_client_cancellation_shim_to_timeout()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripeRefund>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await RefundGateway(http.Object).SubmitAsync(
            Provider(), "pi_1", RefundRequest(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Timeout);
    }

    [Fact]
    public async Task Refund_propagates_cancellation_when_caller_requested_it()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripeRefund>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => RefundGateway(http.Object).SubmitAsync(
            Provider(), "pi_1", RefundRequest(), "idem-1", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Reversal_cancels_the_intent_and_reports_the_intent_reference()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentIntent>(
                HttpMethod.Post,
                It.Is<Dictionary<string, string>>(form => form.Count == 0),
                "https://api.stripe.com/v1/payment_intents/pi_1/cancel",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                15))
            .ReturnsAsync((new StripePaymentIntent { Id = "pi_1", Status = "canceled" },
                string.Empty));

        var result = await RefundGateway(http.Object).SubmitReversalAsync(
            Provider(),
            "pi_1",
            new ProviderReversalRequest { Reference = "r1.token.refund-id" },
            "idem-1",
            CancellationToken.None);

        // Settled, not submitted: cancelling creates no object at Stripe, so the event it
        // raises names the payment and can never identify this reversal.
        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Settled);
        result.ProviderRefundReference.Should().Be("pi_1");
        http.VerifyAll();
    }

    [Fact]
    public async Task Reversal_rejects_an_intent_that_did_not_cancel()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentIntent>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync((new StripePaymentIntent { Id = "pi_1", Status = "succeeded" },
                string.Empty));

        var result = await RefundGateway(http.Object).SubmitReversalAsync(
            Provider(),
            "pi_1",
            new ProviderReversalRequest { Reference = "r1.token.refund-id" },
            "idem-1",
            CancellationToken.None);

        result.Outcome.Should().Be(PaymentRefundProviderOutcome.Rejected);
    }

    [Fact]
    public async Task Capture_posts_the_amount_to_capture_to_the_intent()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentIntent>(
                HttpMethod.Post,
                It.Is<Dictionary<string, string>>(form =>
                    form["amount_to_capture"] == "2500"),
                "https://api.stripe.com/v1/payment_intents/pi_1/capture",
                It.Is<Dictionary<string, string>>(headers =>
                    headers["Idempotency-Key"] == "idem-1"),
                It.IsAny<CancellationToken>(),
                15))
            .ReturnsAsync((new StripePaymentIntent
            {
                Id = "pi_1",
                Status = "succeeded",
                AmountReceived = 2500
            }, string.Empty));

        var result = await CaptureGateway(http.Object).SubmitAsync(
            Provider(), "pi_1", CaptureRequest(), "idem-1", CancellationToken.None);

        // Settled, not submitted: Stripe raises no event naming the capture, so a capture
        // reported as merely submitted would wait forever for one that cannot arrive.
        result.Outcome.Should().Be(PaymentCaptureProviderOutcome.Settled);
        result.ProviderCaptureReference.Should().Be("pi_1");
        http.VerifyAll();
    }

    /// <summary>
    /// Payment methods that clear asynchronously have not moved the money yet, so reporting
    /// them as settled would record a capture that has not happened.
    /// </summary>
    [Fact]
    public async Task Capture_still_processing_is_not_reported_as_settled()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentIntent>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync((new StripePaymentIntent { Id = "pi_1", Status = "processing" },
                string.Empty));

        var result = await CaptureGateway(http.Object).SubmitAsync(
            Provider(), "pi_1", CaptureRequest(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(PaymentCaptureProviderOutcome.Submitted);
    }

    /// <summary>
    /// Capturing updates the intent in place, so writing the capture's own reference into the
    /// routing key would overwrite the payment's and strand every later event for it.
    /// </summary>
    [Fact]
    public async Task Capture_never_writes_routing_metadata()
    {
        Dictionary<string, string>? sent = null;
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentIntent>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .Callback<HttpMethod, Dictionary<string, string>, string,
                Dictionary<string, string>, CancellationToken, int?>(
                (_, form, _, _, _, _) => sent = form)
            .ReturnsAsync((new StripePaymentIntent { Id = "pi_1", Status = "succeeded" },
                string.Empty));

        await CaptureGateway(http.Object).SubmitAsync(
            Provider(), "pi_1", CaptureRequest(), "idem-1", CancellationToken.None);

        sent.Should().NotBeNull();
        sent!.Keys.Should().NotContain(key =>
            key.Contains("metadata", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Capture_rejects_an_intent_that_did_not_reach_a_captured_state()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentIntent>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync((new StripePaymentIntent
            {
                Id = "pi_1",
                Status = "requires_payment_method"
            }, string.Empty));

        var result = await CaptureGateway(http.Object).SubmitAsync(
            Provider(), "pi_1", CaptureRequest(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(PaymentCaptureProviderOutcome.Rejected);
        result.SafeErrorCode.Should().Be("requires_payment_method");
    }

    [Fact]
    public async Task Capture_rejects_unsafe_provider_endpoint_without_calling_http()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "http://api.stripe.com";

        var result = await CaptureGateway(http.Object).SubmitAsync(
            provider, "pi_1", CaptureRequest(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(PaymentCaptureProviderOutcome.Unavailable);
        http.VerifyNoOtherCalls();
    }

    private static IHttpService RefundReturning(StripeRefund? refund, string error)
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripeRefund>(
                It.IsAny<HttpMethod>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ReturnsAsync((refund!, error));

        return http.Object;
    }

    private static StripeRefundProviderGateway RefundGateway(IHttpService http) =>
        new(http, new StripeEndpointPolicy(), Options(),
            NullLogger<StripeRefundProviderGateway>.Instance);

    private static StripeCaptureProviderGateway CaptureGateway(IHttpService http) =>
        new(http, new StripeEndpointPolicy(), Options(),
            NullLogger<StripeCaptureProviderGateway>.Instance);

    private static IOptionsMonitor<PaymentOptions> Options()
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });

        return options.Object;
    }

    private static ProviderRefundRequest RefundRequest() =>
        new()
        {
            MerchantAccount = "merchant",
            Amount = new ProviderAmount { Value = 2500, Currency = "EUR" },
            Reference = "r1.token.refund-id"
        };

    private static ProviderCaptureRequest CaptureRequest() =>
        new()
        {
            MerchantAccount = "merchant",
            Amount = new ProviderAmount { Value = 2500, Currency = "EUR" },
            Reference = "c1.token.capture-id"
        };

    private static PaymentProvider Provider() =>
        new()
        {
            ProviderName = PaymentConstants.StripeProvider,
            ApiBaseUrl = StripeConstants.ApiBaseUrl,
            ApiKey = "secret",
            MerchantId = "merchant"
        };
}
