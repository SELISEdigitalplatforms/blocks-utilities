namespace Utility.DomainService.Geolocation
{
    public class IpLookup
    {
        /// <summary>
        /// String representing the initial IP address to be tracked by geolocation.
        /// </summary>
        public string? StartIp { get; set; }
        
        /// <summary>
        /// String representing the last IP address to be tracked by geolocation.
        /// </summary>
        public string? LastIp { get; set; }
        
        /// <summary>
        /// A conversion of the start IP, to make searches and queries simpler.
        /// </summary>
        public double StartIpNumber { get; set; }
        
        /// <summary>
        /// A conversion of the last IP, to make searches and queries simpler.
        /// </summary>
        public double LastIpNumber { get; set; }
        
        /// <summary>
        /// String representing the location code of the IP address.
        /// </summary>
        public string? LocationCode { get; set; }
        
        /// <summary>
        /// String representing the location code according to the input in registry.
        /// </summary>
        public string? LocationCodeAsRegistered { get; set; }
        
        /// <summary>
        /// String representing the continent code of the IP address.
        /// </summary>
        public string? ContinentCode { get; set; }
        
        /// <summary>
        /// String representing the country code of the IP address.
        /// </summary>
        public string? CountryCode { get; set; }
        
        /// <summary>
        /// String representing the name of the continent of the IP address.
        /// </summary>
        public string? ContinentName { get; set; }
        
        /// <summary>
        /// String representing the name of the country of the IP address.
        /// </summary>
        public string? CountryName { get; set; }
        
        /// <summary>
        /// String representing the name of the city of the IP address.
        /// </summary>
        public string? City { get; set; }
        
        /// <summary>
        /// String representing the name of the region of the IP address.
        /// </summary>
        public string? Region { get; set; }
        
        /// <summary>
        /// String representing the Latitude of the IP address.
        /// </summary>
        public double Latitude { get; set; }
        
        /// <summary>
        /// String representing the Longitude of the IP address.
        /// </summary>
        public double Longitude { get; set; }
        
        /// <summary>
        /// String representing CountryFlagSvgUrl of the IP address.
        /// </summary>
        public string? CountryFlagSvgUrl { get; set; }
        
        /// <summary>
        /// String representing CountryFlagPngUrl of the IP address.
        /// </summary>
        public string? CountryFlagPngUrl { get; set; }
        
        /// <summary>
        /// String representing IspName of the IP address.
        /// </summary>
        public string? IspName { get; set; }
    }
}