using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Api.Utilities;
using Payment.DomainService.Responses;
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
        /// Lookups are serialized against the provider's rate limit and each cache miss carries a
        /// deliberate delay, so a request for ten addresses that are not cached takes seconds
        /// rather than milliseconds. Repeat lookups of the same address are served from the cache.
        ///
        /// Addresses that could not be located are omitted from the response, so it may hold fewer
        /// entries than were asked for. 404 means none of them could be located at all.
        ///
        /// Parameters:
        /// - IpAddresses: Collection of IP addresses to locate
        /// </remarks>
        /// <param name="request">The request containing IP addresses to locate</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Response containing geolocation information for the IP addresses</returns>
        [HttpGet]
        [Authorize]
        [ProducesResponseType(typeof(ApiResponse<IpLookup[]>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<IpLookup[]>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<IpLookup[]>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> LocateIp(
            [FromQuery] LocateIpRequest request,
            CancellationToken cancellationToken)
        {
            var correlationId = HttpContext.TraceIdentifier;
            var result = await _geolocationService.LocateIpAsync(request, cancellationToken);

            return result.ToActionResult(correlationId);
        }
    }
}
