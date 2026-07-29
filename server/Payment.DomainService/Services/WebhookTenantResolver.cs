namespace Payment.DomainService.Services;

public sealed class WebhookTenantResolver : IWebhookTenantResolver
{
    private readonly IPaymentWebhookReferenceService _references;
    private readonly IPaymentRefundWebhookReferenceService _refundReferences;
    private readonly IPaymentCaptureWebhookReferenceService _captureReferences;
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

    public bool TryResolvePayment(
        string? routingReference,
        out PaymentWebhookRoute route) =>
        _references.TryParse(routingReference, out route) ||
        _refundReferences.TryParse(routingReference, out route) ||
        _captureReferences.TryParse(routingReference, out route);

    public bool TryResolveTenant(
        string? shopperReference,
        out string tenantId) =>
        _shopperReferences.TryResolveTenant(shopperReference, out tenantId);
}
