using Utility.DomainService.Geolocation;

namespace Utility.DomainService.Geolocation.service
{
    public interface IGeolocationRepository
    {
        Task<bool> IsGeoRestrictionEnabledAsync(string tenantId);
        Task<bool> IsCountryBlockedAsync(string countryCode, string tenantId);
        Task<bool> IsUserBlockedFromCountryAsync(string countryCode, string userId, string tenantId);
        Task<bool> IsRoleBlockedFromCountryAsync(string countryCode, IEnumerable<string> roles, string tenantId);
        Task<IpLookup> ResolveIpToCountryAsync(IEnumerable<string> ipAddresses, string tenantId);
        Task<IpLookup[]> ResolveMultipleIpsToCountryAsync(IEnumerable<string> ipAddresses, bool useCustomProvider = false);
    }
}