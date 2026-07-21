namespace Payment.DomainService.Services;

public interface IPaymentRefundWebhookReferenceService
{
    bool TryCreate(
        string tenantId,
        string refundId,
        out string reference);

    bool TryParse(
        string? reference,
        out PaymentWebhookRoute route);
}
