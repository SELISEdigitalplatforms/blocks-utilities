using Sms.DomainService.Dtos;

namespace Sms.DomainService.Services;

/// <summary>
/// The worker side of SMS. Every method expects the tenant on the ambient context
/// (<c>SmsTenantContext.Enter</c>) and throws on infrastructure failure so the caller's queue can
/// redeliver.
/// </summary>
public interface ISmsProcessingService
{
    /// <summary>One send round: every recipient still pending. Safe to call twice; the second is a no-op.</summary>
    Task ProcessSendAsync(string tenantId, string messageId, CancellationToken cancellationToken = default);

    Task CheckDeliveryAsync(string tenantId, string messageId, CancellationToken cancellationToken = default);

    /// <summary>False when no message holds the callback's provider message id.</summary>
    Task<bool> ApplyCallbackAsync(string tenantId, SmsDeliveryCallback callback, CancellationToken cancellationToken = default);
}
