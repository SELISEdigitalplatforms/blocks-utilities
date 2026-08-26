namespace Payment.DomainService.Providers;

public interface IPaymentMethodSetupRequestFactoryResolver
{
    /// <summary>
    /// The card-collection factory for <paramref name="providerName"/>, or
    /// <see langword="null"/> when that provider cannot collect a card without charging it.
    /// </summary>
    IPaymentMethodSetupRequestFactory? Resolve(string providerName);
}
