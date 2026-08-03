using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Services;

public sealed record ValidatedStandardWebhook(
    string TenantId,
    string PaymentDetailId,
    string ProviderName,
    NotificationItem Item,
    bool Success,
    string? RefundId = null,
    string? CaptureId = null);
