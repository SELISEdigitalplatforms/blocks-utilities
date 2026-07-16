namespace Payment.DomainService.Services;

public sealed record PaymentWebhookRoute(
    string TenantId,
    string PaymentDetailId);
