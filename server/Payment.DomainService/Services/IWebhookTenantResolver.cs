namespace Payment.DomainService.Services;

/// <summary>
/// Decodes the references this service mints when it starts a payment or identifies a shopper.
/// Providers echo them back verbatim, so decoding them is provider-neutral.
/// </summary>
public interface IWebhookTenantResolver
{
    /// <summary>Resolves the tenant and payment (or refund, or capture) a reference points at.</summary>
    bool TryResolvePayment(string? routingReference, out PaymentWebhookRoute route);

    /// <summary>Resolves the tenant a shopper reference belongs to.</summary>
    bool TryResolveTenant(string? shopperReference, out string tenantId);
}
