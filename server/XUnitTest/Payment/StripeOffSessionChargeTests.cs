using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Models.StoredPayment;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class StripeOffSessionChargeTests
{
    [Fact]
    public void The_gateway_supports_only_stripe()
    {
        var gateway = Gateway(Mock.Of<IHttpService>());

        gateway.Supports(PaymentConstants.StripeProvider).Should().BeTrue();
        gateway.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();
    }

    /// <summary>
    /// <c>off_session</c> and <c>confirm</c> together are what make this a merchant-initiated
    /// charge. Without them Stripe expects a browser to finish the intent, and a charge with
    /// nobody present simply stalls.
    /// </summary>
    [Fact]
    public async Task The_charge_is_confirmed_off_session_against_the_customer()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentIntent>(
                HttpMethod.Post,
                It.Is<Dictionary<string, string>>(form =>
                    form["amount"] == "1250" &&
                    form["currency"] == "eur" &&
                    form["customer"] == "cus_123" &&
                    form["payment_method"] == "pm_456" &&
                    form["off_session"] == "true" &&
                    form["confirm"] == "true" &&
                    form["metadata[tenant_reference]"] == "provider-reference" &&
                    // Intake authorizes every inbound event against the merchant and
                    // organization recorded on the payment.
                    form["metadata[merchant_account]"] == "merchant" &&
                    form["metadata[organization_id]"] == "organization-1" &&
                    form["metadata[shopper_reference]"] == "shopper-reference"),
                "https://api.stripe.com/v1/payment_intents",
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ReturnsAsync((
                new StripePaymentIntent { Id = "pi_1", Status = "succeeded" },
                (string?)null));

        var result = await Gateway(http.Object).ChargeAsync(
            Provider(), Request(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Accepted);
        result.PspReference.Should().Be("pi_1");
        http.VerifyAll();
    }

    /// <summary>
    /// The decline that matters off-session: the card wants the shopper to authenticate, which
    /// cannot happen. Terminal rather than retryable — retrying produces the same answer, and
    /// leaving the payment recoverable would retry it forever.
    /// </summary>
    [Fact]
    public async Task Authentication_required_is_a_terminal_rejection()
    {
        var result = await ChargeWithAsync(
            new StripePaymentIntent
            {
                Error = new StripeError
                {
                    Type = "card_error",
                    Code = "authentication_required",
                    PaymentIntent = new StripeErrorPaymentIntent
                    {
                        Id = "pi_declined"
                    }
                }
            });

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Rejected);
        result.SafeErrorCode.Should().Be("authentication_required");

        // Stripe still created an intent, and its later events name it.
        result.PspReference.Should().Be("pi_declined");
    }

    [Fact]
    public async Task A_stripe_side_error_stays_recoverable()
    {
        var result = await ChargeWithAsync(
            new StripePaymentIntent
            {
                Error = new StripeError { Type = "api_error" }
            });

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Unavailable);
    }

    /// <summary>
    /// <c>requires_action</c> looks like a pending success and is not: it means the card needs
    /// the shopper, so off-session it is finished and declined.
    /// </summary>
    [Theory]
    [InlineData("requires_action", StoredPaymentChargeOutcome.Rejected)]
    [InlineData("requires_payment_method", StoredPaymentChargeOutcome.Rejected)]
    [InlineData("canceled", StoredPaymentChargeOutcome.Rejected)]
    [InlineData("processing", StoredPaymentChargeOutcome.Accepted)]
    [InlineData("succeeded", StoredPaymentChargeOutcome.Accepted)]
    [InlineData("requires_capture", StoredPaymentChargeOutcome.Accepted)]
    public async Task Intent_status_decides_the_charge_outcome(
        string status,
        StoredPaymentChargeOutcome expected)
    {
        var result = await ChargeWithAsync(
            new StripePaymentIntent { Id = "pi_1", Status = status });

        result.Outcome.Should().Be(expected);
    }

    /// <summary>
    /// A card stored before the customer id was recorded cannot be charged: Stripe will not
    /// accept a saved payment method without naming the customer it is attached to. Refused
    /// before the call rather than sent and declined.
    /// </summary>
    [Fact]
    public async Task A_card_with_no_payer_reference_is_rejected_without_calling_stripe()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var request = new StoredPaymentChargeRequest
        {
            MerchantAccount = "merchant",
            Amount = new ProviderAmount { Value = 1250, Currency = "EUR" },
            Reference = "provider-reference",
            PaymentMethod = new StoredPaymentChargeMethod
            {
                StoredPaymentMethodId = "pm_456"
            },
            ShopperReference = "shopper-reference",
            ProviderPayerReference = null
        };

        var result = await Gateway(http.Object).ChargeAsync(
            Provider(), request, "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Rejected);
        result.SafeErrorCode.Should().Be("provider_payer_reference_missing");
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_unsafe_provider_endpoint_is_refused_without_calling_http()
    {
        var http = new Mock<IHttpService>(MockBehavior.Strict);
        var provider = Provider();
        provider.ApiBaseUrl = "https://127.0.0.1";

        var result = await Gateway(http.Object).ChargeAsync(
            provider, Request(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Unavailable);
        http.VerifyNoOtherCalls();
    }

    /// <summary>
    /// A timeout leaves the charge genuinely unknown — the money may have moved — so it must
    /// not be reported as a failure that a caller would retry blindly.
    /// </summary>
    [Fact]
    public async Task A_timeout_reports_an_unknown_outcome_rather_than_a_failure()
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentIntent>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await Gateway(http.Object).ChargeAsync(
            Provider(), Request(), "idem-1", CancellationToken.None);

        result.Outcome.Should().Be(StoredPaymentChargeOutcome.Timeout);
    }

    private static async Task<StoredPaymentChargeProviderResult> ChargeWithAsync(
        StripePaymentIntent response)
    {
        var http = new Mock<IHttpService>();
        http.Setup(service => service.SendFormUrlEncoded<StripePaymentIntent>(
                It.IsAny<HttpMethod>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ReturnsAsync((response, (string?)null));

        return await Gateway(http.Object).ChargeAsync(
            Provider(), Request(), "idem-1", CancellationToken.None);
    }

    private static StripeStoredPaymentChargeProviderGateway Gateway(IHttpService http) =>
        new(http, new StripeEndpointPolicy(), Options(),
            NullLogger<StripeStoredPaymentChargeProviderGateway>.Instance);

    private static IOptionsMonitor<PaymentOptions> Options()
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { ProviderTimeoutSeconds = 15 });

        return options.Object;
    }

    private static StoredPaymentChargeRequest Request() =>
        new()
        {
            MerchantAccount = "merchant",
            Amount = new ProviderAmount { Value = 1250, Currency = "EUR" },
            Reference = "provider-reference",
            PaymentMethod = new StoredPaymentChargeMethod
            {
                StoredPaymentMethodId = "pm_456"
            },
            ShopperReference = "shopper-reference",
            ProviderPayerReference = "cus_123",
            Metadata = new ProviderMetadata
            {
                OrganizationId = "organization-1"
            }
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
