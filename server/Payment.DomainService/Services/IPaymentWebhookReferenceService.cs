namespace Payment.DomainService.Services;

public interface IPaymentWebhookReferenceService
{
    bool TryCreate(
        string tenantId,
        string paymentDetailId,
        out string reference);

    bool TryParse(
        string? reference,
        out PaymentWebhookRoute route);
}
