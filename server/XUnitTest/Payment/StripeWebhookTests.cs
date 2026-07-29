using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.Webhooks;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class StripeWebhookTests
{
    private const string Secret = "whsec_test_secret";

    private readonly StripeWebhookNormalizer _normalizer = new();
    private readonly StripeWebhookSignatureVerifier _verifier;

    public StripeWebhookTests()
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(x => x.CurrentValue).Returns(new PaymentOptions());
        _verifier = new StripeWebhookSignatureVerifier(options.Object);
    }

    [Fact]
    public void Both_components_support_only_stripe()
    {
        _normalizer.Supports(PaymentConstants.StripeProvider).Should().BeTrue();
        _normalizer.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();
        _verifier.Supports(PaymentConstants.StripeProvider).Should().BeTrue();
        _verifier.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();
    }

    [Theory]
    [InlineData("payment_intent.succeeded", WebhookIntent.Authorization)]
    [InlineData("payment_intent.amount_capturable_updated", WebhookIntent.Authorization)]
    [InlineData("payment_intent.payment_failed", WebhookIntent.Authorization)]
    [InlineData("checkout.session.async_payment_succeeded", WebhookIntent.Authorization)]
    [InlineData("checkout.session.async_payment_failed", WebhookIntent.Authorization)]
    [InlineData("checkout.session.expired", WebhookIntent.Cancelled)]
    [InlineData("payment_intent.canceled", WebhookIntent.Cancelled)]
    [InlineData("refund.updated", WebhookIntent.Refund)]
    [InlineData("charge.refund.updated", WebhookIntent.Refund)]
    [InlineData("checkout.session.completed", WebhookIntent.Ignored)]
    // A payment method object carries no routing information of any kind, so it can never be
    // matched to a tenant. Cards are recorded from the authorization event instead.
    [InlineData("payment_method.attached", WebhookIntent.Ignored)]
    [InlineData("payment_method.detached", WebhookIntent.Ignored)]
    // The charge carries the payment's routing reference, not the refund's, so it cannot
    // identify which refund settled and is left to the refund's own events.
    [InlineData("charge.refunded", WebhookIntent.Ignored)]
    [InlineData("invoice.paid", WebhookIntent.Ignored)]
    public void Event_types_translate_to_intents(string eventType, WebhookIntent expected)
    {
        var parsed = Parse(Body(eventType));

        parsed.Events.Should().ContainSingle().Which.Intent.Should().Be(expected);
    }

    [Theory]
    [InlineData("payment_intent.succeeded", true)]
    [InlineData("payment_intent.payment_failed", false)]
    [InlineData("checkout.session.async_payment_failed", false)]
    public void Outcome_is_derived_from_the_event_name(string eventType, bool expected) =>
        Parse(Body(eventType)).Events.Single().Payload.Success.Should().Be(expected);

    [Fact]
    public void Routing_reference_is_read_from_metadata_on_a_payment_intent() =>
        Parse(Body("payment_intent.succeeded")).Events.Single()
            .RoutingReference.Should().Be("routing-1");

    [Fact]
    public void Routing_reference_is_read_from_client_reference_id_on_a_session()
    {
        const string body =
            "{\"id\":\"evt_1\",\"type\":\"checkout.session.completed\",\"created\":1700000000," +
            "\"data\":{\"object\":{\"id\":\"cs_1\",\"client_reference_id\":\"routing-1\"}}}";

        Parse(body).Events.Single().RoutingReference.Should().Be("routing-1");
    }

    [Fact]
    public void Event_id_is_used_as_the_deduplication_seed() =>
        Parse(Body("payment_intent.succeeded")).Events.Single()
            .DeduplicationSeed.Should().Be("evt_1");

    [Fact]
    public void Payload_normalizes_amount_currency_and_merchant()
    {
        var payload = Parse(Body("payment_intent.succeeded")).Events.Single().Payload;

        payload.ProviderName.Should().Be(PaymentConstants.StripeProvider);
        payload.AmountMinorUnits.Should().Be(2500);
        payload.CurrencyCode.Should().Be("EUR");
        payload.MerchantAccount.Should().Be("acct_123");
        payload.PspReference.Should().Be("pi_1");
    }

    /// <summary>
    /// The saved card is the payment method the intent was paid with. Stripe reports it only
    /// on the authorization, so without this the card is never recorded.
    /// </summary>
    [Fact]
    public void Authorization_carries_the_saved_card_token()
    {
        var payload = Parse(Body("payment_intent.succeeded")).Events.Single().Payload;

        payload.StoredPaymentMethodToken.Should().Be("pm_1");
    }

    /// <summary>
    /// Storing a card checks the echoed shopper reference against the one recorded on the
    /// payment. Stripe's customer id is a different identifier and would never match, so it
    /// must not be mistaken for the shopper reference.
    /// </summary>
    [Fact]
    public void Shopper_reference_comes_from_metadata_not_from_the_stripe_customer()
    {
        var payload = Parse(Body("payment_intent.succeeded")).Events.Single().Payload;

        payload.ShopperReference.Should().Be("s1.token.abcdef");
        payload.ProviderPayerReference.Should().Be("cus_1");
    }

    [Fact]
    public void A_non_authorization_event_carries_no_card_token() =>
        Parse(Body("payment_intent.canceled")).Events.Single()
            .Payload.StoredPaymentMethodToken.Should().BeNull();

    /// <summary>
    /// A card refund is created already succeeded and never updates again, so creation is the
    /// only event that will ever report it. Ignoring it left the refund submitted forever, and
    /// because the refund's own routing reference was on it, intake rejected the delivery as
    /// an event mismatch and Stripe retried it indefinitely.
    /// </summary>
    [Fact]
    public void A_created_refund_is_a_refund_outcome()
    {
        var parsed = Parse(RefundBody("refund.created", "succeeded")).Events.Single();

        parsed.Intent.Should().Be(WebhookIntent.Refund);
        parsed.Payload.Success.Should().BeTrue();
        parsed.RoutingReference.Should().Be("r1.token.refund-id");
    }

    [Theory]
    [InlineData("refund.created")]
    [InlineData("refund.updated")]
    public void A_failed_refund_is_not_reported_as_success(string eventType) =>
        Parse(RefundBody(eventType, "failed")).Events.Single()
            .Payload.Success.Should().BeFalse();

    /// <summary>
    /// The refund record is tracked by the refund's own reference. Reporting the payment
    /// intent here would overwrite it with the payment's.
    /// </summary>
    [Fact]
    public void A_refund_event_reports_the_refund_reference_not_the_payment_intent()
    {
        var payload = Parse(RefundBody("refund.created", "succeeded")).Events.Single().Payload;

        payload.PspReference.Should().Be("re_1");
        payload.OriginalPspReference.Should().Be("pi_1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_body_is_malformed(string body) =>
        Parse(body).RejectionReason.Should().Be("empty_body");

    [Fact]
    public void Missing_signature_header_is_malformed() =>
        _normalizer.Parse(Body("payment_intent.succeeded"), new Dictionary<string, string>())
            .RejectionReason.Should().Be("missing_signature");

    [Fact]
    public void Invalid_json_is_malformed() =>
        Parse("{not-json").RejectionReason.Should().Be("invalid_json");

    [Fact]
    public void Event_without_a_data_object_is_malformed() =>
        Parse("{\"id\":\"evt_1\",\"type\":\"payment_intent.succeeded\"}")
            .RejectionReason.Should().Be("invalid_event_envelope");

    [Fact]
    public void A_correctly_signed_event_is_accepted()
    {
        var body = Body("payment_intent.succeeded");
        var signature = Sign(body, Secret);

        Verify(body, signature).Should().Be(WebhookSignatureOutcome.Valid);
    }

    [Fact]
    public void A_tampered_body_is_rejected()
    {
        var signature = Sign(Body("payment_intent.succeeded"), Secret);

        Verify(Body("payment_intent.payment_failed"), signature)
            .Should().Be(WebhookSignatureOutcome.Invalid);
    }

    [Fact]
    public void A_signature_from_another_secret_is_rejected()
    {
        var body = Body("payment_intent.succeeded");

        Verify(body, Sign(body, "whsec_other")).Should().Be(WebhookSignatureOutcome.Invalid);
    }

    [Fact]
    public void The_previous_secret_is_accepted_during_a_roll()
    {
        var body = Body("payment_intent.succeeded");
        var provider = Provider();
        provider.StandardWebhookHmacKey = "whsec_rolled";
        provider.PreviousStandardWebhookHmacKey = Secret;

        Verify(body, Sign(body, Secret), provider)
            .Should().Be(WebhookSignatureOutcome.Valid);
    }

    [Fact]
    public void Any_matching_signature_in_the_header_is_enough()
    {
        var body = Body("payment_intent.succeeded");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header =
            $"t={timestamp},v1={Digest(timestamp, body, "whsec_other")},v1={Digest(timestamp, body, Secret)}";

        Verify(body, header).Should().Be(WebhookSignatureOutcome.Valid);
    }

    [Fact]
    public void A_replayed_event_outside_the_window_is_reported_separately_from_a_forgery()
    {
        var body = Body("payment_intent.succeeded");
        var old = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds();

        Verify(body, $"t={old},v1={Digest(old, body, Secret)}")
            .Should().Be(WebhookSignatureOutcome.Expired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1=abc")]
    [InlineData("t=notanumber,v1=abc")]
    [InlineData("t=1700000000")]
    [InlineData("t=1700000000,v0=abc")]
    public void A_malformed_signature_header_is_rejected(string header) =>
        Verify(Body("payment_intent.succeeded"), header)
            .Should().Be(WebhookSignatureOutcome.Invalid);

    [Fact]
    public void A_provider_without_a_signing_secret_cannot_verify_anything()
    {
        var provider = Provider();
        provider.StandardWebhookHmacKey = null;

        Verify(Body("payment_intent.succeeded"), "t=1,v1=ab", provider)
            .Should().Be(WebhookSignatureOutcome.NotConfigured);
    }

    private WebhookParseResult Parse(string body) =>
        _normalizer.Parse(
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [StripeConstants.SignatureHeader] = Sign(body, Secret)
            });

    private WebhookSignatureOutcome Verify(
        string body,
        string signatureHeader,
        PaymentProvider? provider = null)
    {
        var parsed = _normalizer.Parse(
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [StripeConstants.SignatureHeader] = signatureHeader
            });

        return parsed.IsValid
            ? _verifier.Verify(provider ?? Provider(), parsed.Events.Single().Signature)
            : _verifier.Verify(
                provider ?? Provider(),
                new WebhookSignature(
                    body,
                    signatureHeader,
                    StripeConstants.WebhookSecretName));
    }

    private static string Sign(string body, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return $"t={timestamp},v1={Digest(timestamp, body, secret)}";
    }

    private static string Digest(long timestamp, string body, string secret) =>
        Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(secret),
                    Encoding.UTF8.GetBytes(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{timestamp}.{body}"))))
            .ToLowerInvariant();

    private static PaymentProvider Provider() => new()
    {
        ProviderName = PaymentConstants.StripeProvider,
        MerchantId = "acct_123",
        StandardWebhookHmacKey = Secret
    };

    /// <summary>Shaped after a real refund.created delivery.</summary>
    private static string RefundBody(string eventType, string status) =>
        "{\"id\":\"evt_1\",\"type\":\"" + eventType + "\",\"created\":1700000000," +
        "\"data\":{\"object\":{\"id\":\"re_1\",\"object\":\"refund\",\"amount\":1000," +
        "\"currency\":\"chf\",\"charge\":\"ch_1\",\"payment_intent\":\"pi_1\"," +
        "\"status\":\"" + status + "\"," +
        "\"metadata\":{\"tenant_reference\":\"r1.token.refund-id\"}}}}";

    private static string Body(string eventType) =>
        "{\"id\":\"evt_1\",\"type\":\"" + eventType + "\",\"created\":1700000000," +
        "\"data\":{\"object\":{\"id\":\"pi_1\",\"object\":\"payment_intent\"," +
        "\"amount\":2500,\"currency\":\"eur\",\"status\":\"succeeded\"," +
        "\"payment_method\":\"pm_1\",\"customer\":\"cus_1\"," +
        "\"metadata\":{\"tenant_reference\":\"routing-1\",\"payment_id\":\"payment-1\"," +
        "\"shopper_reference\":\"s1.token.abcdef\"," +
        "\"merchant_account\":\"acct_123\"}}}}";
}
