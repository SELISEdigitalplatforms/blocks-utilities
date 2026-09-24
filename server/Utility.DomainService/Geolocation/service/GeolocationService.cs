using Microsoft.AspNetCore.Http;

namespace Utility.DomainService.Geolocation.service
{
    /// <summary>
    /// Validates the request and reports what the provider could resolve.
    /// </summary>
    /// <remarks>
    /// "Nothing resolved" is reported as a failure rather than as an empty success. A caller that
    /// receives <c>IsSuccess = true</c> with no lookups cannot tell a private address apart from a
    /// provider outage, and the audit records that consume this need that distinction.
    /// </remarks>
    public sealed class GeolocationService : IGeolocationService
    {
        private const int MaximumAddressesPerRequest = 10;

        private readonly IGeolocationRepository _geolocationRepository;

        public GeolocationService(IGeolocationRepository geolocationRepository)
        {
            ArgumentNullException.ThrowIfNull(geolocationRepository);

            _geolocationRepository = geolocationRepository;
        }

        public async Task<LocateIpResponse> LocateIpAsync(
            LocateIpRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.IpAddresses == null || !request.IpAddresses.Any())
            {
                return Failed("IP addresses are required", GeolocationFailureKind.Validation);
            }

            var ipAddresses = request.IpAddresses.ToList();

            if (ipAddresses.Count > MaximumAddressesPerRequest)
            {
                return Failed(
                    $"Maximum {MaximumAddressesPerRequest} IP addresses allowed per request",
                    GeolocationFailureKind.Validation);
            }

            return await ResolveAsync(ipAddresses, cancellationToken);
        }

        public async Task<LocateIpResponse> LocateAsync(
            LocateRequest request,
            IEnumerable<string> ipAddresses,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (ipAddresses == null || !ipAddresses.Any())
            {
                return Failed(
                    "No IP addresses found in request context",
                    GeolocationFailureKind.Validation);
            }

            return await ResolveAsync(
                ipAddresses.Take(MaximumAddressesPerRequest),
                cancellationToken);
        }

        /// <summary>
        /// Get visitor IP addresses from request headers (following HttpContextExtensions logic).
        /// </summary>
        /// <param name="httpContext">The HTTP context to extract IP addresses from</param>
        /// <returns>Collection of IP addresses</returns>
        public IEnumerable<string> GetVisitorsIpAddresses(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            const string X_Forwarded_For_Header_Name = "X-Forwarded-For";

            var forwardedForHeader = httpContext.Request.Headers[X_Forwarded_For_Header_Name].FirstOrDefault();

            var visitorsIpAddress = string.IsNullOrWhiteSpace(forwardedForHeader)
                ? httpContext.Connection.RemoteIpAddress?.ToString() ?? ""
                : forwardedForHeader;

            var visitorsIpAddresses = visitorsIpAddress
                .Split([','], StringSplitOptions.RemoveEmptyEntries)
                .Select(ipAddress => ipAddress.Trim());

            return visitorsIpAddresses;
        }

        private async Task<LocateIpResponse> ResolveAsync(
            IEnumerable<string> ipAddresses,
            CancellationToken cancellationToken)
        {
            var ipLookups = await _geolocationRepository.ResolveMultipleIpsToCountryAsync(
                ipAddresses,
                cancellationToken);

            // The repository drops what it could not resolve, so an empty array means every
            // address failed - a bad address, a private one, or a provider that would not answer.
            return ipLookups.Length == 0
                ? Failed("IP address could not be located", GeolocationFailureKind.NotFound)
                : new LocateIpResponse
                {
                    IpLookups = ipLookups,
                    IsSuccess = true
                };
        }

        private static LocateIpResponse Failed(
            string errorMessage,
            GeolocationFailureKind failureKind) =>
            new()
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                FailureKind = failureKind
            };
    }
}
