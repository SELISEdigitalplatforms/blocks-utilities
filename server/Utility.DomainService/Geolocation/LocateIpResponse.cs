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

        /// <summary>
        /// Why the operation failed, for the Api to map onto a status code.
        /// </summary>
        /// <remarks>
        /// A kind rather than a parsed message: the Api used to have no way to tell a malformed
        /// request from an address nobody could locate, so every outcome left here as HTTP 200 and
        /// the caller had to read <see cref="BaseResponse.IsSuccess"/> to find out what happened.
        /// </remarks>
        public GeolocationFailureKind FailureKind { get; set; } = GeolocationFailureKind.None;
    }
}
