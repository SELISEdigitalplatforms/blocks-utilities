using System.Text.Json;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.Webhooks;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Reads a Stripe event. Stripe sends one event per request and signs the whole body, so a
/// request always normalizes to a single event.
/// </summary>
public sealed class StripeWebhookNormalizer : IWebhookNormalizer
{
    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase);

    public WebhookParseResult Parse(
        string rawBody,
        IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return WebhookParseResult.Malformed("empty_body");
        }

        if (!headers.TryGetValue(StripeConstants.SignatureHeader, out var signature) ||
            string.IsNullOrWhiteSpace(signature))
        {
            return WebhookParseResult.Malformed("missing_signature");
        }

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            var eventId = GetString(root, "id");
            var eventType = GetString(root, "type");

            if (string.IsNullOrWhiteSpace(eventId) ||
                string.IsNullOrWhiteSpace(eventType) ||
                !root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("object", out var subject) ||
                subject.ValueKind != JsonValueKind.Object)
            {
                return WebhookParseResult.Malformed("invalid_event_envelope");
            }

            var intent = ResolveIntent(eventType!);

            return WebhookParseResult.Parsed(
            [
                new ParsedWebhookEvent
                {
                    WebhookType = "event",
                    EventCode = eventType!,
                    Intent = intent,
                    EventDateUtc = ReadCreated(root),
                    RoutingReference = ReadRoutingReference(subject),
                    ProviderEventId = eventId,
                    EchoedOrganizationId = GetMetadata(subject, StripeRoutingMetadata.OrganizationKey),
                    // Stripe event ids are unique per event, which is a stronger
                    // deduplication key than a composite of payload fields.
                    DeduplicationSeed = eventId!,
                    Signature = new WebhookSignature(
                        BuildSignedPayload(signature, rawBody),
                        signature,
                        StripeConstants.WebhookSecretName),
                    Payload = CreatePayload(eventType!, intent, subject)
                }
            ]);
        }
        catch (JsonException)
        {
            return WebhookParseResult.Malformed("invalid_json");
        }
    }

    /// <summary>
    /// Stripe signs the timestamp from its own header concatenated with the untouched body.
    /// The timestamp is echoed here so the verifier receives exactly the signed bytes.
    /// </summary>
    private static string BuildSignedPayload(string signatureHeader, string rawBody) =>
        StripeSignatureHeader.TryParse(signatureHeader, out var timestamp, out _)
            ? $"{timestamp}.{rawBody}"
            : rawBody;

    private static WebhookIntent ResolveIntent(string eventType) => eventType switch
    {
        "payment_intent.succeeded" => WebhookIntent.Authorization,
        "payment_intent.amount_capturable_updated" => WebhookIntent.Authorization,
        "payment_intent.payment_failed" => WebhookIntent.Authorization,
        "checkout.session.async_payment_succeeded" => WebhookIntent.Authorization,
        "checkout.session.async_payment_failed" => WebhookIntent.Authorization,

        // A completed session only reports that the shopper finished; the money is reported
        // by the payment_intent events above, so this is recorded but not acted on.
        "checkout.session.completed" => WebhookIntent.Ignored,
        "checkout.session.expired" => WebhookIntent.Cancelled,
        "payment_intent.canceled" => WebhookIntent.Cancelled,

        // A refunded charge reports the charge, which carries the *payment's* routing
        // reference rather than the refund's, so it cannot identify which refund settled.
        // Refunds settle from the refund object's own events below.
        // The only event that reports a capture. payment_intent.succeeded also fires, but it
        // deduplicates against the authorisation that preceded it — same event type, same
        // intent reference — so it can never move a manually captured payment on its own.
        "charge.captured" => WebhookIntent.Capture,

        "charge.refunded" => WebhookIntent.Ignored,

        // A card refund is usually born already succeeded and never updates again, so
        // creation is the only event that will ever report it.
        "refund.created" => WebhookIntent.Refund,
        "refund.updated" => WebhookIntent.Refund,
        "charge.refund.updated" => WebhookIntent.Refund,

        // A payment method object carries neither client_reference_id nor our metadata, so it
        // can never say which tenant or shopper it belongs to and cannot be routed. Cards are
        // recorded from the authorization event instead, which does carry both.
        "payment_method.attached" => WebhookIntent.Ignored,
        "payment_method.detached" => WebhookIntent.Ignored,

        _ => WebhookIntent.Ignored
    };

    /// <summary>
    /// The reference this service minted, echoed back by Stripe. Checkout sessions carry it
    /// as client_reference_id; other objects carry it in metadata.
    /// </summary>
    private static string ReadRoutingReference(JsonElement subject) =>
        GetString(subject, "client_reference_id") ??
        GetMetadata(subject, "tenant_reference") ??
        string.Empty;

    private static PaymentWebhookPayload CreatePayload(
        string eventType,
        WebhookIntent intent,
        JsonElement subject)
    {
        var succeeded = ResolveSuccess(eventType, subject);
        var card = subject.TryGetProperty("payment_method_details", out var details) &&
                   details.TryGetProperty("card", out var cardDetails)
            ? cardDetails
            : default;

        return new PaymentWebhookPayload
        {
            EventId = GetString(subject, "id"),
            ProviderName = PaymentConstants.StripeProvider,
            MerchantAccount = GetMetadata(subject, "merchant_account"),
            MerchantReference = ReadRoutingReference(subject),
            PspReference = ResolveProviderReference(subject, intent),
            OriginalPspReference = GetString(subject, "payment_intent"),
            Success = succeeded,
            FundsCaptured = ResolveFundsCaptured(eventType),
            AmountMinorUnits = ResolveAmount(subject, intent),
            CurrencyCode = GetString(subject, "currency")?.ToUpperInvariant(),
            // The reference this service derived, not Stripe's customer id. Storing a card
            // checks this against the reference recorded on the payment, and the two
            // identifiers are unrelated.
            ShopperReference = GetMetadata(subject, StripeRoutingMetadata.ShopperReferenceKey),
            ProviderPayerReference = GetString(subject, "customer"),

            // Stripe has no token object of its own: the saved card is the payment method the
            // intent was paid with, reported on the authorization event.
            StoredPaymentMethodToken = intent == WebhookIntent.Authorization
                ? GetString(subject, "payment_method")
                : null,
            PaymentMethodType = GetString(subject, "type") ?? "card",
            Brand = card.ValueKind == JsonValueKind.Object
                ? GetString(card, "brand")
                : null,
            LastFour = card.ValueKind == JsonValueKind.Object
                ? GetString(card, "last4")
                : null,
            ProviderFailureCode = ReadFailureCode(subject)
        };
    }

    /// <summary>
    /// Stripe reports outcome through the event name rather than a success flag, so the flag
    /// the rest of the system reads is derived here.
    /// </summary>
    private static bool? ResolveSuccess(string eventType, JsonElement subject) => eventType switch
    {
        "payment_intent.succeeded" => true,
        "payment_intent.amount_capturable_updated" => true,
        "checkout.session.async_payment_succeeded" => true,
        "payment_intent.payment_failed" => false,
        "checkout.session.async_payment_failed" => false,
        "payment_intent.canceled" => false,
        "checkout.session.expired" => false,
        "charge.captured" => true,
        "charge.refunded" => true,
        "refund.created" or "refund.updated" or "charge.refund.updated" =>
            string.Equals(GetString(subject, "status"), "succeeded", StringComparison.Ordinal),
        _ => null
    };

    /// <summary>
    /// The identifier this event is reporting on. A refund reports itself, since that is the
    /// reference recorded against the refund record — taking its payment_intent instead would
    /// overwrite the stored refund reference with the payment's. Everything else is tracked by
    /// the payment: a charge names its intent, an intent names itself.
    /// </summary>
    private static string? ResolveProviderReference(JsonElement subject, WebhookIntent intent) =>
        intent is WebhookIntent.Refund or WebhookIntent.Capture
            ? GetString(subject, "id")
            : GetString(subject, "payment_intent") ?? GetString(subject, "id");

    /// <summary>
    /// Whether the event says the money was taken rather than only held.
    /// </summary>
    /// <remarks>
    /// A succeeded intent means Stripe has the money, whether this service asked for it or
    /// someone captured it in Stripe's dashboard. An updated capturable amount means the
    /// opposite: authorised, nothing taken. Deciding this from the configured capture mode
    /// instead recorded every manually captured payment as merely authorised.
    /// </remarks>
    private static bool? ResolveFundsCaptured(string eventType) => eventType switch
    {
        "payment_intent.succeeded" => true,
        "checkout.session.async_payment_succeeded" => true,
        "payment_intent.amount_capturable_updated" => false,
        _ => null
    };

    /// <summary>
    /// What the payment is for. Deliberately not <c>amount_received</c>: that is how much has
    /// been captured so far, which is zero on an authorisation and less than the total on a
    /// partial capture, and reading it made those events fail the amount check.
    /// </summary>
    private static long? ResolveAmount(JsonElement subject, WebhookIntent intent)
    {
        // A captured charge reports both what was authorised and what was actually taken, and
        // a partial capture takes less. Only the captured figure describes the capture.
        var names = intent == WebhookIntent.Capture
            ? (string[])["amount_captured", "amount_total", "amount"]
            : (string[])["amount_total", "amount"];

        foreach (var name in names)
        {
            if (subject.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt64(out var amount))
            {
                return amount;
            }
        }

        return null;
    }

    private static string? ReadFailureCode(JsonElement subject) =>
        subject.TryGetProperty("last_payment_error", out var error) &&
        error.ValueKind == JsonValueKind.Object
            ? ProviderRejectionParser.SanitizeErrorCode(
                GetString(error, "decline_code") ?? GetString(error, "code"))
            : null;

    private static DateTime ReadCreated(JsonElement root) =>
        root.TryGetProperty("created", out var created) &&
        created.ValueKind == JsonValueKind.Number &&
        created.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : DateTime.UtcNow;

    private static string? GetMetadata(JsonElement subject, string name) =>
        subject.TryGetProperty("metadata", out var metadata) &&
        metadata.ValueKind == JsonValueKind.Object
            ? GetString(metadata, name)
            : null;

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
