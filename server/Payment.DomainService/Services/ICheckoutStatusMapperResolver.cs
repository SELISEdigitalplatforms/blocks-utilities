namespace Payment.DomainService.Services;

public interface ICheckoutStatusMapperResolver
{
    ICheckoutStatusMapper? Resolve(string providerName);
}
