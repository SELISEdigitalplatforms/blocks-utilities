namespace Payment.DomainService.Services;

public interface IPaymentTenantContextScopeFactory
{
    IDisposable Establish(string tenantId);
}
