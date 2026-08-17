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

    /// <summary>
    /// Drops every organization's entry for one of a tenant's providers.
    /// </summary>
    /// <remarks>
    /// One configuration can be cached under many keys, because entries are keyed by the
    /// organization that <em>asked</em> and a tenant-level configuration answers for every
    /// organization without one of its own. Evicting only the organization a row is stored
    /// under would leave every other organization holding the previous credentials —
    /// already decrypted — until the entry expired on its own.
    /// </remarks>
    void RemoveAll(string tenantId, string providerName);
}
