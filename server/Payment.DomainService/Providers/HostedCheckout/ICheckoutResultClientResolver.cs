namespace Payment.DomainService.Providers.HostedCheckout;

public interface ICheckoutResultClientResolver
{
    /// <summary>
    /// The result client for <paramref name="providerName"/>, or <see langword="null"/> when
    /// no client serves that provider. Callers must treat <see langword="null"/> as
    /// "cannot observe".
    /// </summary>
    ICheckoutResultClient? Resolve(string providerName);
}
