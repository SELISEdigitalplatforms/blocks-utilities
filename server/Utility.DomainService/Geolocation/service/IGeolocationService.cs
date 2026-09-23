using Microsoft.AspNetCore.Http;

namespace Utility.DomainService.Geolocation.service
{
    public interface IGeolocationService
    {
        Task<LocateIpResponse> LocateIpAsync(
            LocateIpRequest request,
            CancellationToken cancellationToken = default);

        Task<LocateIpResponse> LocateAsync(
            LocateRequest request,
            IEnumerable<string> ipAddresses,
            CancellationToken cancellationToken = default);

        IEnumerable<string> GetVisitorsIpAddresses(HttpContext httpContext);
    }
}
