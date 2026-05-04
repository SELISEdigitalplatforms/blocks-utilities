using Blocks.Genesis;

namespace Utility.DomainService.Geolocation
{
    public class LocateIpResponse : BaseResponse
    {
        /// <summary>
        /// Array of IP lookup results.
        /// </summary>
        public IpLookup[]? IpLookups { get; set; }
        
        /// <summary>
        /// Error message if operation failed.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}