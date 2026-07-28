using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.Adyen;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class AdyenWebhookNormalizerTests
{
    private static readonly IReadOnlyDictionary<string, string> NoHeaders =
        new Dictionary<string, string>();

    private readonly AdyenWebhookNormalizer _normalizer =
        new(new ProviderFailureReasonMapper());

    [Fact]
    public void Supports_only_the_adyen_online_provider()
    {
        _normalizer.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeTrue();
        _normalizer.Supports("STRIPE").Should().BeFalse();
    }

    [Theory]
    [InlineData("AUTHORISATION", WebhookIntent.Authorization)]
    [InlineData("authorisation", WebhookIntent.Authorization)]
    [InlineData("REFUND", WebhookIntent.Refund)]
    [InlineData("REFUND_FAILED", WebhookIntent.Refund)]
    [InlineData("REFUNDED_REVERSED", WebhookIntent.Refund)]
    [InlineData("CANCEL_OR_REFUND", WebhookIntent.Refund)]
    [InlineData("CAPTURE", WebhookIntent.Capture)]
    [InlineData("capture_failed", WebhookIntent.Capture)]
    [InlineData("SOMETHING_ELSE", WebhookIntent.Ignored)]
    public void Event_codes_translate_to_intents(string eventCode, WebhookIntent expected)
    {
        var result = _normalizer.Parse(StandardBody(Item(eventCode)), NoHeaders);

        result.IsValid.Should().BeTrue();
        result.Events.Should().ContainSingle()
            .Which.Intent.Should().Be(expected);
    }

    [Fact]
    public void Standard_events_carry_the_canonical_payload_the_verifier_needs()
    {
        var result = _normalizer.Parse(StandardBody(Item("AUTHORISATION")), NoHeaders);

        var signature = result.Events.Single().Signature;
        signature.SecretName.Should().Be("standard");
        signature.SuppliedSignature.Should().Be("supplied-signature");
        signature.SignedPayload.Should().Be(
            "psp-1::merchant:reference-1:1050:USD:AUTHORISATION:true");
    }

    [Fact]
    public void Standard_events_normalize_into_the_shared_payload_shape()
    {
        var item = Item("AUTHORISATION");
        item.AdditionalData["cardSummary"] = "4242";
        item.AdditionalData["expiryDate"] = "03/2030";
        item.AdditionalData["authCode"] = "auth-1";

        var payload = _normalizer.Parse(StandardBody(item), NoHeaders).Events.Single().Payload;

        payload.ProviderName.Should().Be(PaymentConstants.AdyenOnlineProvider);
        payload.PspReference.Should().Be("psp-1");
        payload.MerchantAccount.Should().Be("merchant");
        payload.Success.Should().BeTrue();
        payload.AmountMinorUnits.Should().Be(1050);
        payload.CurrencyCode.Should().Be("USD");
        payload.LastFour.Should().Be("4242");
        payload.ExpiryMonth.Should().Be("03");
        payload.ExpiryYear.Should().Be("2030");
        payload.AuthorizationCode.Should().Be("auth-1");
        payload.PaymentDetailId.Should().BeNull("routing identifiers are filled in by intake");
    }

    [Fact]
    public void Echoed_tenant_metadata_is_decoded_for_the_consistency_check()
    {
        var item = Item("AUTHORISATION");
        item.AdditionalData["metadata.value_a"] =
            Convert.ToBase64String(Encoding.UTF8.GetBytes("tenant-1"));

        _normalizer.Parse(StandardBody(item), NoHeaders).Events.Single()
            .EchoedTenantId.Should().Be("tenant-1");
    }

    [Fact]
    public void Unreadable_tenant_metadata_cannot_confirm_the_tenant()
    {
        var item = Item("AUTHORISATION");
        item.AdditionalData["metadata.value_a"] = "not-base64!";

        _normalizer.Parse(StandardBody(item), NoHeaders).Events.Single()
            .EchoedTenantId.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_body_is_malformed(string rawBody) =>
        _normalizer.Parse(rawBody, NoHeaders).RejectionReason.Should().Be("empty_body");

    [Fact]
    public void Invalid_json_is_malformed() =>
        _normalizer.Parse("{not-json", NoHeaders).RejectionReason.Should().Be("invalid_json");

    [Fact]
    public void Standard_batch_larger_than_the_cap_is_malformed()
    {
        var items = Enumerable.Range(0, 101).Select(_ => Item("AUTHORISATION")).ToArray();

        _normalizer.Parse(StandardBody(items), NoHeaders)
            .RejectionReason.Should().Be("invalid_notification_collection");
    }

    [Fact]
    public void Standard_event_without_a_signature_is_malformed()
    {
        var item = Item("AUTHORISATION");
        item.AdditionalData.Remove("hmacSignature");

        _normalizer.Parse(StandardBody(item), NoHeaders)
            .RejectionReason.Should().Be("missing_signature");
    }

    [Fact]
    public void Token_events_are_signed_over_the_untouched_body()
    {
        var body = TokenBody("recurring.token.created");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hmacsignature"] = "supplied-signature"
        };

        var result = _normalizer.Parse(body, headers);

        var parsed = result.Events.Should().ContainSingle().Subject;
        parsed.Intent.Should().Be(WebhookIntent.StoredMethod);
        parsed.WebhookType.Should().Be("token");
        parsed.RoutingReference.Should().Be("shopper-1");
        parsed.Signature.SecretName.Should().Be("token");
        parsed.Signature.SignedPayload.Should().Be(body);
        parsed.Payload.StoredPaymentMethodToken.Should().Be("token-1");
    }

    [Fact]
    public void Token_event_of_an_unknown_type_is_malformed() =>
        _normalizer.Parse(
                TokenBody("recurring.token.unknown"),
                new Dictionary<string, string> { ["hmacsignature"] = "s" })
            .RejectionReason.Should().Be("invalid_event_envelope");

    [Fact]
    public void Token_event_with_an_unsupported_protocol_is_malformed() =>
        _normalizer.Parse(
                TokenBody("recurring.token.created"),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["protocol"] = "HmacSHA1",
                    ["hmacsignature"] = "s"
                })
            .RejectionReason.Should().Be("unsupported_signature_protocol");

    [Fact]
    public void Token_event_without_a_signature_header_is_malformed() =>
        _normalizer.Parse(TokenBody("recurring.token.created"), NoHeaders)
            .RejectionReason.Should().Be("missing_signature");

    private static NotificationItem Item(string eventCode)
    {
        var item = new NotificationItem
        {
            PspReference = "psp-1",
            MerchantAccountCode = "merchant",
            MerchantReference = "reference-1",
            Amount = new ProviderAmount { Value = 1050, Currency = "USD" },
            EventCode = eventCode,
            Success = "true"
        };
        item.AdditionalData["hmacSignature"] = "supplied-signature";

        return item;
    }

    private static string StandardBody(params NotificationItem[] items) =>
        JsonSerializer.Serialize(
            new StandardWebhookRequest
            {
                NotificationItems = items
                    .Select(item => new NotificationContainer { Item = item })
                    .ToList()
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string TokenBody(string type) => $$"""
        {
          "eventId":"event-1",
          "type":"{{type}}",
          "createdAt":"2026-07-16T10:00:00Z",
          "data":{
            "merchantAccount":"merchant",
            "shopperReference":"shopper-1",
            "storedPaymentMethodId":"token-1",
            "type":"scheme"
          }
        }
        """;
}
