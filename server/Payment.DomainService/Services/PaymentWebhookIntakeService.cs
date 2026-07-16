using System.Text.Json;
using Microsoft.Extensions.Logging;
using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentWebhookIntakeService : IPaymentWebhookIntakeService
{
    private static readonly HashSet<string> TokenEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "recurring.token.created", "recurring.token.alreadyExisting", "recurring.token.updated", "recurring.token.disabled"
    };
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProviderCache _providers;
    private readonly IPaymentWebhookInboxRepository _inbox;
    private readonly IWebhookSignatureValidator _signatures;
    private readonly ILogger<PaymentWebhookIntakeService> _logger;

    public PaymentWebhookIntakeService(
        IPaymentRepository payments,
        IPaymentProviderCache providers,
        IPaymentWebhookInboxRepository inbox,
        IWebhookSignatureValidator signatures,
        ILogger<PaymentWebhookIntakeService> logger)
    {
        _payments = payments;
        _providers = providers;
        _inbox = inbox;
        _signatures = signatures;
        _logger = logger;
    }

    public async Task<WebhookIntakeOutcome> AcceptStandardAsync(
        string tenantId,
        StandardWebhookRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsSafeTenant(tenantId) || request.NotificationItems.Count is 0 or > 100 || request.NotificationItems.Any(x => x.Item == null))
            return WebhookIntakeOutcome.Malformed;
        var provider = await GetProviderAsync(tenantId, cancellationToken);
        if (provider == null) return WebhookIntakeOutcome.NotFound;
        var items = request.NotificationItems.Select(x => x.Item!).ToArray();
        if (string.IsNullOrWhiteSpace(provider.StandardWebhookHmacKey) || items.Any(item =>
                !_signatures.ValidateStandard(item, provider.StandardWebhookHmacKey, provider.PreviousStandardWebhookHmacKey)))
            return WebhookIntakeOutcome.Unauthorized;
        if (items.Any(x => !string.Equals(x.MerchantAccountCode, provider.MerchantId, StringComparison.Ordinal)))
            return WebhookIntakeOutcome.Unauthorized;

        try
        {
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.PspReference) || string.IsNullOrWhiteSpace(item.EventCode))
                    return WebhookIntakeOutcome.Malformed;
                var success = string.Equals(item.Success, "true", StringComparison.OrdinalIgnoreCase);
                var payload = CreateStandardPayload(provider.ProviderName, item, success);
                await _inbox.StoreAsync(new PaymentWebhookInbox
                {
                    TenantId = tenantId,
                    WebhookType = "standard",
                    EventCode = item.EventCode,
                    PspReference = item.PspReference,
                    MerchantReference = item.MerchantReference,
                    EventDateUtc = item.EventDate?.ToUniversalTime() ?? DateTime.UtcNow,
                    DeduplicationKey = PaymentHashing.HashSensitiveValue($"{tenantId}:{item.PspReference}:{item.EventCode}:{success}"),
                    NormalizedPayload = payload
                }, cancellationToken);
            }
            return WebhookIntakeOutcome.Accepted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Payment webhook persistence failed TenantHash={TenantHash} Type=standard ExceptionType={ExceptionType}",
                PaymentHashing.HashSensitiveValue(tenantId)[..16], ex.GetType().Name);
            return WebhookIntakeOutcome.StorageUnavailable;
        }
    }

    public async Task<WebhookIntakeOutcome> AcceptTokenAsync(
        string tenantId,
        string rawBody,
        string signature,
        CancellationToken cancellationToken)
    {
        if (!IsSafeTenant(tenantId) || string.IsNullOrWhiteSpace(rawBody) || string.IsNullOrWhiteSpace(signature))
            return WebhookIntakeOutcome.Malformed;
        var provider = await GetProviderAsync(tenantId, cancellationToken);
        if (provider == null) return WebhookIntakeOutcome.NotFound;
        if (string.IsNullOrWhiteSpace(provider.TokenWebhookHmacKey) ||
            !_signatures.ValidateToken(rawBody, signature, provider.TokenWebhookHmacKey, provider.PreviousTokenWebhookHmacKey))
            return WebhookIntakeOutcome.Unauthorized;
        try
        {
            var request = JsonSerializer.Deserialize<TokenWebhookRequest>(rawBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (request == null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Type) ||
                !TokenEvents.Contains(request.Type) || request.Data.ValueKind != JsonValueKind.Object)
                return WebhookIntakeOutcome.Malformed;
            var merchant = GetString(request.Data, "merchantAccount");
            if (!string.Equals(merchant, provider.MerchantId, StringComparison.Ordinal)) return WebhookIntakeOutcome.Unauthorized;
            var payload = CreateTokenPayload(provider.ProviderName, request);
            if (string.IsNullOrWhiteSpace(payload.StoredPaymentMethodToken) || string.IsNullOrWhiteSpace(payload.ShopperReference))
                return WebhookIntakeOutcome.Malformed;
            await _inbox.StoreAsync(new PaymentWebhookInbox
            {
                TenantId = tenantId,
                WebhookType = "token",
                EventCode = request.Type,
                EventDateUtc = request.CreatedAt?.ToUniversalTime() ?? DateTime.UtcNow,
                DeduplicationKey = PaymentHashing.HashSensitiveValue($"{tenantId}:{request.Id}:{request.Type}"),
                NormalizedPayload = payload
            }, cancellationToken);
            return WebhookIntakeOutcome.Accepted;
        }
        catch (JsonException) { return WebhookIntakeOutcome.Malformed; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Payment webhook persistence failed TenantHash={TenantHash} Type=token ExceptionType={ExceptionType}",
                PaymentHashing.HashSensitiveValue(tenantId)[..16], ex.GetType().Name);
            return WebhookIntakeOutcome.StorageUnavailable;
        }
    }

    private Task<PaymentProvider?> GetProviderAsync(string tenantId, CancellationToken cancellationToken) =>
        _providers.GetAsync(tenantId, PaymentConstants.AdyenOnlineProvider,
            () => _payments.GetProviderAsync(tenantId, PaymentConstants.AdyenOnlineProvider, cancellationToken));

    private static PaymentWebhookPayload CreateStandardPayload(string providerName, NotificationItem item, bool success)
    {
        item.AdditionalData.TryGetValue("recurring.recurringDetailReference", out var token);
        item.AdditionalData.TryGetValue("shopperReference", out var shopper);
        item.AdditionalData.TryGetValue("cardSummary", out var lastFour);
        item.AdditionalData.TryGetValue("expiryDate", out var expiry);
        var expiryParts = expiry?.Split('/');
        return new PaymentWebhookPayload
        {
            ProviderName = providerName,
            MerchantAccount = item.MerchantAccountCode,
            MerchantReference = item.MerchantReference,
            PspReference = item.PspReference,
            Success = success,
            AmountMinorUnits = item.Amount?.Value,
            CurrencyCode = item.Amount?.Currency,
            ShopperReference = shopper,
            StoredPaymentMethodToken = token,
            PaymentMethodType = Get(item.AdditionalData, "paymentMethod") ?? "scheme",
            Brand = Get(item.AdditionalData, "paymentMethodVariant"),
            LastFour = SafeLastFour(lastFour),
            ExpiryMonth = expiryParts?.Length == 2 ? expiryParts[0] : null,
            ExpiryYear = expiryParts?.Length == 2 ? expiryParts[1] : null,
            FundingSource = Get(item.AdditionalData, "fundingSource"),
            IssuerCountry = Get(item.AdditionalData, "issuerCountry"),
            IssuerName = Get(item.AdditionalData, "issuerName"),
            AuthorizationCode = Get(item.AdditionalData, "authCode")
        };
    }

    private static PaymentWebhookPayload CreateTokenPayload(string providerName, TokenWebhookRequest request) => new()
    {
        EventId = request.Id,
        ProviderName = providerName,
        MerchantAccount = GetString(request.Data, "merchantAccount"),
        ShopperReference = GetString(request.Data, "shopperReference"),
        StoredPaymentMethodToken = GetString(request.Data, "storedPaymentMethodId") ?? GetString(request.Data, "storedPaymentMethodToken"),
        PaymentMethodType = GetString(request.Data, "type") ?? "scheme",
        Brand = GetString(request.Data, "brand"),
        LastFour = SafeLastFour(GetString(request.Data, "lastFour") ?? GetString(request.Data, "lastFourDigits")),
        ExpiryMonth = GetString(request.Data, "expiryMonth"),
        ExpiryYear = GetString(request.Data, "expiryYear"),
        FundingSource = GetString(request.Data, "fundingSource"),
        IssuerCountry = GetString(request.Data, "issuerCountry")
    };

    private static string? Get(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? value : null;
    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? SafeLastFour(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length >= 4 && value[^4..].All(char.IsDigit) ? value[^4..] : null;
    private static bool IsSafeTenant(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.');
}
