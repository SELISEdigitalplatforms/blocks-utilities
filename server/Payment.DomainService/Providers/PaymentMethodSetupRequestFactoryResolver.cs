namespace Payment.DomainService.Providers;

public sealed class PaymentMethodSetupRequestFactoryResolver :
    IPaymentMethodSetupRequestFactoryResolver
{
    private readonly IReadOnlyCollection<IPaymentMethodSetupRequestFactory> _factories;

    public PaymentMethodSetupRequestFactoryResolver(
        IEnumerable<IPaymentMethodSetupRequestFactory> factories)
    {
        _factories = factories.ToArray();
    }

    public IPaymentMethodSetupRequestFactory? Resolve(string providerName) =>
        _factories.FirstOrDefault(factory => factory.Supports(providerName));
}
