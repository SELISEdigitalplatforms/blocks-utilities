namespace Utility.DomainService.Geolocation.service
{
    /// <summary>
    /// Resolves IP addresses to a location through the configured provider.
    /// </summary>
    /// <remarks>
    /// Every method returns null, or omits an entry, when the provider cannot answer. A caller
    /// that needs a value for an unresolvable address supplies its own - the repository never
    /// invents one, because a fabricated "Unknown" country is indistinguishable from a real
    /// answer once it has been cached or written to an audit record.
    /// </remarks>
    public interface IGeolocationRepository
    {
        /// <summary>
        /// Resolves one address. Returns null when the address is unusable, the provider is not
        /// configured, or the provider call fails.
        /// </summary>
        Task<IpLookup?> ResolveIpToLocationAsync(
            string ipAddress,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves several addresses, dropping the ones that could not be resolved. Lookups are
        /// serialized against the provider's rate limit, so this is proportional to the number of
        /// cache misses rather than constant.
        /// </summary>
        Task<IpLookup[]> ResolveMultipleIpsToCountryAsync(
            IEnumerable<string> ipAddresses,
            CancellationToken cancellationToken = default);
    }
}
