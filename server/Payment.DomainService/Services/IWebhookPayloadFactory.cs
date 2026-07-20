using Payment.DomainService.Entities;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Services;

public interface IWebhookPayloadFactory
{
    PaymentWebhookPayload CreateStandard(
        string providerName,
        string paymentDetailId,
        NotificationItem item,
        bool success,
        string? refundId = null);

    PaymentWebhookPayload CreateToken(
        string providerName,
        TokenWebhookRequest request);
}
