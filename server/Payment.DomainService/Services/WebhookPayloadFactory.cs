using System.Text.Json;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Services;

public sealed class WebhookPayloadFactory : IWebhookPayloadFactory
{
    public PaymentWebhookPayload CreateStandard(
        string providerName,
        string paymentDetailId,
        NotificationItem item,
        bool success,
        string? refundId = null)
    {
        var token = Get(
                        item.AdditionalData,
                        "tokenization.storedPaymentMethodId") ??
                    Get(
                        item.AdditionalData,
                        "recurring.recurringDetailReference");
        var shopper = Get(
                          item.AdditionalData,
                          "tokenization.shopperReference") ??
                      Get(
                          item.AdditionalData,
                          "shopperReference") ??
                      Get(
                          item.AdditionalData,
                          "recurring.shopperReference");
        item.AdditionalData.TryGetValue(
            "cardSummary",
            out var lastFour);
        item.AdditionalData.TryGetValue(
            "expiryDate",
            out var expiry);

        var expiryParts = expiry?.Split('/');

        return new PaymentWebhookPayload
        {
            ProviderName = providerName,
            MerchantAccount = item.MerchantAccountCode,
            PaymentDetailId = paymentDetailId,
            RefundId = refundId,
            MerchantReference = item.MerchantReference,
            PspReference = item.PspReference,
            OriginalPspReference =
                item.OriginalReference,
            Success = success,
            AmountMinorUnits = item.Amount?.Value,
            CurrencyCode = item.Amount?.Currency,
            ShopperReference = shopper,
            StoredPaymentMethodToken = token,
            PaymentMethodType = Get(item.AdditionalData, "paymentMethod") ?? "scheme",
            Brand = Get(item.AdditionalData, "paymentMethodVariant") ??
                    item.PaymentMethod,
            LastFour = SafeLastFour(lastFour),
            ExpiryMonth = expiryParts?.Length == 2 ? expiryParts[0] : null,
            ExpiryYear = expiryParts?.Length == 2 ? expiryParts[1] : null,
            FundingSource = Get(item.AdditionalData, "fundingSource"),
            IssuerCountry = Get(item.AdditionalData, "issuerCountry"),
            IssuerName = Get(item.AdditionalData, "issuerName"),
            AuthorizationCode = Get(item.AdditionalData, "authCode")
        };
    }

    public PaymentWebhookPayload CreateToken(
        string providerName,
        TokenWebhookRequest request) => new()
        {
            EventId = request.EffectiveEventId,
            ProviderName = providerName,
            MerchantAccount = GetString(request.Data, "merchantAccount"),
            ShopperReference = GetString(request.Data, "shopperReference"),
            StoredPaymentMethodToken = GetString(request.Data, "storedPaymentMethodId") ??
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

    private static string? Get(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static string? GetString(
        JsonElement element,
        string name) =>
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
}
