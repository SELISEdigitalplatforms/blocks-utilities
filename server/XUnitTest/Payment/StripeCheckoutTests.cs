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

    /// <summary>
    /// Stripe has no shopper field to echo, so the reference that owns a saved card has to
    /// travel as metadata on the intent — the object the authorization event is raised against.
    /// Without it the authorization cannot say whose card was stored.
    /// </summary>
    [Fact]
    public void Shopper_reference_travels_as_metadata_on_the_intent()
    {
        var form = StripeInitiationRequestFactory.ReadForm(Create());

        form["payment_intent_data[metadata][shopper_reference]"]
            .Should().Be("shopper-reference");
        form["metadata[shopper_reference]"].Should().Be("shopper-reference");
    }

    /// <summary>
    /// Every event Stripe raises is routed back by the organization in this metadata, and the
    /// provider that took the money was resolved from the payment's organization. Stamping the
    /// caller's instead sends the events home naming an organization with no provider: intake
    /// answers 404, no state change is ever applied, and the payment stays in Processing
    /// forever while the shopper's card has been charged.
    /// </summary>
    /// <remarks>
    /// The two agreed until a payment could name an organization other than the caller's, which
    /// is why this shipped and only failed in production. Asserted on both copies of the
    /// metadata: the events that report the money are raised against the intent, not the
    /// session.
    /// </remarks>
    [Fact]
    public void Events_are_routed_by_the_payments_organization_not_the_callers()
    {
        var form = StripeInitiationRequestFactory.ReadForm(
            Create(paymentOrganizationId: "organization-2"));

        form[$"metadata[{StripeRoutingMetadata.OrganizationKey}]"]
            .Should().Be("organization-2");
        form[$"payment_intent_data[metadata][{StripeRoutingMetadata.OrganizationKey}]"]
            .Should().Be("organization-2");
    }

    /// <summary>
    /// Saving a card needs a Customer to attach it to, and Checkout in payment mode does not
    /// create one unless asked.
    /// </summary>
    [Fact]
    public void Saving_a_card_asks_stripe_to_create_a_customer()
    {
        var request = Create(
            new MakePaymentRequest
            {
                Description = "A description",
                SavePaymentMethod = true
            });

        StripeInitiationRequestFactory.ReadForm(request)["customer_creation"]
            .Should().Be("always");
    }

    /// <summary>
    /// Naming the customer is what lets Stripe recognise a returning shopper and offer the
    /// cards already saved against them. Without it every payment is a stranger.
    /// </summary>
    [Fact]
    public void A_returning_shopper_is_named_so_stripe_can_offer_their_saved_cards()
    {
        var form = StripeInitiationRequestFactory.ReadForm(
            Create(providerPayerReference: "cus_1"));

        form["customer"].Should().Be("cus_1");

        // Both are rejected by Stripe alongside a customer: it already carries an email, and
        // there is no customer to create.
        form.Should().NotContainKey("customer_creation");
        form.Should().NotContainKey("customer_email");
    }

    /// <summary>
    /// A shopper who already has a customer must reuse it, or each payment would mint another
    /// and leave them with one customer per payment holding a single card each.
    /// </summary>
    [Fact]
    public void A_returning_shopper_saving_another_card_still_reuses_their_customer()
    {
        var form = StripeInitiationRequestFactory.ReadForm(
            Create(
                new MakePaymentRequest
                {
                    Description = "A description",
                    SavePaymentMethod = true
                },
                providerPayerReference: "cus_1"));

        form["customer"].Should().Be("cus_1");
        form.Should().NotContainKey("customer_creation");
        form["saved_payment_method_options[payment_method_save]"].Should().Be("enabled");
    }

    [Fact]
    public void A_payment_without_save_consent_does_not_create_a_customer() =>
        StripeInitiationRequestFactory.ReadForm(Create())
            .Should().NotContainKey("customer_creation");

    [Fact]
    public void Return_url_carries_the_session_id_template_unencoded()
    {
        var request = Create();

        request.ReturnUrl.Should().Be(
            "https://payments.example/return?state=signed&sessionId={CHECKOUT_SESSION_ID}");
        StripeInitiationRequestFactory.ReadForm(request)["success_url"]
            .Should().Be(request.ReturnUrl);
    }

    /// <summary>
    /// The callback endpoint binds <c>sessionId</c>. Stripe's conventional <c>session_id</c>
    /// would not bind, leaving the session id null and rejecting every return as an invalid
    /// callback request.
    /// </summary>
    [Fact]
    public void Return_url_names_the_session_parameter_as_the_callback_endpoint_binds_it()
    {
        var returnUrl = Create().ReturnUrl;

        returnUrl.Should().Contain("sessionId={CHECKOUT_SESSION_ID}");
        returnUrl.Should().NotContain("session_id=");
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

    /// <summary>
    /// Saving a card needs both parameters, because Stripe treats them as separate purposes.
    /// </summary>
    /// <remarks>
    /// <c>payment_method_save</c> makes Stripe collect the consent itself and marks the card
    /// displayable; a card saved through <c>setup_future_usage</c> alone can never be offered
    /// back to the shopper, with no way to change that afterwards.
    /// <c>setup_future_usage</c> is what establishes the mandate permitting a later charge with
    /// nobody present. Send only the first and saved cards display but cannot be charged
    /// off-session; send only the second and they can be charged but never reappear.
    /// </remarks>
    [Fact]
    public void Saving_a_card_asks_stripe_to_collect_consent_and_takes_an_off_session_mandate()
    {
        var form = StripeInitiationRequestFactory.ReadForm(Create(
            new MakePaymentRequest
            {
                Description = "A description",
                SavePaymentMethod = true
            }));

        form["saved_payment_method_options[payment_method_save]"].Should().Be("enabled");
        form["payment_intent_data[setup_future_usage]"].Should().Be("off_session");
    }

    /// <summary>
    /// No consent, no mandate. Taking one anyway would claim a right to charge the card later
    /// that the shopper never granted.
    /// </summary>
    [Fact]
    public void A_payment_without_save_consent_does_not_ask_stripe_to_save()
    {
        var form = StripeInitiationRequestFactory.ReadForm(Create());

        form.Should().NotContainKey("saved_payment_method_options[payment_method_save]");
        form.Should().NotContainKey("payment_intent_data[setup_future_usage]");
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
        string reference = "payment-reference",
        string? providerPayerReference = null,
        string? paymentOrganizationId = null) =>
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
                CurrencyCode = "EUR",
                OrganizationId = paymentOrganizationId
            },
            provider ?? Provider(),
            "https://payments.example/return?state=signed",
            reference,
            "shopper-reference",
            providerPayerReference,
            includeStoredPaymentMethods: true,
            minorUnits: 2500);

    private static PaymentProvider Provider() => new()
    {
        ProviderName = PaymentConstants.StripeProvider,
        ApiBaseUrl = "https://api.stripe.com",
        MerchantId = "acct_123"
    };
}
