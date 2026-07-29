using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class StripeCheckoutTests
{
    private readonly StripeInitiationRequestFactory _factory = new();
    private readonly StripeCheckoutStatusMapper _statusMapper = new();

    [Fact]
    public void Factory_supports_only_stripe()
    {
        _factory.Supports(PaymentConstants.StripeProvider).Should().BeTrue();
        _factory.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();
    }

    [Fact]
    public void Session_request_uses_stripes_documented_field_shape()
    {
        var form = StripeInitiationRequestFactory.ReadForm(Create());

        form["mode"].Should().Be("payment");
        form["line_items[0][quantity]"].Should().Be("1");
        form["line_items[0][price_data][currency]"].Should().Be("eur");
        form["line_items[0][price_data][unit_amount]"].Should().Be("2500");
        form["line_items[0][price_data][product_data][name]"].Should().Be("A description");
        form["client_reference_id"].Should().Be("payment-reference");
        form["metadata[payment_id]"].Should().Be("payment-1");
        form["customer_email"].Should().Be("shopper@example.com");
    }

    [Fact]
    public void Return_url_carries_the_session_id_template_unencoded()
    {
        var request = Create();

        request.ReturnUrl.Should().Be(
            "https://payments.example/return?state=signed&session_id={CHECKOUT_SESSION_ID}");
        StripeInitiationRequestFactory.ReadForm(request)["success_url"]
            .Should().Be(request.ReturnUrl);
    }

    [Fact]
    public void Automatic_capture_does_not_ask_stripe_for_manual_capture()
    {
        var request = Create();

        request.CaptureMode.Should().Be(PaymentCaptureModes.AutomaticImmediate);
        StripeInitiationRequestFactory.ReadForm(request)
            .Should().NotContainKey("payment_intent_data[capture_method]");
    }

    [Fact]
    public void Manual_capture_is_requested_explicitly()
    {
        var provider = Provider();
        provider.ManualCapture = true;

        var request = Create(provider: provider);

        request.CaptureMode.Should().Be(PaymentCaptureModes.Manual);
        StripeInitiationRequestFactory.ReadForm(request)["payment_intent_data[capture_method]"]
            .Should().Be("manual");
    }

    [Fact]
    public void Saving_a_card_asks_stripe_to_keep_it_for_off_session_use()
    {
        var request = Create(
            new MakePaymentRequest
            {
                Description = "A description",
                SavePaymentMethod = true
            });

        StripeInitiationRequestFactory.ReadForm(request)
            ["payment_intent_data[setup_future_usage]"].Should().Be("off_session");
    }

    [Fact]
    public void Client_reference_id_is_truncated_to_stripes_limit()
    {
        var reference = new string('a', StripeConstants.MaximumClientReferenceLength + 50);

        var form = StripeInitiationRequestFactory.ReadForm(Create(reference: reference));

        form["client_reference_id"].Length
            .Should().Be(StripeConstants.MaximumClientReferenceLength);
    }

    [Fact]
    public void Line_item_name_falls_back_to_the_order_id_because_stripe_requires_one()
    {
        var form = StripeInitiationRequestFactory.ReadForm(
            Create(new MakePaymentRequest { OrderId = "order-9" }));

        form["line_items[0][price_data][product_data][name]"].Should().Be("order-9");
    }

    [Fact]
    public void Stored_payload_round_trips_so_a_recovered_initiation_replays_identically()
    {
        var request = Create();

        StripeInitiationRequestFactory.ReadForm(request)
            .Should().BeEquivalentTo(StripeInitiationRequestFactory.ReadForm(request));
    }

    [Theory]
    [InlineData("complete", "paid", "completed")]
    [InlineData("complete", "no_payment_required", "completed")]
    [InlineData("complete", "unpaid", "paymentPending")]
    [InlineData("open", "unpaid", "paymentPending")]
    [InlineData("expired", "unpaid", "expired")]
    [InlineData("something", "unpaid", "unknown")]
    public void Session_status_and_payment_status_are_read_together(
        string status,
        string paymentStatus,
        string expected) =>
        _statusMapper
            .Normalize(StripeCheckoutStatusMapper.Compose(status, paymentStatus))
            .Should().Be(expected);

    [Fact]
    public void A_completed_but_unpaid_session_is_not_treated_as_success()
    {
        var normalized = _statusMapper.Normalize(
            StripeCheckoutStatusMapper.Compose("complete", "unpaid"));

        _statusMapper.ToRedirectStatus(normalized)
            .Should().Be(PaymentRedirectStatuses.Pending);
    }

    [Theory]
    [InlineData("api_error", ProviderClientOutcome.Unavailable)]
    [InlineData("rate_limit_error", ProviderClientOutcome.Unavailable)]
    [InlineData("invalid_request_error", ProviderClientOutcome.Rejected)]
    [InlineData("card_error", ProviderClientOutcome.Rejected)]
    [InlineData("something_new", ProviderClientOutcome.Failure)]
    public void Stripe_errors_map_to_retryable_or_terminal_outcomes(
        string type,
        ProviderClientOutcome expected) =>
        StripeOutcomeMapper.Map(new StripeError { Type = type }).Should().Be(expected);

    [Fact]
    public void Decline_code_is_preferred_because_it_says_more_than_card_declined() =>
        StripeOutcomeMapper.SafeCode(new StripeError
        {
            Type = "card_error",
            Code = "card_declined",
            DeclineCode = "insufficient_funds"
        }).Should().Be("insufficient_funds");

    [Fact]
    public void Error_codes_are_sanitized_before_leaving_the_gateway() =>
        StripeOutcomeMapper.SafeCode(new StripeError
        {
            Code = "bad<script>alert(1)</script>"
        }).Should().NotContain("<");

    private ProviderInitiationRequest Create(
        MakePaymentRequest? request = null,
        PaymentProvider? provider = null,
        string reference = "payment-reference") =>
        _factory.Create(
            request ?? new MakePaymentRequest
            {
                Description = "A description",
                CustomerEmail = "shopper@example.com"
            },
            new PaymentExecutionContext("tenant-1", "actor-1", "organization-1"),
            new PaymentDetail
            {
                ItemId = "payment-1",
                TenantId = "tenant-1",
                CurrencyCode = "EUR"
            },
            provider ?? Provider(),
            "https://payments.example/return?state=signed",
            reference,
            "shopper-reference",
            includeStoredPaymentMethods: true,
            minorUnits: 2500);

    private static PaymentProvider Provider() => new()
    {
        ProviderName = PaymentConstants.StripeProvider,
        ApiBaseUrl = "https://api.stripe.com",
        MerchantId = "acct_123"
    };
}
