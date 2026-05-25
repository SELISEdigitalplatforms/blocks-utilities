using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using Utility.DomainService.Geolocation.service;
using Utility.DomainService.Geolocation;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class GeolocationController : ControllerBase
    {
        private readonly IGeolocationService _geolocationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="GeolocationController"/> class.
        /// </summary>
        /// <param name="geolocationService">The geolocation service.</param>
        public GeolocationController(
            IGeolocationService geolocationService)
        {
            _geolocationService = geolocationService;
        }

        /// <summary>
        /// Request to get IP addresses location information.
        /// </summary>
        /// <remarks>
        /// Retrieves geolocation information for the specified IP addresses.
        /// Supports bulk lookup of multiple IP addresses (maximum 10 per request).
        /// 
        /// Parameters:
        /// - IpAddresses: Collection of IP addresses to locate
        /// - UseCustomProvider: Whether to use custom IP lookup provider
        /// </remarks>
        /// <param name="request">The request containing IP addresses to locate</param>
        /// <returns>Response containing geolocation information for the IP addresses</returns>
        [HttpGet]
        public Task<LocateIpResponse> LocateIp([FromQuery] LocateIpRequest request)
        {
            return _geolocationService.LocateIpAsync(request);
        }

        /// <summary>
        /// Locate IP addresses from the current request context.
        /// </summary>
        /// <remarks>
        /// Automatically extracts and locates IP addresses from the current HTTP request context.
        /// This is useful for getting geolocation information of the current visitor without
        /// explicitly specifying IP addresses.
        /// 
        /// The endpoint extracts IP addresses from:
        /// - X-Forwarded-For header (for requests through proxies/load balancers)
        /// - Direct connection remote IP address
        /// 
        /// Parameters:
        /// - UseCustomProvider: Whether to use custom IP lookup provider
        /// </remarks>
        /// <param name="request">The request parameters</param>
        /// <returns>Response containing geolocation information for the request IP addresses</returns>
        [HttpGet]
        public async Task<LocateIpResponse> Locate([FromQuery] LocateRequest request)
        {
            // Extract IP addresses from the current request context
            var ipAddresses = _geolocationService.GetVisitorsIpAddresses(HttpContext);
            
            return await _geolocationService.LocateAsync(request, ipAddresses);
        }
    }
}