namespace Payment.DomainService.Providers;

/// <summary>
/// Per-provider allowlist for outbound API endpoints, applied on top of the shared transport
/// safety checks in <see cref="Utilities.SafeHttpsUrl"/>. An implementation narrows what its
/// own provider may be called at; it can never widen the shared guarantees.
/// </summary>
public interface IProviderEndpointPolicy
{
    bool Supports(string providerName);

    /// <summary>Whether this provider may be called at <paramref name="endpointUrl"/>.</summary>
    bool IsAllowed(string? endpointUrl);
}
