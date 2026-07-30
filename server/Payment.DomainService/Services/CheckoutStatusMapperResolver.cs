namespace Payment.DomainService.Services;

public sealed class CheckoutStatusMapperResolver : ICheckoutStatusMapperResolver
{
    private readonly IReadOnlyCollection<ICheckoutStatusMapper> _mappers;

    public CheckoutStatusMapperResolver(IEnumerable<ICheckoutStatusMapper> mappers)
    {
        _mappers = mappers.ToArray();
    }

    public ICheckoutStatusMapper? Resolve(string providerName) =>
        _mappers.FirstOrDefault(mapper => mapper.Supports(providerName));
}
