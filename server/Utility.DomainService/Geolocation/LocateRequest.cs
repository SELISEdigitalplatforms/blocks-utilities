using Blocks.Genesis;

namespace Utility.DomainService.Geolocation
{
    public class LocateRequest : IProjectKey
    {
        /// <summary>
        /// Project key for tenant context.
        /// </summary>
        public string? ProjectKey { get; set; }
    }
}