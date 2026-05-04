using Microsoft.AspNetCore.Http;

namespace Utility.DomainService.Geolocation.service
{
    public class GeolocationService : IGeolocationService
    {
        private readonly IGeolocationRepository _geolocationRepository;

        public GeolocationService(IGeolocationRepository geolocationRepository)
        {
            _geolocationRepository = geolocationRepository;
        }

        public async Task<LocateIpResponse> LocateIpAsync(LocateIpRequest request)
        {
            try
            {
                if (request.IpAddresses == null || !request.IpAddresses.Any())
                {
                    return new LocateIpResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "IP addresses are required"
                    };
                }

                var ipList = request.IpAddresses.ToList();
                if (ipList.Count > 10)
                {
                    return new LocateIpResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "Maximum 10 IP addresses allowed per request"
                    };
                }

                var ipLookups = await _geolocationRepository.ResolveMultipleIpsToCountryAsync(request.IpAddresses, request.UseCustomProvider);

                return new LocateIpResponse
                {
                    IpLookups = ipLookups,
                    IsSuccess = true
                };
            }
            catch (Exception ex)
            {
                return new LocateIpResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Failed to locate IP addresses: {ex.Message}"
                };
            }
        }

        public async Task<LocateIpResponse> LocateAsync(LocateRequest request, IEnumerable<string> ipAddresses)
        {
            try
            {
                if (ipAddresses == null || !ipAddresses.Any())
                {
                    return new LocateIpResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "No IP addresses found in request context"
                    };
                }

                var ipLookups = await _geolocationRepository.ResolveMultipleIpsToCountryAsync(ipAddresses, request.UseCustomProvider);

                return new LocateIpResponse
                {
                    IpLookups = ipLookups,
                    IsSuccess = true
                };
            }
            catch (Exception ex)
            {
                return new LocateIpResponse
                {
                    IsSuccess = false,
                    ErrorMessage = $"Failed to locate IP addresses: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Get visitor IP addresses from request headers (following HttpContextExtensions logic).
        /// </summary>
        /// <param name="httpContext">The HTTP context to extract IP addresses from</param>
        /// <returns>Collection of IP addresses</returns>
        public IEnumerable<string> GetVisitorsIpAddresses(HttpContext httpContext)
        {
            const string X_Forwarded_For_Header_Name = "X-Forwarded-For";
            
            var forwardedForHeader = httpContext.Request.Headers[X_Forwarded_For_Header_Name].FirstOrDefault();
            
            var visitorsIpAddress = string.IsNullOrWhiteSpace(forwardedForHeader) 
                ? httpContext.Connection.RemoteIpAddress?.ToString() ?? ""
                : forwardedForHeader;

            var visitorsIpAddresses = visitorsIpAddress
                .Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ipAddress => ipAddress.Trim());

            return visitorsIpAddresses;
        }
    }
}