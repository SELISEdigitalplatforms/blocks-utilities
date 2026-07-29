using System.Globalization;
using System.Text;
using System.Text.Json;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Models.Webhooks;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Providers.Adyen;

/// <summary>
/// Reads Adyen's two webhook shapes. Standard notifications arrive as a batch, each item
/// individually signed over a canonical field list; token notifications arrive one per request
/// and are signed over the raw body.
/// </summary>
public sealed class AdyenWebhookNormalizer : IWebhookNormalizer
{
    private const int MaximumNotificationItems = 100;
    private const string TenantMetadataKey = "metadata.value_a";
    private const string SignatureKey = "hmacSignature";

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> TokenEvents =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "recurring.token.created",
            "recurring.token.alreadyExisting",
            "recurring.token.updated",
            "recurring.token.disabled"
        };

    private readonly IProviderFailureReasonMapper _failureReasons;

    public AdyenWebhookNormalizer(IProviderFailureReasonMapper failureReasons)
    {
        _failureReasons = failureReasons;
    }

    public bool Supports(string providerName) =>
        string.Equals(
            providerName,
            PaymentConstants.AdyenOnlineProvider,
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

        try
        {
            using var document = JsonDocument.Parse(rawBody);

            return document.RootElement.TryGetProperty("notificationItems", out _)
                ? ParseStandard(rawBody)
                : ParseToken(rawBody, headers);
        }
        catch (JsonException)
        {
            return WebhookParseResult.Malformed("invalid_json");
        }
    }

    private WebhookParseResult ParseStandard(string rawBody)
    {
        var request = JsonSerializer.Deserialize<StandardWebhookRequest>(
            rawBody,
            SerializerOptions);
        var containers = request?.NotificationItems;

        if (containers == null ||
            containers.Count is 0 or > MaximumNotificationItems ||
            containers.Any(container => container.Item == null))
        {
            return WebhookParseResult.Malformed("invalid_notification_collection");
        }

        var events = new List<ParsedWebhookEvent>(containers.Count);

        foreach (var item in containers.Select(container => container.Item!))
        {
            if (string.IsNullOrWhiteSpace(item.PspReference))
            {
                return WebhookParseResult.Malformed("missing_psp_reference");
            }

            if (string.IsNullOrWhiteSpace(item.EventCode))
            {
                return WebhookParseResult.Malformed("missing_event_code");
            }

            if (!bool.TryParse(item.Success, out var success))
            {
                return WebhookParseResult.Malformed("invalid_success_value");
            }

            if (!item.AdditionalData.TryGetValue(SignatureKey, out var supplied) ||
                string.IsNullOrWhiteSpace(supplied))
            {
                return WebhookParseResult.Malformed("missing_signature");
            }

            events.Add(new ParsedWebhookEvent
            {
                WebhookType = "standard",
                EventCode = item.EventCode!,
                Intent = ResolveIntent(item.EventCode!),
                EventDateUtc = item.EventDate?.ToUniversalTime() ?? DateTime.UtcNow,
                RoutingReference = item.MerchantReference ?? string.Empty,
                ProviderEventId = item.PspReference,
                EchoedTenantId = DecodeTenantMetadata(item),
                DeduplicationSeed =
                    $"{item.PspReference}:{item.EventCode}:{success}",
                Signature = new WebhookSignature(
                    BuildCanonicalPayload(item),
                    supplied,
                    AdyenWebhookSecrets.Standard),
                Payload = CreateStandardPayload(item, success)
            });
        }

        return WebhookParseResult.Parsed(events);
    }

    private WebhookParseResult ParseToken(
        string rawBody,
        IReadOnlyDictionary<string, string> headers)
    {
        var request = JsonSerializer.Deserialize<TokenWebhookRequest>(
            rawBody,
            SerializerOptions);

        if (request == null ||
            string.IsNullOrWhiteSpace(request.EffectiveEventId) ||
            string.IsNullOrWhiteSpace(request.Type) ||
            !TokenEvents.Contains(request.Type) ||
            request.Data.ValueKind != JsonValueKind.Object)
        {
            return WebhookParseResult.Malformed("invalid_event_envelope");
        }

        if (headers.TryGetValue("protocol", out var protocol) &&
            !string.IsNullOrWhiteSpace(protocol) &&
            !protocol.Equals("HmacSHA256", StringComparison.OrdinalIgnoreCase))
        {
            return WebhookParseResult.Malformed("unsupported_signature_protocol");
        }

        if (!headers.TryGetValue("hmacsignature", out var supplied) ||
            string.IsNullOrWhiteSpace(supplied))
        {
            return WebhookParseResult.Malformed("missing_signature");
        }

        var shopperReference = GetString(request.Data, "shopperReference");

        return WebhookParseResult.Parsed(
        [
            new ParsedWebhookEvent
            {
                WebhookType = "token",
                EventCode = request.Type!,
                Intent = WebhookIntent.StoredMethod,
                EventDateUtc = request.CreatedAt?.ToUniversalTime() ?? DateTime.UtcNow,
                RoutingReference = shopperReference ?? string.Empty,
                ProviderEventId = request.EffectiveEventId,
                DeduplicationSeed =
                    $"{request.EffectiveEventId}:{request.Type}",
                Signature = new WebhookSignature(
                    rawBody,
                    supplied,
                    AdyenWebhookSecrets.Token),
                Payload = CreateTokenPayload(request)
            }
        ]);
    }

    private static string BuildCanonicalPayload(NotificationItem item) =>
        string.Join(':',
        [
            Escape(item.PspReference),
            Escape(item.OriginalReference),
            Escape(item.MerchantAccountCode),
            Escape(item.MerchantReference),
            item.Amount?.Value.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Escape(item.Amount?.Currency),
            Escape(item.EventCode),
            Escape(item.Success)
        ]);

    private static WebhookIntent ResolveIntent(string eventCode) => eventCode switch
    {
        _ when Is(eventCode, "AUTHORISATION") => WebhookIntent.Authorization,
        _ when Is(eventCode, "REFUND") ||
               Is(eventCode, "REFUND_FAILED") ||
               Is(eventCode, "REFUNDED_REVERSED") ||
               Is(eventCode, "CANCEL_OR_REFUND") => WebhookIntent.Refund,
        _ when Is(eventCode, "CAPTURE") ||
               Is(eventCode, "CAPTURE_FAILED") => WebhookIntent.Capture,
        _ => WebhookIntent.Ignored
    };

    private static bool Is(string eventCode, string expected) =>
        eventCode.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private PaymentWebhookPayload CreateStandardPayload(
        NotificationItem item,
        bool success)
    {
        var token = Get(item.AdditionalData, "tokenization.storedPaymentMethodId") ??
                    Get(item.AdditionalData, "recurring.recurringDetailReference");
        var shopper = Get(item.AdditionalData, "tokenization.shopperReference") ??
                      Get(item.AdditionalData, "shopperReference") ??
                      Get(item.AdditionalData, "recurring.shopperReference");
        item.AdditionalData.TryGetValue("cardSummary", out var lastFour);
        item.AdditionalData.TryGetValue("expiryDate", out var expiry);

        var expiryParts = expiry?.Split('/');
        var failure = _failureReasons.Map(item.EventCode, success, item.Reason);

        return new PaymentWebhookPayload
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            MerchantAccount = item.MerchantAccountCode,
            MerchantReference = item.MerchantReference,
            PspReference = item.PspReference,
            OriginalPspReference = item.OriginalReference,
            Success = success,
            AmountMinorUnits = item.Amount?.Value,
            CurrencyCode = item.Amount?.Currency,
            ShopperReference = shopper,
            StoredPaymentMethodToken = token,
            PaymentMethodType = Get(item.AdditionalData, "paymentMethod") ?? "scheme",
            Brand = Get(item.AdditionalData, "paymentMethodVariant") ?? item.PaymentMethod,
            LastFour = SafeLastFour(lastFour),
            ExpiryMonth = expiryParts?.Length == 2 ? expiryParts[0] : null,
            ExpiryYear = expiryParts?.Length == 2 ? expiryParts[1] : null,
            FundingSource = Get(item.AdditionalData, "fundingSource"),
            IssuerCountry = Get(item.AdditionalData, "issuerCountry"),
            IssuerName = Get(item.AdditionalData, "issuerName"),
            AuthorizationCode = Get(item.AdditionalData, "authCode"),
            ProviderFailureCode = failure?.Code,
            ProviderFailureSummary = failure?.Summary,
            ModificationAction = Get(item.AdditionalData, "modification.action")
        };
    }

    private static PaymentWebhookPayload CreateTokenPayload(
        TokenWebhookRequest request) => new()
        {
            EventId = request.EffectiveEventId,
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            MerchantAccount = GetString(request.Data, "merchantAccount"),
            ShopperReference = GetString(request.Data, "shopperReference"),
            StoredPaymentMethodToken =
                GetString(request.Data, "storedPaymentMethodId") ??
                GetString(request.Data, "storedPaymentMethodToken"),
            PaymentMethodType = GetString(request.Data, "type") ?? "scheme",
            Brand = GetString(request.Data, "brand"),
            LastFour = SafeLastFour(
                GetString(request.Data, "lastFour") ??
                GetString(request.Data, "lastFourDigits")),
            ExpiryMonth = GetString(request.Data, "expiryMonth"),
            ExpiryYear = GetString(request.Data, "expiryYear"),
            FundingSource = GetString(request.Data, "fundingSource"),
            IssuerCountry = GetString(request.Data, "issuerCountry")
        };

    private static string? DecodeTenantMetadata(NotificationItem item)
    {
        if (!item.AdditionalData.TryGetValue(TenantMetadataKey, out var encoded) ||
            string.IsNullOrWhiteSpace(encoded) ||
            encoded.Length > 128)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            // An unreadable echo cannot confirm the tenant; surface it as a mismatch.
            return string.Empty;
        }
    }

    private static string? Get(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? SafeLastFour(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length >= 4 &&
        value[^4..].All(char.IsDigit)
            ? value[^4..]
            : null;

    private static string Escape(string? value) =>
        (value ?? string.Empty).Replace("\\", "\\\\").Replace(":", "\\:");
}
