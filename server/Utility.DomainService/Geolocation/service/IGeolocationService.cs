using Microsoft.AspNetCore.Http;

namespace Utility.DomainService.Geolocation.service
{
    public interface IGeolocationService
    {
        Task<LocateIpResponse> LocateIpAsync(LocateIpRequest request);
        Task<LocateIpResponse> LocateAsync(LocateRequest request, IEnumerable<string> ipAddresses);
        IEnumerable<string> GetVisitorsIpAddresses(HttpContext httpContext);
    }
}