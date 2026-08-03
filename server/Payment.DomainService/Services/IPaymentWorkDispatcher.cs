namespace Payment.DomainService.Services;

public interface IPaymentWorkDispatcher
{
    Task DispatchAsync(
        string tenantId,
        bool includeRecovery,
        DateTimeOffset? scheduledAtUtc = null,
        CancellationToken cancellationToken = default);

    Task<bool> TryDispatchAsync(
        string tenantId,
        bool includeRecovery,
        DateTimeOffset? scheduledAtUtc = null,
        CancellationToken cancellationToken = default);
}
