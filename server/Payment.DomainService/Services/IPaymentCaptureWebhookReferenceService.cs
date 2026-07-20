namespace Payment.DomainService.Services;

public interface IPaymentCaptureWebhookReferenceService
{
    bool TryCreate(
        string tenantId,
        string captureId,
        out string reference);

    bool TryParse(
        string? reference,
        out PaymentWebhookRoute route);
}
