namespace Payment.DomainService.Providers.HostedCheckout;

public interface IPaymentSessionClientResolver
{
    /// <summary>
    /// The session client for <paramref name="providerName"/>, or <see langword="null"/> when
    /// no client serves that provider. Callers must treat <see langword="null"/> as
    /// "cannot initiate".
    /// </summary>
    IPaymentSessionClient? Resolve(string providerName);
}
