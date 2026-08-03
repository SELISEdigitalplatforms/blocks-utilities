namespace Payment.DomainService.Providers;

public interface IProviderEndpointPolicyResolver
{
    /// <summary>
    /// The endpoint policy for <paramref name="providerName"/>, or <see langword="null"/> when
    /// no provider matches. Callers must treat <see langword="null"/> as "not allowed".
    /// </summary>
    IProviderEndpointPolicy? Resolve(string providerName);
}
