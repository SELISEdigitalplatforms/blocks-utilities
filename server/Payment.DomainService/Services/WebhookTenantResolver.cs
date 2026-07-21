using System.Text;
using System.Text.Json;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Services;

public sealed class WebhookTenantResolver : IWebhookTenantResolver
{
    private const string TenantMetadataKey = "metadata.value_a";
    private readonly IPaymentWebhookReferenceService _references;
    private readonly IPaymentRefundWebhookReferenceService
        _refundReferences;
    private readonly IPaymentCaptureWebhookReferenceService
        _captureReferences;
    private readonly IShopperReferenceService _shopperReferences;

    public WebhookTenantResolver(
        IPaymentWebhookReferenceService references,
        IPaymentRefundWebhookReferenceService refundReferences,
        IPaymentCaptureWebhookReferenceService captureReferences,
        IShopperReferenceService shopperReferences)
    {
        _references = references;
        _refundReferences = refundReferences;
        _captureReferences = captureReferences;
        _shopperReferences = shopperReferences;
    }

    public bool TryResolveStandard(
        NotificationItem item,
        out PaymentWebhookRoute route) =>
        _references.TryParse(
            item.MerchantReference,
            out route) ||
        _refundReferences.TryParse(
            item.MerchantReference,
            out route) ||
        _captureReferences.TryParse(
            item.MerchantReference,
            out route);

    public bool TryResolveToken(
        TokenWebhookRequest request,
        out string tenantId)
    {
        tenantId = string.Empty;

        if (request.Data.ValueKind != JsonValueKind.Object ||
            !request.Data.TryGetProperty("shopperReference", out var shopperReference))
        {
            return false;
        }

        return _shopperReferences.TryResolveTenant(
            shopperReference.GetString(),
            out tenantId);
    }

    public bool IsMetadataConsistent(
        NotificationItem item,
        string tenantId)
    {
        if (!item.AdditionalData.TryGetValue(TenantMetadataKey, out var encodedTenant))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(encodedTenant) || encodedTenant.Length > 128)
        {
            return false;
        }

        try
        {
            var decodedTenant = Encoding.UTF8.GetString(
                Convert.FromBase64String(encodedTenant));

            return string.Equals(
                decodedTenant,
                tenantId,
                StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
