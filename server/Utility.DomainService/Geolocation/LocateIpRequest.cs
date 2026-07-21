using Blocks.Genesis;

namespace Utility.DomainService.Geolocation
{
    public class LocateIpRequest 
    {
        /// <summary>
        /// String representing the IP addresses to be located.
        /// </summary>
        public IEnumerable<string>? IpAddresses { get; set; }
        
        /// <summary>
        /// Use custom ip lookup provider.
        /// </summary>
        public bool UseCustomProvider { get; set; } = false;
    }
}