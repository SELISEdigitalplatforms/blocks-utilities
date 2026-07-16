using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Services;

public interface IWebhookTenantResolver
{
    bool TryResolveStandard(
        NotificationItem item,
        out PaymentWebhookRoute route);

    bool TryResolveToken(
        TokenWebhookRequest request,
        out string tenantId);

    bool IsMetadataConsistent(
        NotificationItem item,
        string tenantId);
}
