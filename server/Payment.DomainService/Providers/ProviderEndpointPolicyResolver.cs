namespace Payment.DomainService.Providers;

public sealed class ProviderEndpointPolicyResolver :
    IProviderEndpointPolicyResolver
{
    private readonly IReadOnlyCollection<
        IProviderEndpointPolicy> _policies;

    public ProviderEndpointPolicyResolver(
        IEnumerable<IProviderEndpointPolicy> policies)
    {
        _policies = policies.ToArray();
    }

    public IProviderEndpointPolicy? Resolve(
        string providerName) =>
        _policies.FirstOrDefault(policy =>
            policy.Supports(providerName));
}
