using Payment.DomainService.Entities;

namespace Payment.DomainService.Services;

public interface IPaymentProviderCache
{
    Task<PaymentProvider?> GetAsync(
        string tenantId,
        string providerName,
        Func<Task<PaymentProvider?>> loader);

    Task<PaymentProvider?> RefreshAsync(
        string tenantId,
        string providerName,
        Func<Task<PaymentProvider?>> loader);

    void Remove(
        string tenantId,
        string providerName);
}
