using Payment.DomainService.Entities;

namespace Payment.DomainService.Services;

/// <summary>
/// Caches resolved provider configurations.
/// </summary>
/// <remarks>
/// Keyed by organization as well as tenant. Two organizations under one tenant may pay through
/// different merchant accounts, so a cache ignoring the organization would hand one
/// organization's configuration — and its credentials — to another.
/// </remarks>
public interface IPaymentProviderCache
{
    Task<PaymentProvider?> GetAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        Func<Task<PaymentProvider?>> loader);

    Task<PaymentProvider?> RefreshAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        Func<Task<PaymentProvider?>> loader);

    void Remove(
        string tenantId,
        string? organizationId,
        string providerName);
}
